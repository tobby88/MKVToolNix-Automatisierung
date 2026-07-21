using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace MkvToolnixAutomatisierung.Services.Metadata;

/// <summary>
/// Fortschritt beim streamenden Import eines offiziellen IMDb-TSV-Datensatzes einschließlich
/// dateibezogener und über alle drei Archive gewichteter Prozentwerte.
/// </summary>
/// <param name="DatasetName">Technischer Name der aktuell gelesenen IMDb-Datei.</param>
/// <param name="DatasetNumber">Einbasierte Position der Datei im Importablauf.</param>
/// <param name="DatasetCount">Gesamtzahl der einzulesenden Dateien.</param>
/// <param name="ProcessedRowCount">Exakte Zahl der bisher gelesenen Datenzeilen der aktuellen Datei.</param>
/// <param name="DatasetProgressPercent">Aus der gelesenen komprimierten Dateiposition geschätzter Dateifortschritt.</param>
/// <param name="OverallProgressPercent">Nach Archivgröße gewichteter Fortschritt über alle Dateien.</param>
/// <param name="IsFinalizing">Kennzeichnet den anschließenden Aufbau der SQLite-Suchindizes.</param>
internal sealed record ImdbDatasetImportProgress(
    string DatasetName,
    int DatasetNumber,
    int DatasetCount,
    long ProcessedRowCount,
    double DatasetProgressPercent,
    double OverallProgressPercent,
    bool IsFinalizing = false);

/// <summary>
/// Baut aus den offiziellen IMDb-Dateien einen auf Serien und Episoden begrenzten SQLite-Index.
/// </summary>
internal sealed class ImdbDatasetIndexBuilder
{
    private static readonly TimeSpan ProgressUpdateInterval = TimeSpan.FromMilliseconds(200);

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

        var archivePaths = new[] { basicsArchivePath, episodesArchivePath, aliasesArchivePath };
        var archiveLengths = archivePaths.Select(path => new FileInfo(path).Length).ToArray();
        var totalArchiveBytes = archiveLengths.Sum();
        var completedArchiveBytes = 0L;

