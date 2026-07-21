using System.Globalization;
using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace MkvToolnixAutomatisierung.Services.Metadata;

/// <summary>
/// Fortschritt beim streamenden Import eines offiziellen IMDb-TSV-Datensatzes.
/// </summary>
internal sealed record ImdbDatasetImportProgress(string DatasetName, long ProcessedRowCount);

/// <summary>
/// Baut aus den offiziellen IMDb-Dateien einen auf Serien und Episoden begrenzten SQLite-Index.
/// </summary>
internal sealed class ImdbDatasetIndexBuilder
{
    private const int ProgressRowInterval = 25_000;

    /// <summary>
    /// Erstellt einen vollständig neuen Index. Die aufrufende Verwaltung entscheidet anschließend
    /// atomar, ob dieser die bisherige produktive Datenbank ersetzen darf.
    /// </summary>
    public async Task BuildAsync(
        string databasePath,
        string basicsArchivePath,
        string episodesArchivePath,
        string aliasesArchivePath,
        string versionToken,
        IProgress<ImdbDatasetImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        File.Delete(databasePath);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            // Der neue Index wird direkt nach dem Import atomar verschoben. Ein gepoolter
            // EXCLUSIVE-Handle würde die Datei unter Windows trotz Dispose weiter sperren.
            Pooling = false
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ExecuteNonQueryAsync(connection, "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA temp_store=MEMORY; PRAGMA locking_mode=EXCLUSIVE;", cancellationToken);
        await ExecuteNonQueryAsync(
            connection,
            """
            CREATE TABLE titles (
                id TEXT PRIMARY KEY,
                kind INTEGER NOT NULL,
                primary_title TEXT NOT NULL,
                original_title TEXT NOT NULL,
                normalized_primary TEXT NOT NULL,
                normalized_original TEXT NOT NULL,
                start_year INTEGER NULL,
                parent_id TEXT NULL,
                season_number INTEGER NULL,
                episode_number INTEGER NULL
            ) WITHOUT ROWID;
            CREATE TABLE aliases (
                title_id TEXT NOT NULL,
                title TEXT NOT NULL,
                normalized_title TEXT NOT NULL,
                region TEXT NULL,
                language TEXT NULL
            );
            CREATE TABLE metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            ) WITHOUT ROWID;
            """,
            cancellationToken);

        await ImportBasicsAsync(connection, basicsArchivePath, progress, cancellationToken);
        await ImportEpisodesAsync(connection, episodesArchivePath, progress, cancellationToken);
        await ImportGermanAliasesAsync(connection, aliasesArchivePath, progress, cancellationToken);
        await ExecuteNonQueryAsync(
            connection,
            """
            DELETE FROM titles WHERE kind = 2 AND parent_id IS NULL;
            CREATE INDEX ix_titles_kind_primary ON titles(kind, normalized_primary);
            CREATE INDEX ix_titles_kind_original ON titles(kind, normalized_original);
            CREATE INDEX ix_titles_parent ON titles(parent_id, season_number, episode_number);
            CREATE INDEX ix_aliases_normalized ON aliases(normalized_title);
            CREATE INDEX ix_aliases_title_id ON aliases(title_id);
            """,
            cancellationToken);

        await using var metadataCommand = connection.CreateCommand();
        metadataCommand.CommandText = "INSERT INTO metadata(key, value) VALUES ('version', $version), ('builtUtc', $builtUtc);";
        metadataCommand.Parameters.AddWithValue("$version", versionToken);
        metadataCommand.Parameters.AddWithValue("$builtUtc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await metadataCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ImportBasicsAsync(
        SqliteConnection connection,
        string archivePath,
        IProgress<ImdbDatasetImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            INSERT INTO titles(
                id, kind, primary_title, original_title, normalized_primary, normalized_original, start_year)
            VALUES($id, $kind, $primary, $original, $normalizedPrimary, $normalizedOriginal, $year);
            """;
        var id = command.Parameters.Add("$id", SqliteType.Text);
        var kind = command.Parameters.Add("$kind", SqliteType.Integer);
        var primary = command.Parameters.Add("$primary", SqliteType.Text);
        var original = command.Parameters.Add("$original", SqliteType.Text);
        var normalizedPrimary = command.Parameters.Add("$normalizedPrimary", SqliteType.Text);
        var normalizedOriginal = command.Parameters.Add("$normalizedOriginal", SqliteType.Text);
        var year = command.Parameters.Add("$year", SqliteType.Integer);
        command.Prepare();

        await ReadGzipTsvAsync(
            archivePath,
            "title.basics",
            progress,
            (columns, _) =>
            {
                if (columns.Length < 6 || !TryMapTitleKind(columns[1], out var mappedKind))
                {
                    return null;
                }

                var primaryTitle = columns[2];
                var originalTitle = columns[3];
                id.Value = columns[0];
                kind.Value = mappedKind;
                primary.Value = primaryTitle;
                original.Value = originalTitle;
                normalizedPrimary.Value = EpisodeMetadataMatchingHeuristics.NormalizeText(primaryTitle);
                normalizedOriginal.Value = EpisodeMetadataMatchingHeuristics.NormalizeText(originalTitle);
                year.Value = TryParseNullableInt(columns[5]) is { } parsedYear ? parsedYear : DBNull.Value;
                return command;
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ImportEpisodesAsync(
        SqliteConnection connection,
        string archivePath,
        IProgress<ImdbDatasetImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            UPDATE titles
            SET parent_id = $parent, season_number = $season, episode_number = $episode
            WHERE id = $id AND kind = 2;
            """;
        var id = command.Parameters.Add("$id", SqliteType.Text);
        var parent = command.Parameters.Add("$parent", SqliteType.Text);
        var season = command.Parameters.Add("$season", SqliteType.Integer);
        var episode = command.Parameters.Add("$episode", SqliteType.Integer);
        command.Prepare();

        await ReadGzipTsvAsync(
            archivePath,
            "title.episode",
            progress,
            (columns, _) =>
            {
                if (columns.Length < 4)
                {
                    return null;
                }

                id.Value = columns[0];
                parent.Value = columns[1];
                season.Value = TryParseNullableInt(columns[2]) is { } parsedSeason ? parsedSeason : DBNull.Value;
                episode.Value = TryParseNullableInt(columns[3]) is { } parsedEpisode ? parsedEpisode : DBNull.Value;
                return command;
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ImportGermanAliasesAsync(
        SqliteConnection connection,
        string archivePath,
        IProgress<ImdbDatasetImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            INSERT INTO aliases(title_id, title, normalized_title, region, language)
            SELECT $id, $title, $normalized, $region, $language
            WHERE EXISTS (SELECT 1 FROM titles WHERE id = $id);
            """;
        var id = command.Parameters.Add("$id", SqliteType.Text);
        var title = command.Parameters.Add("$title", SqliteType.Text);
        var normalized = command.Parameters.Add("$normalized", SqliteType.Text);
        var region = command.Parameters.Add("$region", SqliteType.Text);
        var language = command.Parameters.Add("$language", SqliteType.Text);
        command.Prepare();

        await ReadGzipTsvAsync(
            archivePath,
            "title.akas",
            progress,
            (columns, _) =>
            {
                if (columns.Length < 5
                    || (!string.Equals(columns[3], "DE", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(columns[4], "de", StringComparison.OrdinalIgnoreCase)))
                {
                    return null;
                }

                var aliasTitle = columns[2];
                id.Value = columns[0];
                title.Value = aliasTitle;
                normalized.Value = EpisodeMetadataMatchingHeuristics.NormalizeText(aliasTitle);
                region.Value = ToDatabaseNullable(columns[3]);
                language.Value = ToDatabaseNullable(columns[4]);
                return command;
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ReadGzipTsvAsync(
        string archivePath,
        string datasetName,
        IProgress<ImdbDatasetImportProgress>? progress,
        Func<string[], long, SqliteCommand?> prepareCommand,
        CancellationToken cancellationToken)
    {
        await using var fileStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress, leaveOpen: false);
        using var reader = new StreamReader(gzipStream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 1024 * 1024);
        _ = await reader.ReadLineAsync(cancellationToken); // Kopfzeile

        long rowCount = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowCount++;
            var command = prepareCommand(line.Split('\t'), rowCount);
            if (command is not null)
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            if (rowCount % ProgressRowInterval == 0)
            {
                progress?.Report(new ImdbDatasetImportProgress(datasetName, rowCount));
            }
        }

        progress?.Report(new ImdbDatasetImportProgress(datasetName, rowCount));
    }

    private static bool TryMapTitleKind(string value, out int kind)
    {
        if (string.Equals(value, "tvSeries", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "tvMiniSeries", StringComparison.OrdinalIgnoreCase))
        {
            kind = 1;
            return true;
        }

        if (string.Equals(value, "tvEpisode", StringComparison.OrdinalIgnoreCase))
        {
            kind = 2;
            return true;
        }

        kind = 0;
        return false;
    }

    private static int? TryParseNullableInt(string value) =>
        !string.Equals(value, @"\N", StringComparison.Ordinal)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static object ToDatabaseNullable(string value) =>
        string.Equals(value, @"\N", StringComparison.Ordinal) ? DBNull.Value : value;

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>
/// Ein lokal gefundener IMDb-Episodenkandidat inklusive nachvollziehbarer Matchingmerkmale.
/// </summary>
internal sealed record ImdbEpisodeCandidate(
    string ImdbId,
    string SeriesTitle,
    string EpisodeTitle,
    int? SeasonNumber,
    int? EpisodeNumber,
    int Score,
    int TitleSimilarity,
    bool SeriesTitleMatchedExactly)
{
    public string EpisodeCode => SeasonNumber is { } season && EpisodeNumber is { } episode
        ? $"S{season:00}E{episode:00}"
        : "ohne Nummer";

    public string MatchQualityText => TitleSimilarity >= 30
        ? "Titel exakt"
        : TitleSimilarity >= 22
            ? "Titel ähnlich"
            : "Nummerntreffer";

    /// <summary>
    /// Nur ein exakter Serien- und Episodentitel ist ohne Benutzerentscheidung stark genug.
    /// Der aufrufende Workflow prüft zusätzlich den Abstand zum zweitbesten Treffer.
    /// </summary>
    public bool IsStrongAutomaticMatch => SeriesTitleMatchedExactly && TitleSimilarity >= 30;
}

/// <summary>
/// Durchsucht den optionalen SQLite-Index nach IMDb-Episoden, ohne Netzwerkzugriff auszuführen.
/// </summary>
internal sealed class ImdbDatasetSearchService
{
    private const int MinimumAutomaticScoreGap = 8;
    private readonly string _databasePath;

    public ImdbDatasetSearchService(string? databasePath = null)
    {
        _databasePath = databasePath ?? PortableAppStorage.ImdbDatabaseFilePath;
    }

    public bool IsAvailable => File.Exists(_databasePath);

    /// <summary>
    /// Liefert nur dann einen automatisch verwendbaren Treffer, wenn Titel und Serie exakt passen
    /// und kein nahezu gleich guter Kandidat die Zuordnung mehrdeutig macht.
    /// </summary>
    public bool TryFindAutomaticEpisode(EpisodeMetadataGuess guess, out ImdbEpisodeCandidate? candidate)
    {
        var candidates = SearchEpisodeCandidates(guess, maximumResults: 2);
        candidate = SelectAutomaticCandidate(candidates);
        return candidate is not null;
    }

    internal static ImdbEpisodeCandidate? SelectAutomaticCandidate(IReadOnlyList<ImdbEpisodeCandidate> candidates)
    {
        if (candidates.Count == 0 || !candidates[0].IsStrongAutomaticMatch)
        {
            return null;
        }

        return candidates.Count == 1 || candidates[0].Score - candidates[1].Score >= MinimumAutomaticScoreGap
            ? candidates[0]
            : null;
    }

    public IReadOnlyList<ImdbEpisodeCandidate> SearchEpisodeCandidates(EpisodeMetadataGuess guess, int maximumResults = 20)
    {
        ArgumentNullException.ThrowIfNull(guess);
        if (!IsAvailable || maximumResults <= 0)
        {
            return [];
        }

        var normalizedSeries = EpisodeMetadataMatchingHeuristics.NormalizeText(guess.SeriesName);
        if (string.IsNullOrWhiteSpace(normalizedSeries))
        {
            return [];
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString());
        connection.Open();
        var seriesCandidates = LoadSeriesCandidates(connection, normalizedSeries);
        var results = new List<ImdbEpisodeCandidate>();
        foreach (var series in seriesCandidates)
        {
            results.AddRange(LoadAndScoreEpisodes(connection, series, guess));
        }

        return results
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.SeasonNumber ?? int.MaxValue)
            .ThenBy(candidate => candidate.EpisodeNumber ?? int.MaxValue)
            .ThenBy(candidate => candidate.EpisodeTitle, StringComparer.OrdinalIgnoreCase)
            .Take(maximumResults)
            .ToArray();
    }

    private static IReadOnlyList<ImdbSeriesCandidate> LoadSeriesCandidates(SqliteConnection connection, string normalizedSeries)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT t.id, t.primary_title,
                CASE WHEN t.normalized_primary = $query OR t.normalized_original = $query
                          OR EXISTS (SELECT 1 FROM aliases a WHERE a.title_id = t.id AND a.normalized_title = $query)
                     THEN 1 ELSE 0 END AS exact_match
            FROM titles t
            WHERE t.kind = 1 AND (
                t.normalized_primary = $query OR t.normalized_original = $query
                OR t.normalized_primary LIKE $prefix OR t.normalized_original LIKE $prefix
                OR EXISTS (SELECT 1 FROM aliases a WHERE a.title_id = t.id
                           AND (a.normalized_title = $query OR a.normalized_title LIKE $prefix)))
            ORDER BY exact_match DESC, t.primary_title
            LIMIT 8;
            """;
        command.Parameters.AddWithValue("$query", normalizedSeries);
        command.Parameters.AddWithValue("$prefix", normalizedSeries + "%");
        using var reader = command.ExecuteReader();
        var results = new List<ImdbSeriesCandidate>();
        while (reader.Read())
        {
            results.Add(new ImdbSeriesCandidate(reader.GetString(0), reader.GetString(1), reader.GetInt32(2) == 1));
        }

        return results;
    }

    private static IEnumerable<ImdbEpisodeCandidate> LoadAndScoreEpisodes(
        SqliteConnection connection,
        ImdbSeriesCandidate series,
        EpisodeMetadataGuess guess)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT t.id, t.primary_title, t.season_number, t.episode_number, a.title
            FROM titles t
            LEFT JOIN aliases a ON a.title_id = t.id
            WHERE t.kind = 2 AND t.parent_id = $parent
            ORDER BY t.id;
            """;
        command.Parameters.AddWithValue("$parent", series.Id);
        using var reader = command.ExecuteReader();
        var episodes = new Dictionary<string, MutableEpisodeCandidate>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            var id = reader.GetString(0);
            if (!episodes.TryGetValue(id, out var episode))
            {
                episode = new MutableEpisodeCandidate(
                    id,
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3));
                episodes.Add(id, episode);
            }

            if (!reader.IsDBNull(4))
            {
                episode.Titles.Add(reader.GetString(4));
            }
        }

        foreach (var episode in episodes.Values)
        {
            var titleSimilarity = episode.Titles
                .Append(episode.PrimaryTitle)
                .Select(title => EpisodeMetadataMatchingHeuristics.CalculateTitleSimilarity(guess.EpisodeTitle, title))
                .DefaultIfEmpty(0)
                .Max();
            var seasonMatched = int.TryParse(guess.SeasonNumber, out var season) && episode.SeasonNumber == season;
            var episodeMatched = int.TryParse(guess.EpisodeNumber, out var number) && episode.EpisodeNumber == number;
            if (titleSimilarity < 22 && !(seasonMatched && episodeMatched))
            {
                continue;
            }

            var score = titleSimilarity
                + (series.ExactTitleMatch ? 35 : 12)
                + (seasonMatched ? 8 : 0)
                + (episodeMatched ? 12 : 0);
            yield return new ImdbEpisodeCandidate(
                episode.Id,
                series.Title,
                episode.PrimaryTitle,
                episode.SeasonNumber,
                episode.EpisodeNumber,
                score,
                titleSimilarity,
                series.ExactTitleMatch);
        }
    }

    private sealed record ImdbSeriesCandidate(string Id, string Title, bool ExactTitleMatch);

    private sealed record MutableEpisodeCandidate(
        string Id,
        string PrimaryTitle,
        int? SeasonNumber,
        int? EpisodeNumber)
    {
        public List<string> Titles { get; } = [];
    }
}