        await ImportBasicsAsync(
            connection,
            basicsArchivePath,
            CreateProgressContext("title.basics", 1, archiveLengths[0], completedArchiveBytes, totalArchiveBytes),
            progress,
            cancellationToken);
        completedArchiveBytes += archiveLengths[0];
        await ImportEpisodesAsync(
            connection,
            episodesArchivePath,
            CreateProgressContext("title.episode", 2, archiveLengths[1], completedArchiveBytes, totalArchiveBytes),
            progress,
            cancellationToken);
        completedArchiveBytes += archiveLengths[1];
        await ImportGermanAliasesAsync(
            connection,
            aliasesArchivePath,
            CreateProgressContext("title.akas", 3, archiveLengths[2], completedArchiveBytes, totalArchiveBytes),
            progress,
            cancellationToken);
        progress?.Report(new ImdbDatasetImportProgress(
            "SQLite-Suchindex",
            3,
            archivePaths.Length,
            0,
            100d,
            100d,
            IsFinalizing: true));
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
        ImdbDatasetProgressContext progressContext,
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
            progressContext,
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
        ImdbDatasetProgressContext progressContext,
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
            progressContext,
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
        ImdbDatasetProgressContext progressContext,
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
            progressContext,
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
        ImdbDatasetProgressContext progressContext,
        IProgress<ImdbDatasetImportProgress>? progress,
        Func<string[], long, SqliteCommand?> prepareCommand,
        CancellationToken cancellationToken)
    {
        await using var fileStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress, leaveOpen: false);
        using var reader = new StreamReader(gzipStream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 1024 * 1024);
        _ = await reader.ReadLineAsync(cancellationToken); // Kopfzeile

        long rowCount = 0;
        var lastProgressTimestamp = Stopwatch.GetTimestamp();
        ReportImportProgress(progress, progressContext, rowCount, fileStream.Position);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowCount++;
            var command = prepareCommand(line.Split('\t'), rowCount);
            if (command is not null)
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            if (Stopwatch.GetElapsedTime(lastProgressTimestamp) >= ProgressUpdateInterval)
            {
                ReportImportProgress(progress, progressContext, rowCount, fileStream.Position);
                lastProgressTimestamp = Stopwatch.GetTimestamp();
            }
        }

        ReportImportProgress(progress, progressContext, rowCount, progressContext.ArchiveLength);
    }

    private static ImdbDatasetProgressContext CreateProgressContext(
        string datasetName,
        int datasetNumber,
        long archiveLength,
        long completedArchiveBytes,
        long totalArchiveBytes) =>
        new(datasetName, datasetNumber, 3, archiveLength, completedArchiveBytes, totalArchiveBytes);

    private static void ReportImportProgress(
        IProgress<ImdbDatasetImportProgress>? progress,
        ImdbDatasetProgressContext context,
        long processedRowCount,
        long processedArchiveBytes)
    {
        if (progress is null)
        {
            return;
        }

        var boundedBytes = Math.Clamp(processedArchiveBytes, 0L, context.ArchiveLength);
        var datasetPercent = context.ArchiveLength > 0
            ? boundedBytes * 100d / context.ArchiveLength
            : 100d;
        var overallPercent = context.TotalArchiveBytes > 0
            ? (context.CompletedArchiveBytes + boundedBytes) * 100d / context.TotalArchiveBytes
            : datasetPercent;
        progress.Report(new ImdbDatasetImportProgress(
            context.DatasetName,
            context.DatasetNumber,
            context.DatasetCount,
            processedRowCount,
            Math.Clamp(datasetPercent, 0d, 100d),
            Math.Clamp(overallPercent, 0d, 100d)));
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

    private sealed record ImdbDatasetProgressContext(
        string DatasetName,
        int DatasetNumber,
        int DatasetCount,
        long ArchiveLength,
        long CompletedArchiveBytes,
        long TotalArchiveBytes);
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
    /// <summary>
    /// Kompakter Staffel-/Folgecode für die Kandidatenliste; IMDb kann dabei bewusst von TVDB abweichen.
    /// </summary>
    public string EpisodeCode => SeasonNumber is { } season && EpisodeNumber is { } episode
        ? $"S{season:00}E{episode:00}"
        : "ohne Nummer";

    /// <summary>
    /// Lesbare Einordnung des Titelanteils am Gesamtscore, damit der Benutzer Treffer nachvollziehen kann.
    /// </summary>
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
    private readonly object _cacheSync = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<ImdbSeriesCandidate>> _seriesCandidateCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<ImdbEpisodeCatalogEntry>> _episodeCatalogCache = new(StringComparer.OrdinalIgnoreCase);
    private ImdbDatabaseStamp? _cacheDatabaseStamp;

    public ImdbDatasetSearchService(string? databasePath = null)
    {
        _databasePath = databasePath ?? PortableAppStorage.ImdbDatabaseFilePath;
    }

    /// <summary>
    /// Gibt an, ob ein fertig aufgebauter lokaler IMDb-Index gelesen werden kann.
    /// </summary>
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

    /// <summary>
    /// Führt die potenziell größere SQLite-Suche außerhalb des aufrufenden UI-Threads aus.
    /// </summary>
    /// <param name="guess">Aktuell im Dialog sichtbare Suchangaben.</param>
    /// <param name="maximumResults">Maximale Zahl zurückzugebender Kandidaten.</param>
    /// <param name="cancellationToken">Abbruchsignal für das Starten oder Übernehmen der Hintergrundsuche.</param>
    /// <returns>Nach Match-Score sortierte lokale IMDb-Kandidaten.</returns>
    public Task<IReadOnlyList<ImdbEpisodeCandidate>> SearchEpisodeCandidatesAsync(
        EpisodeMetadataGuess guess,
        int maximumResults = 20,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => SearchEpisodeCandidates(guess, maximumResults), cancellationToken);

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

    /// <summary>
    /// Sucht nachvollziehbar bewertete Episodenkandidaten. Titelähnlichkeit ist das Hauptsignal;
    /// Staffel und Folge erhöhen den Score nur, weil IMDb- und TVDB-Nummerierungen abweichen können.
    /// </summary>
    /// <param name="guess">Lokale Serien-, Titel- und optionale Episodenerkennung.</param>
    /// <param name="maximumResults">Maximale Anzahl zurückzugebender Kandidaten.</param>
    /// <returns>Nach absteigendem Match-Score sortierte IMDb-Episoden.</returns>
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

        EnsureCachesMatchCurrentDatabase();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString());
        connection.Open();
        var seriesCandidates = _seriesCandidateCache.GetOrAdd(
            normalizedSeries,
            query => LoadSeriesCandidates(connection, query));
        var results = new List<ImdbEpisodeCandidate>();
        foreach (var series in seriesCandidates)
        {
            var episodeCatalog = _episodeCatalogCache.GetOrAdd(
                series.Id,
                parentId => LoadEpisodeCatalog(connection, parentId));
            results.AddRange(ScoreEpisodes(episodeCatalog, series, guess));
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
        // Getrennte Indexbereiche verhindern, dass SQLite bei einem großen OR/EXISTS-Ausdruck
        // zuerst Millionen Aliaszeilen mit der Titeltabelle verknüpft. INDEXED BY hält dabei
        // gerade den Aliaszweig zuverlässig auf dem selektiven normalized_title-Index.
        command.CommandText =
            """
            WITH candidate_ids(id, exact_match) AS (
                SELECT id, normalized_primary = $query
                FROM titles
                WHERE kind = 1 AND normalized_primary >= $query AND normalized_primary < $prefixUpper
                UNION ALL
                SELECT id, normalized_original = $query
                FROM titles
                WHERE kind = 1 AND normalized_original >= $query AND normalized_original < $prefixUpper
                UNION ALL
                SELECT a.title_id, a.normalized_title = $query
                FROM aliases a INDEXED BY ix_aliases_normalized
                INNER JOIN titles t ON t.id = a.title_id
                WHERE t.kind = 1 AND a.normalized_title >= $query AND a.normalized_title < $prefixUpper
            )
            SELECT t.id, t.primary_title, MAX(candidate_ids.exact_match) AS exact_match
            FROM candidate_ids
            INNER JOIN titles t ON t.id = candidate_ids.id
            GROUP BY t.id, t.primary_title
            ORDER BY exact_match DESC, t.primary_title
            LIMIT 8;
            """;
        command.Parameters.AddWithValue("$query", normalizedSeries);
        command.Parameters.AddWithValue("$prefixUpper", normalizedSeries + '\uffff');
        using var reader = command.ExecuteReader();
        var results = new List<ImdbSeriesCandidate>();
        while (reader.Read())
        {
            results.Add(new ImdbSeriesCandidate(reader.GetString(0), reader.GetString(1), reader.GetInt32(2) == 1));
        }

        return results;
    }

    private static IReadOnlyList<ImdbEpisodeCatalogEntry> LoadEpisodeCatalog(
        SqliteConnection connection,
        string parentId)
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
        command.Parameters.AddWithValue("$parent", parentId);
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

        return episodes.Values
            .Select(episode => new ImdbEpisodeCatalogEntry(
                episode.Id,
                episode.PrimaryTitle,
                episode.SeasonNumber,
                episode.EpisodeNumber,
                episode.Titles.ToArray()))
            .ToArray();
    }

    private static IEnumerable<ImdbEpisodeCandidate> ScoreEpisodes(
        IReadOnlyList<ImdbEpisodeCatalogEntry> episodes,
        ImdbSeriesCandidate series,
        EpisodeMetadataGuess guess)
    {
        foreach (var episode in episodes)
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

    private void EnsureCachesMatchCurrentDatabase()
    {
        var file = new FileInfo(_databasePath);
        var currentStamp = new ImdbDatabaseStamp(file.Length, file.LastWriteTimeUtc.Ticks);
        if (_cacheDatabaseStamp == currentStamp)
        {
            return;
        }

        lock (_cacheSync)
        {
            if (_cacheDatabaseStamp == currentStamp)
            {
                return;
            }

            _seriesCandidateCache.Clear();
            _episodeCatalogCache.Clear();
            _cacheDatabaseStamp = currentStamp;
        }
    }

    private sealed record ImdbSeriesCandidate(string Id, string Title, bool ExactTitleMatch);

    private sealed record ImdbEpisodeCatalogEntry(
        string Id,
        string PrimaryTitle,
        int? SeasonNumber,
        int? EpisodeNumber,
        IReadOnlyList<string> Titles);

    private sealed record ImdbDatabaseStamp(long Length, long LastWriteTimeUtcTicks);

    private sealed record MutableEpisodeCandidate(
        string Id,
        string PrimaryTitle,
        int? SeasonNumber,
        int? EpisodeNumber)
    {
        public List<string> Titles { get; } = [];
    }
}
