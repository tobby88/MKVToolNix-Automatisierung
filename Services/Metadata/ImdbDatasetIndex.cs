using System.Buffers;
using System.Buffers.Text;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;

namespace MkvToolnixAutomatisierung.Services.Metadata;

/// <summary>
/// Fortschritt beim streamenden Import eines offiziellen IMDb-TSV-Datensatzes einschließlich
/// dateibezogener und über alle drei Archive gewichteter Prozentwerte.
/// </summary>
/// <param name="DatasetName">Technischer Datensatzname oder Beschreibung des aktuellen Abschluss-Schritts.</param>
/// <param name="DatasetNumber">Einbasierte Position innerhalb der Import- oder Abschlussphase.</param>
/// <param name="DatasetCount">Gesamtzahl der Schritte in der aktuellen Phase.</param>
/// <param name="ProcessedRowCount">Exakte Zahl der bisher gelesenen Datenzeilen der aktuellen Datei.</param>
/// <param name="DatasetProgressPercent">Aus der gelesenen komprimierten Dateiposition geschätzter Dateifortschritt.</param>
/// <param name="OverallProgressPercent">Nach Archivgröße gewichteter Fortschritt über alle Dateien.</param>
/// <param name="IsFinalizing">Kennzeichnet einen nicht prozentual messbaren SQLite-Abschluss-Schritt.</param>
/// <param name="ImportedRowCount">Zahl der tatsächlich in den Arbeitsindex übernommenen Datensätze.</param>
/// <param name="ProcessedRowsPerSecond">Seit Beginn der aktuellen Datei durchschnittlich gelesene Datensätze pro Sekunde.</param>
internal sealed record ImdbDatasetImportProgress(
    string DatasetName,
    int DatasetNumber,
    int DatasetCount,
    long ProcessedRowCount,
    double DatasetProgressPercent,
    double OverallProgressPercent,
    bool IsFinalizing = false,
    long ImportedRowCount = 0,
    double ProcessedRowsPerSecond = 0d);

/// <summary>
/// Baut aus den offiziellen IMDb-Dateien einen auf Serien und Episoden begrenzten SQLite-Index.
/// </summary>
internal sealed class ImdbDatasetIndexBuilder
{
    internal const int SchemaVersion = 3;
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
        var sqliteWorkerThreads = Math.Clamp(Environment.ProcessorCount - 1, 1, 4);
        // Der Index wird vollständig neu erzeugt. Ein größerer nur für diese Verbindung gültiger
        // Seitencache vermeidet den sehr kleinen SQLite-Standardcache beim Bulkimport; Hilfsthreads
        // dürfen insbesondere die fünf abschließenden CREATE-INDEX-Sortierungen unterstützen.
        await ExecuteNonQueryAsync(
            connection,
            $"PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA temp_store=MEMORY; PRAGMA locking_mode=EXCLUSIVE; PRAGMA cache_size=-131072; PRAGMA threads={sqliteWorkerThreads};",
            cancellationToken);
        await ExecuteNonQueryAsync(
            connection,
            """
            CREATE TABLE titles (
                id TEXT PRIMARY KEY,
                kind INTEGER NOT NULL,
                primary_title TEXT NOT NULL,
                original_title TEXT NULL,
                normalized_primary TEXT NULL,
                normalized_original TEXT NULL,
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
            CREATE TABLE series_aliases (
                title_id TEXT NOT NULL,
                title TEXT NOT NULL,
                normalized_title TEXT NOT NULL
            );
            CREATE TABLE metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            ) WITHOUT ROWID;
            """,
            cancellationToken);

        var basicsArchiveLength = new FileInfo(basicsArchivePath).Length;
        var episodesArchiveLength = new FileInfo(episodesArchivePath).Length;
        var aliasesArchiveLength = new FileInfo(aliasesArchivePath).Length;
        var totalArchiveBytes = basicsArchiveLength + episodesArchiveLength + aliasesArchiveLength;
        var completedArchiveBytes = 0L;
        var importedTitleIds = new ImportedTitleIdSet();
        var importedSeriesIds = new ImportedTitleIdSet();
        var episodeLinks = LoadEpisodeLinks(
            episodesArchivePath,
            CreateProgressContext("title.episode", 1, episodesArchiveLength, completedArchiveBytes, totalArchiveBytes),
            progress,
            cancellationToken);
        completedArchiveBytes += episodesArchiveLength;

        try
        {
            ImportBasics(
                connection,
                basicsArchivePath,
                CreateProgressContext("title.basics", 2, basicsArchiveLength, completedArchiveBytes, totalArchiveBytes),
                progress,
                importedTitleIds,
                importedSeriesIds,
                episodeLinks,
                cancellationToken);
        }
        finally
        {
            episodeLinks.Release();
        }

        completedArchiveBytes += basicsArchiveLength;
        ImportGermanAliases(
            connection,
            aliasesArchivePath,
            CreateProgressContext("title.akas", 3, aliasesArchiveLength, completedArchiveBytes, totalArchiveBytes),
            progress,
            importedTitleIds,
            importedSeriesIds,
            cancellationToken);
        var finalizationSteps = new (string Name, string Sql)[]
        {
            ("Der Primärtitel-Index wird aufgebaut.", "CREATE INDEX ix_titles_kind_primary ON titles(normalized_primary) WHERE kind = 1;"),
            ("Der Originaltitel-Index wird aufgebaut.", "CREATE INDEX ix_titles_kind_original ON titles(normalized_original) WHERE kind = 1 AND normalized_original IS NOT NULL;"),
            ("Der Episoden-Index wird aufgebaut.", "CREATE INDEX ix_titles_parent ON titles(parent_id, season_number, episode_number) WHERE kind = 2;"),
            ("Der Aliasnamen-Index wird aufgebaut.", "CREATE INDEX ix_aliases_normalized ON series_aliases(normalized_title);"),
            ("Der Aliasverknüpfungs-Index wird aufgebaut.", "CREATE INDEX ix_aliases_title_id ON aliases(title_id);")
        };
        var finalizationStepCount = finalizationSteps.Length + 1;
        for (var index = 0; index < finalizationSteps.Length; index++)
        {
            ReportFinalizationProgress(progress, finalizationSteps[index].Name, index + 1, finalizationStepCount);
            await ExecuteNonQueryAsync(connection, finalizationSteps[index].Sql, cancellationToken);
        }

        ReportFinalizationProgress(progress, "Versionsinformationen werden gespeichert.", finalizationStepCount, finalizationStepCount);
        await using var metadataCommand = connection.CreateCommand();
        metadataCommand.CommandText = "INSERT INTO metadata(key, value) VALUES ('version', $version), ('builtUtc', $builtUtc);";
        metadataCommand.Parameters.AddWithValue("$version", versionToken);
        metadataCommand.Parameters.AddWithValue("$builtUtc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await metadataCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ReportFinalizationProgress(
        IProgress<ImdbDatasetImportProgress>? progress,
        string stepName,
        int stepNumber,
        int stepCount)
    {
        progress?.Report(new ImdbDatasetImportProgress(
            stepName,
            stepNumber,
            stepCount,
            0,
            100d,
            100d,
            IsFinalizing: true));
    }

    private static void ImportBasics(
        SqliteConnection connection,
        string archivePath,
        ImdbDatasetProgressContext progressContext,
        IProgress<ImdbDatasetImportProgress>? progress,
        ImportedTitleIdSet importedTitleIds,
        ImportedTitleIdSet importedSeriesIds,
        EpisodeLinkLookup episodeLinks,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        using var seriesCommand = connection.CreateCommand();
        seriesCommand.Transaction = transaction;
        seriesCommand.CommandText =
            """
            INSERT INTO titles(
                id, kind, primary_title, original_title, normalized_primary, normalized_original, start_year,
                parent_id, season_number, episode_number)
            VALUES(
                $id, 1, $primary, $original, $normalizedPrimary, $normalizedOriginal, $year,
                NULL, NULL, NULL);
            """;
        var seriesId = seriesCommand.Parameters.Add("$id", SqliteType.Text);
        var seriesPrimary = seriesCommand.Parameters.Add("$primary", SqliteType.Text);
        var seriesOriginal = seriesCommand.Parameters.Add("$original", SqliteType.Text);
        var seriesNormalizedPrimary = seriesCommand.Parameters.Add("$normalizedPrimary", SqliteType.Text);
        var seriesNormalizedOriginal = seriesCommand.Parameters.Add("$normalizedOriginal", SqliteType.Text);
        var seriesYear = seriesCommand.Parameters.Add("$year", SqliteType.Integer);
        seriesCommand.Prepare();

        // Episoden bilden den weitaus größten Teil von title.basics. Ein eigenes Statement
        // bindet nur die fünf variablen Werte, die der spätere Episodenkatalog tatsächlich liest.
        using var episodeCommand = connection.CreateCommand();
        episodeCommand.Transaction = transaction;
        episodeCommand.CommandText =
            """
            INSERT INTO titles(id, kind, primary_title, parent_id, season_number, episode_number)
            VALUES($id, 2, $primary, $parent, $season, $episode);
            """;
        var episodeId = episodeCommand.Parameters.Add("$id", SqliteType.Text);
        var episodePrimary = episodeCommand.Parameters.Add("$primary", SqliteType.Text);
        var episodeParent = episodeCommand.Parameters.Add("$parent", SqliteType.Text);
        var episodeSeason = episodeCommand.Parameters.Add("$season", SqliteType.Integer);
        var episodeNumberParameter = episodeCommand.Parameters.Add("$episode", SqliteType.Integer);
        episodeCommand.Prepare();

        ReadGzipTsv(
            archivePath,
            progressContext,
            progress,
            line =>
            {
                Span<Range> columns = stackalloc Range[6];
                if (!TryGetColumnRanges(line, columns)
                    || !TryMapTitleKind(line[columns[1]], out var mappedKind))
                {
                    return false;
                }

                var idSpan = line[columns[0]];
                string? parentId = null;
                int? seasonNumber = null;
                int? episodeNumber = null;
                if (mappedKind == 2
                    && !episodeLinks.TryGet(idSpan, out parentId, out seasonNumber, out episodeNumber))
                {
                    return false;
                }

                var primaryTitleSpan = line[columns[2]];
                var originalTitleSpan = line[columns[3]];
                var primaryTitle = Encoding.UTF8.GetString(primaryTitleSpan);
                if (mappedKind == 1)
                {
                    seriesId.Value = Encoding.ASCII.GetString(idSpan);
                    seriesPrimary.Value = primaryTitle;
                    seriesNormalizedPrimary.Value = EpisodeMetadataMatchingHeuristics.NormalizeText(primaryTitle);
                    if (primaryTitleSpan.SequenceEqual(originalTitleSpan))
                    {
                        // Der Primärtitelzweig findet denselben Text bereits. Ein zweites Exemplar
                        // würde sowohl die Tabelle als auch den Originaltitel-Index nur vergrößern.
                        seriesOriginal.Value = DBNull.Value;
                        seriesNormalizedOriginal.Value = DBNull.Value;
                    }
                    else
                    {
                        var originalTitle = Encoding.UTF8.GetString(originalTitleSpan);
                        seriesOriginal.Value = originalTitle;
                        seriesNormalizedOriginal.Value = EpisodeMetadataMatchingHeuristics.NormalizeText(originalTitle);
                    }

                    seriesYear.Value = TryParseNullableInt(line[columns[5]]) is { } parsedYear
                        ? parsedYear
                        : DBNull.Value;
                    seriesCommand.ExecuteNonQuery();
                    importedSeriesIds.Add(idSpan);
                }
                else
                {
                    episodeId.Value = Encoding.ASCII.GetString(idSpan);
                    episodePrimary.Value = primaryTitle;
                    episodeParent.Value = parentId is null ? DBNull.Value : parentId;
                    episodeSeason.Value = seasonNumber is { } parsedSeason ? parsedSeason : DBNull.Value;
                    episodeNumberParameter.Value = episodeNumber is { } parsedEpisode ? parsedEpisode : DBNull.Value;
                    episodeCommand.ExecuteNonQuery();
                }

                importedTitleIds.Add(idSpan);
                return true;
            },
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    private static EpisodeLinkLookup LoadEpisodeLinks(
        string archivePath,
        ImdbDatasetProgressContext progressContext,
        IProgress<ImdbDatasetImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var estimatedRowCount = (int)Math.Clamp(new FileInfo(archivePath).Length / 32L, 1024L, 12_000_000L);
        var lookup = new EpisodeLinkLookup(estimatedRowCount);

        ReadGzipTsv(
            archivePath,
            progressContext,
            progress,
            line =>
            {
                Span<Range> columns = stackalloc Range[4];
                if (!TryGetColumnRanges(line, columns))
                {
                    return false;
                }

                lookup.Add(
                    line[columns[0]],
                    line[columns[1]],
                    TryParseNullableInt(line[columns[2]]),
                    TryParseNullableInt(line[columns[3]]));
                return true;
            },
            cancellationToken);
        lookup.PrepareForLookup();
        return lookup;
    }

    private static void ImportGermanAliases(
        SqliteConnection connection,
        string archivePath,
        ImdbDatasetProgressContext progressContext,
        IProgress<ImdbDatasetImportProgress>? progress,
        ImportedTitleIdSet importedTitleIds,
        ImportedTitleIdSet importedSeriesIds,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO aliases(title_id, title, normalized_title, region, language)
            VALUES($id, $title, $normalized, $region, $language);
            """;
        var id = command.Parameters.Add("$id", SqliteType.Text);
        var title = command.Parameters.Add("$title", SqliteType.Text);
        var normalized = command.Parameters.Add("$normalized", SqliteType.Text);
        var region = command.Parameters.Add("$region", SqliteType.Text);
        var language = command.Parameters.Add("$language", SqliteType.Text);
        command.Prepare();
        using var seriesCommand = connection.CreateCommand();
        seriesCommand.Transaction = transaction;
        seriesCommand.CommandText =
            """
            INSERT INTO series_aliases(title_id, title, normalized_title)
            VALUES($id, $title, $normalized);
            """;
        var seriesId = seriesCommand.Parameters.Add("$id", SqliteType.Text);
        var seriesTitle = seriesCommand.Parameters.Add("$title", SqliteType.Text);
        var seriesNormalized = seriesCommand.Parameters.Add("$normalized", SqliteType.Text);
        seriesCommand.Prepare();

        ReadGzipTsv(
            archivePath,
            progressContext,
            progress,
            line =>
            {
                Span<Range> columns = stackalloc Range[5];
                if (!TryGetColumnRanges(line, columns))
                {
                    return false;
                }

                var idSpan = line[columns[0]];
                var regionSpan = line[columns[3]];
                var languageSpan = line[columns[4]];
                if ((!EqualsAsciiIgnoreCase(regionSpan, "DE"u8)
                        && !EqualsAsciiIgnoreCase(languageSpan, "de"u8))
                    || !importedTitleIds.Contains(idSpan))
                {
                    return false;
                }

                var aliasTitle = Encoding.UTF8.GetString(line[columns[2]]);
                id.Value = Encoding.ASCII.GetString(idSpan);
                title.Value = aliasTitle;
                normalized.Value = EpisodeMetadataMatchingHeuristics.NormalizeText(aliasTitle);
                region.Value = ToDatabaseNullable(regionSpan);
                language.Value = ToDatabaseNullable(languageSpan);
                command.ExecuteNonQuery();
                if (importedSeriesIds.Contains(idSpan))
                {
                    seriesId.Value = id.Value;
                    seriesTitle.Value = aliasTitle;
                    seriesNormalized.Value = normalized.Value;
                    seriesCommand.ExecuteNonQuery();
                }

                return true;
            },
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    /// <summary>
    /// Liest die lokalen GZip-Dateien als UTF-8-Bytes auf dem vom Manager bereitgestellten Worker-Thread.
    /// Verworfene IMDb-Zeilen benötigen dadurch weder einen vollständigen String noch Teilstrings.
    /// </summary>
    private static void ReadGzipTsv(
        string archivePath,
        ImdbDatasetProgressContext progressContext,
        IProgress<ImdbDatasetImportProgress>? progress,
        Utf8LineProcessor processLine,
        CancellationToken cancellationToken)
    {
        using var fileStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: false);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress, leaveOpen: false);
        var readBuffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        var lineBuffer = ArrayPool<byte>.Shared.Rent(4096);
        var bufferedLineLength = 0;
        var isHeader = true;
        long rowCount = 0;
        long importedRowCount = 0;
        var elapsed = Stopwatch.StartNew();
        var lastProgressTimestamp = Stopwatch.GetTimestamp();
        ReportImportProgress(progress, progressContext, rowCount, importedRowCount, elapsed.Elapsed, fileStream.Position);

        void AppendLineSegment(ReadOnlySpan<byte> segment)
        {
            var requiredLength = bufferedLineLength + segment.Length;
            if (requiredLength > lineBuffer.Length)
            {
                var replacement = ArrayPool<byte>.Shared.Rent(Math.Max(requiredLength, lineBuffer.Length * 2));
                lineBuffer.AsSpan(0, bufferedLineLength).CopyTo(replacement);
                ArrayPool<byte>.Shared.Return(lineBuffer);
                lineBuffer = replacement;
            }

            segment.CopyTo(lineBuffer.AsSpan(bufferedLineLength));
            bufferedLineLength = requiredLength;
        }

        void ProcessCompletedLine(ReadOnlySpan<byte> line)
        {
            if (!line.IsEmpty && line[^1] == (byte)'\r')
            {
                line = line[..^1];
            }

            if (isHeader)
            {
                isHeader = false;
                return;
            }

            rowCount++;
            if (processLine(line))
            {
                importedRowCount++;
            }

            if (Stopwatch.GetElapsedTime(lastProgressTimestamp) >= ProgressUpdateInterval)
            {
                ReportImportProgress(progress, progressContext, rowCount, importedRowCount, elapsed.Elapsed, fileStream.Position);
                lastProgressTimestamp = Stopwatch.GetTimestamp();
            }
        }

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytesRead = gzipStream.Read(readBuffer);
                if (bytesRead == 0)
                {
                    break;
                }

                var block = readBuffer.AsSpan(0, bytesRead);
                var segmentStart = 0;
                for (var index = 0; index < block.Length; index++)
                {
                    if (block[index] != (byte)'\n')
                    {
                        continue;
                    }

                    var segment = block[segmentStart..index];
                    if (bufferedLineLength == 0)
                    {
                        ProcessCompletedLine(segment);
                    }
                    else
                    {
                        AppendLineSegment(segment);
                        ProcessCompletedLine(lineBuffer.AsSpan(0, bufferedLineLength));
                        bufferedLineLength = 0;
                    }

                    segmentStart = index + 1;
                }

                if (segmentStart < block.Length)
                {
                    AppendLineSegment(block[segmentStart..]);
                }
            }

            if (bufferedLineLength > 0)
            {
                ProcessCompletedLine(lineBuffer.AsSpan(0, bufferedLineLength));
            }

            ReportImportProgress(progress, progressContext, rowCount, importedRowCount, elapsed.Elapsed, progressContext.ArchiveLength);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(readBuffer);
            ArrayPool<byte>.Shared.Return(lineBuffer);
        }
    }

    /// <summary>
    /// Ermittelt nur die tatsächlich benötigten TSV-Spalten. Dadurch entstehen für verworfene IMDb-Zeilen
    /// keine Teilstrings und für relevante Zeilen nur die Werte, die in den Index geschrieben werden.
    /// </summary>
    private static bool TryGetColumnRanges(ReadOnlySpan<byte> line, Span<Range> columns)
    {
        var columnIndex = 0;
        var columnStart = 0;
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] != (byte)'\t')
            {
                continue;
            }

            columns[columnIndex++] = columnStart..index;
            if (columnIndex == columns.Length)
            {
                return true;
            }

            columnStart = index + 1;
        }

        if (columnIndex < columns.Length)
        {
            columns[columnIndex++] = columnStart..line.Length;
        }

        return columnIndex == columns.Length;
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
        long importedRowCount,
        TimeSpan elapsed,
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
            Math.Clamp(overallPercent, 0d, 100d),
            ImportedRowCount: importedRowCount,
            ProcessedRowsPerSecond: elapsed.TotalSeconds > 0d ? processedRowCount / elapsed.TotalSeconds : 0d));
    }

    private static bool TryMapTitleKind(ReadOnlySpan<byte> value, out int kind)
    {
        if (EqualsAsciiIgnoreCase(value, "tvSeries"u8)
            || EqualsAsciiIgnoreCase(value, "tvMiniSeries"u8))
        {
            kind = 1;
            return true;
        }

        if (EqualsAsciiIgnoreCase(value, "tvEpisode"u8))
        {
            kind = 2;
            return true;
        }

        kind = 0;
        return false;
    }

    private static bool EqualsAsciiIgnoreCase(ReadOnlySpan<byte> value, ReadOnlySpan<byte> expected)
    {
        if (value.Length != expected.Length)
        {
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            var left = value[index];
            var right = expected[index];
            if (left == right)
            {
                continue;
            }

            if (left is >= (byte)'A' and <= (byte)'Z')
            {
                left += (byte)('a' - 'A');
            }

            if (right is >= (byte)'A' and <= (byte)'Z')
            {
                right += (byte)('a' - 'A');
            }

            if (left != right)
            {
                return false;
            }
        }

        return true;
    }

    private static int? TryParseNullableInt(ReadOnlySpan<byte> value) =>
        !value.SequenceEqual("\\N"u8)
        && Utf8Parser.TryParse(value, out int parsed, out var consumed)
        && consumed == value.Length
            ? parsed
            : null;

    private static object ToDatabaseNullable(ReadOnlySpan<byte> value) =>
        value.SequenceEqual("\\N"u8) ? DBNull.Value : Encoding.UTF8.GetString(value);

    private static bool TryParseNumericTitleId(ReadOnlySpan<byte> id, out int numericId)
    {
        numericId = 0;
        if (id.Length <= 2
            || id[0] is not ((byte)'t' or (byte)'T')
            || id[1] is not ((byte)'t' or (byte)'T'))
        {
            return false;
        }

        foreach (var character in id[2..])
        {
            if (character is < (byte)'0' or > (byte)'9')
            {
                return false;
            }

            var nextValue = (long)numericId * 10L + character - (byte)'0';
            if (nextValue >= int.MaxValue - ImportedTitleIdSet.GrowthBlockSize)
            {
                return false;
            }

            numericId = (int)nextValue;
        }

        return true;
    }

    private delegate bool Utf8LineProcessor(ReadOnlySpan<byte> line);

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

    /// <summary>
    /// Hält die Zuordnung aus <c>title.episode</c> kompakt im Arbeitsspeicher, damit Episodentitel beim
    /// ersten und einzigen SQLite-Schreibvorgang bereits Parent, Staffel und Folge erhalten. Numerische
    /// IMDb-Kennungen benötigen dabei nur vier Integer je Eintrag statt mehrerer verwalteter Zeichenfolgen.
    /// </summary>
    private sealed class EpisodeLinkLookup
    {
        private const ushort MissingNumber = ushort.MaxValue;
        private const ulong ParentIdMask = 0x7FFF_FFFFUL;
        private readonly List<NumericEpisodeLink> _numericLinks;
        private readonly Dictionary<string, TextEpisodeLink> _textLinks = new(StringComparer.Ordinal);
        private bool _isOrdered = true;
        private ulong _lastSortKey;
        private int _lookupCursor;
        private ulong _lastLookupSortKey;

        public EpisodeLinkLookup(int estimatedRowCount)
        {
            _numericLinks = new List<NumericEpisodeLink>(estimatedRowCount);
        }

        public void Add(ReadOnlySpan<byte> id, ReadOnlySpan<byte> parentId, int? seasonNumber, int? episodeNumber)
        {
            if (TryCreateTitleIdSortKey(id, out var sortKey)
                && TryParseNumericTitleId(parentId, out var numericParentId)
                && TryPackEpisodeLink(numericParentId, seasonNumber, episodeNumber, out var packedData))
            {
                _isOrdered &= sortKey >= _lastSortKey;
                _lastSortKey = sortKey;
                _numericLinks.Add(new NumericEpisodeLink(sortKey, packedData));
                return;
            }

            _textLinks[Encoding.UTF8.GetString(id)] = new TextEpisodeLink(
                Encoding.UTF8.GetString(parentId),
                seasonNumber,
                episodeNumber);
        }

        public void PrepareForLookup()
        {
            if (!_isOrdered)
            {
                _numericLinks.Sort(static (left, right) => left.SortKey.CompareTo(right.SortKey));
            }
        }

        public bool TryGet(
            ReadOnlySpan<byte> id,
            out string? parentId,
            out int? seasonNumber,
            out int? episodeNumber)
        {
            if (TryCreateTitleIdSortKey(id, out var sortKey))
            {
                if (sortKey < _lastLookupSortKey)
                {
                    _lookupCursor = FindLowerBound(sortKey);
                }
                else
                {
                    while (_lookupCursor < _numericLinks.Count && _numericLinks[_lookupCursor].SortKey < sortKey)
                    {
                        _lookupCursor++;
                    }
                }

                _lastLookupSortKey = sortKey;
                if (_lookupCursor < _numericLinks.Count && _numericLinks[_lookupCursor].SortKey == sortKey)
                {
                    var candidate = _numericLinks[_lookupCursor];
                    var numericParentId = (int)(candidate.PackedData & ParentIdMask);
                    var packedSeason = (ushort)((candidate.PackedData >> 31) & ushort.MaxValue);
                    var packedEpisode = (ushort)((candidate.PackedData >> 47) & ushort.MaxValue);
                    parentId = "tt" + numericParentId.ToString("D7", CultureInfo.InvariantCulture);
                    seasonNumber = packedSeason == MissingNumber ? null : packedSeason;
                    episodeNumber = packedEpisode == MissingNumber ? null : packedEpisode;
                    return true;
                }
            }
            else if (_textLinks.TryGetValue(Encoding.UTF8.GetString(id), out var textLink))
            {
                parentId = textLink.ParentId;
                seasonNumber = textLink.SeasonNumber;
                episodeNumber = textLink.EpisodeNumber;
                return true;
            }

            parentId = null;
            seasonNumber = null;
            episodeNumber = null;
            return false;
        }

        private int FindLowerBound(ulong sortKey)
        {
            var lower = 0;
            var upper = _numericLinks.Count;
            while (lower < upper)
            {
                var middle = lower + ((upper - lower) / 2);
                if (_numericLinks[middle].SortKey < sortKey)
                {
                    lower = middle + 1;
                }
                else
                {
                    upper = middle;
                }
            }

            return lower;
        }

        private static bool TryCreateTitleIdSortKey(ReadOnlySpan<byte> id, out ulong sortKey)
        {
            sortKey = 0;
            if (!TryParseNumericTitleId(id, out _) || id.Length > 12)
            {
                return false;
            }

            // Jede Ziffer erhält 1..10, das implizite Stringende 0. Der linksbündig auf zehn
            // Nibbles aufgefüllte Wert hat damit exakt dieselbe Ordnung wie die TSV-Zeichenfolge,
            // auch am Übergang tt1000000 -> tt10000000 -> tt1000001.
            var digits = id[2..];
            for (var index = 0; index < 10; index++)
            {
                sortKey <<= 4;
                if (index < digits.Length)
                {
                    sortKey |= (uint)(digits[index] - (byte)'0' + 1);
                }
            }

            return true;
        }

        private static bool TryPackEpisodeLink(
            int parentId,
            int? seasonNumber,
            int? episodeNumber,
            out ulong packedData)
        {
            packedData = 0;
            if (seasonNumber is < 0 or >= MissingNumber || episodeNumber is < 0 or >= MissingNumber)
            {
                return false;
            }

            var packedSeason = (ushort)(seasonNumber ?? MissingNumber);
            var packedEpisode = (ushort)(episodeNumber ?? MissingNumber);
            packedData = (uint)parentId | ((ulong)packedSeason << 31) | ((ulong)packedEpisode << 47);
            return true;
        }

        public void Release()
        {
            _numericLinks.Clear();
            _numericLinks.TrimExcess();
            _textLinks.Clear();
            _textLinks.TrimExcess();
        }

        private readonly record struct NumericEpisodeLink(ulong SortKey, ulong PackedData);

        private sealed record TextEpisodeLink(string ParentId, int? SeasonNumber, int? EpisodeNumber);
    }

    /// <summary>
    /// Kompakter Vorfilter für IMDb-Titelkennungen. Die numerische <c>tt</c>-Kennung passt direkt in
    /// ein Bitfeld, sodass selbst zig Millionen importierte Titel nur wenige MiB Arbeitsspeicher benötigen.
    /// Seltene nicht standardkonforme Kennungen bleiben über eine kleine Zeichenfolgenmenge korrekt.
    /// </summary>
    private sealed class ImportedTitleIdSet
    {
        internal const int GrowthBlockSize = 4 * 1024 * 1024;
        private readonly HashSet<string> _nonNumericIds = new(StringComparer.Ordinal);
        private BitArray _numericIds = new(GrowthBlockSize);

        public void Add(ReadOnlySpan<byte> id)
        {
            if (!TryParseNumericTitleId(id, out var numericId))
            {
                _nonNumericIds.Add(Encoding.UTF8.GetString(id));
                return;
            }

            EnsureCapacity(numericId);
            _numericIds[numericId] = true;
        }

        public bool Contains(ReadOnlySpan<byte> id)
        {
            if (!TryParseNumericTitleId(id, out var numericId))
            {
                return _nonNumericIds.Contains(Encoding.UTF8.GetString(id));
            }

            return numericId < _numericIds.Length && _numericIds[numericId];
        }

        private void EnsureCapacity(int numericId)
        {
            if (numericId < _numericIds.Length)
            {
                return;
            }

            var requiredLength = ((long)numericId / GrowthBlockSize + 1L) * GrowthBlockSize;
            _numericIds.Length = checked((int)requiredLength);
        }

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
            : TitleSimilarity >= 12
                ? "Titel unscharf"
                : "Weitere Folge";

    /// <summary>
    /// Nur ein exakter Serien- und Episodentitel ist ohne Benutzerentscheidung stark genug.
    /// Der aufrufende Workflow prüft zusätzlich den Abstand zum zweitbesten Treffer.
    /// </summary>
    public bool IsStrongAutomaticMatch => SeriesTitleMatchedExactly && TitleSimilarity >= 30;
}

/// <summary>
/// Eine im Offlineindex gefundene IMDb-Serie mit dem zur Suchanfrage passendsten, bevorzugt deutschen Titel.
/// </summary>
internal sealed record ImdbSeriesCandidate(
    string ImdbId,
    string DisplayTitle,
    string PrimaryTitle,
    int? StartYear,
    int TitleSimilarity,
    bool ExactTitleMatch)
{
    /// <summary>
    /// Kompakte Beschriftung für die manuelle Serienauswahl im IMDb-Dialog.
    /// </summary>
    public string DisplayText => StartYear is { } year
        ? $"{DisplayTitle} ({year}) · {ImdbId}"
        : $"{DisplayTitle} · {ImdbId}";
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

    /// <summary>
    /// Sucht Seriennamen asynchron und berücksichtigt dabei deutsche IMDb-Aliase sowie begrenzte Tippfehler-Toleranz.
    /// </summary>
    public Task<IReadOnlyList<ImdbSeriesCandidate>> SearchSeriesCandidatesAsync(
        string seriesQuery,
        int maximumResults = 12,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => SearchSeriesCandidates(seriesQuery, maximumResults), cancellationToken);

    /// <summary>
    /// Lädt alle Episoden einer gewählten Serie oder Staffel und sortiert ähnliche Titel nach vorne.
    /// Anders als die automatische Zuordnung verwirft diese Browse-Ansicht schwache Treffer nicht.
    /// </summary>
    public Task<IReadOnlyList<ImdbEpisodeCandidate>> BrowseSeriesEpisodesAsync(
        ImdbSeriesCandidate series,
        string episodeQuery,
        int? seasonNumber,
        string? guessedSeasonNumber,
        string? guessedEpisodeNumber,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => BrowseSeriesEpisodes(
                series,
                episodeQuery,
                seasonNumber,
                guessedSeasonNumber,
                guessedEpisodeNumber),
            cancellationToken);

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
                series.ImdbId,
                parentId => LoadEpisodeCatalog(connection, parentId));
            results.AddRange(ScoreEpisodes(episodeCatalog, series, guess, includeUnmatched: false));
        }

        return results
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.SeasonNumber ?? int.MaxValue)
            .ThenBy(candidate => candidate.EpisodeNumber ?? int.MaxValue)
            .ThenBy(candidate => candidate.EpisodeTitle, StringComparer.OrdinalIgnoreCase)
            .Take(maximumResults)
            .ToArray();
    }

    /// <summary>
    /// Sucht Serien synchron für automatische Workflows und Tests. Die SQL-Abfrage bleibt durch
    /// Präfixbereiche indexgestützt; ein kurzer stabiler Präfix liefert zusätzliche Kandidaten für
    /// die anschließende Fuzzy-Bewertung, ohne die große Alias-Tabelle vollständig zu scannen.
    /// </summary>
    public IReadOnlyList<ImdbSeriesCandidate> SearchSeriesCandidates(string seriesQuery, int maximumResults = 12)
    {
        if (!IsAvailable || maximumResults <= 0)
        {
            return [];
        }

        var normalizedSeries = EpisodeMetadataMatchingHeuristics.NormalizeText(seriesQuery);
        if (normalizedSeries.Length < 2)
        {
            return [];
        }

        EnsureCachesMatchCurrentDatabase();
        using var connection = OpenReadOnlyConnection();
        return _seriesCandidateCache
            .GetOrAdd(normalizedSeries, query => LoadSeriesCandidates(connection, query))
            .Take(maximumResults)
            .ToArray();
    }

    private IReadOnlyList<ImdbEpisodeCandidate> BrowseSeriesEpisodes(
        ImdbSeriesCandidate series,
        string episodeQuery,
        int? seasonNumber,
        string? guessedSeasonNumber,
        string? guessedEpisodeNumber)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (!IsAvailable)
        {
            return [];
        }

        EnsureCachesMatchCurrentDatabase();
        using var connection = OpenReadOnlyConnection();
        var episodeCatalog = _episodeCatalogCache.GetOrAdd(
            series.ImdbId,
            parentId => LoadEpisodeCatalog(connection, parentId));
        var guess = new EpisodeMetadataGuess(
            series.DisplayTitle,
            episodeQuery ?? string.Empty,
            guessedSeasonNumber ?? string.Empty,
            guessedEpisodeNumber ?? string.Empty);
        var candidates = ScoreEpisodes(
                episodeCatalog.Where(episode => seasonNumber is null || episode.SeasonNumber == seasonNumber),
                series,
                guess,
                includeUnmatched: true)
            .ToArray();

        return string.IsNullOrWhiteSpace(episodeQuery)
            ? candidates
                .OrderBy(candidate => candidate.SeasonNumber ?? int.MaxValue)
                .ThenBy(candidate => candidate.EpisodeNumber ?? int.MaxValue)
                .ThenBy(candidate => candidate.EpisodeTitle, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : candidates
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.SeasonNumber ?? int.MaxValue)
                .ThenBy(candidate => candidate.EpisodeNumber ?? int.MaxValue)
                .ThenBy(candidate => candidate.EpisodeTitle, StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private SqliteConnection OpenReadOnlyConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static IReadOnlyList<ImdbSeriesCandidate> LoadSeriesCandidates(SqliteConnection connection, string normalizedSeries)
    {
        var rows = new List<ImdbSeriesTitleRow>();
        var aliasTable = HasDedicatedSeriesAliasTable(connection) ? "series_aliases" : "aliases";
        foreach (var prefix in BuildSeriesSearchPrefixes(normalizedSeries))
        {
            using var command = connection.CreateCommand();
            // Jede Quelle nutzt einen passenden Präfixindex. Der Aliaszweig liefert den deutschen
            // Anzeigenamen, während Primär- und Originaltitel weiterhin englische Suchtexte finden.
            command.CommandText =
                $$"""
                WITH candidate_titles(id, title, normalized_title, source_priority) AS (
                    SELECT id, primary_title, normalized_primary, 1
                    FROM titles
                    WHERE kind = 1 AND normalized_primary >= $prefix AND normalized_primary < $prefixUpper
                    UNION ALL
                    SELECT id, original_title, normalized_original, 0
                    FROM titles
                    WHERE kind = 1 AND normalized_original >= $prefix AND normalized_original < $prefixUpper
                    UNION ALL
                    SELECT a.title_id, a.title, a.normalized_title, 2
                    FROM {{aliasTable}} a INDEXED BY ix_aliases_normalized
                    INNER JOIN titles t ON t.id = a.title_id
                    WHERE t.kind = 1 AND a.normalized_title >= $prefix AND a.normalized_title < $prefixUpper
                )
                SELECT candidate_titles.id,
                       candidate_titles.title,
                       candidate_titles.normalized_title,
                       candidate_titles.source_priority,
                       titles.primary_title,
                       titles.start_year
                FROM candidate_titles
                INNER JOIN titles ON titles.id = candidate_titles.id
                ORDER BY candidate_titles.normalized_title
                LIMIT 256;
                """;
            command.Parameters.AddWithValue("$prefix", prefix);
            command.Parameters.AddWithValue("$prefixUpper", prefix + '\uffff');
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new ImdbSeriesTitleRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5)));
            }
        }

        var rankedCandidates = rows
            .GroupBy(row => row.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .Select(row => new
                {
                    Row = row,
                    Similarity = CalculateFuzzyTitleSimilarity(normalizedSeries, row.NormalizedTitle),
                    Exact = string.Equals(normalizedSeries, row.NormalizedTitle, StringComparison.Ordinal)
                })
                .OrderByDescending(candidate => candidate.Exact)
                .ThenByDescending(candidate => candidate.Similarity)
                .ThenByDescending(candidate => candidate.Row.SourcePriority)
                .ThenBy(candidate => candidate.Row.Title, StringComparer.OrdinalIgnoreCase)
                .First())
            .Select(candidate => new ImdbSeriesCandidate(
                candidate.Row.Id,
                candidate.Row.Title,
                candidate.Row.PrimaryTitle,
                candidate.Row.StartYear,
                candidate.Similarity,
                candidate.Exact))
            .OrderByDescending(candidate => candidate.ExactTitleMatch)
            .ThenByDescending(candidate => candidate.TitleSimilarity)
            .ThenBy(candidate => candidate.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var exactCandidates = rankedCandidates.Where(candidate => candidate.ExactTitleMatch).ToArray();
        if (exactCandidates.Length > 0)
        {
            return exactCandidates.Take(12).ToArray();
        }

        var bestSimilarity = rankedCandidates.FirstOrDefault()?.TitleSimilarity ?? 0;
        return rankedCandidates
            .Where(candidate => candidate.TitleSimilarity >= 12
                && candidate.TitleSimilarity >= bestSimilarity - 6)
            .Take(12)
            .ToArray();
    }

    private static bool HasDedicatedSeriesAliasTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'series_aliases');";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
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
        IEnumerable<ImdbEpisodeCatalogEntry> episodes,
        ImdbSeriesCandidate series,
        EpisodeMetadataGuess guess,
        bool includeUnmatched)
    {
        foreach (var episode in episodes)
        {
            var titleCandidates = episode.Titles
                .Select(title => new ImdbEpisodeTitleCandidate(title, IsLocalizedAlias: true))
                .Append(new ImdbEpisodeTitleCandidate(episode.PrimaryTitle, IsLocalizedAlias: false))
                .Select(title => new
                {
                    title.Title,
                    title.IsLocalizedAlias,
                    Similarity = CalculateFuzzyTitleSimilarity(guess.EpisodeTitle, title.Title)
                })
                .OrderByDescending(title => title.Similarity)
                .ThenByDescending(title => title.IsLocalizedAlias)
                .ThenBy(title => title.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var preferredTitle = string.IsNullOrWhiteSpace(guess.EpisodeTitle)
                ? titleCandidates
                    .OrderByDescending(title => title.IsLocalizedAlias)
                    .ThenBy(title => title.Title, StringComparer.OrdinalIgnoreCase)
                    .First()
                : titleCandidates[0];
            var titleSimilarity = preferredTitle.Similarity;
            var seasonMatched = int.TryParse(guess.SeasonNumber, out var season) && episode.SeasonNumber == season;
            var episodeMatched = int.TryParse(guess.EpisodeNumber, out var number) && episode.EpisodeNumber == number;
            if (!includeUnmatched && titleSimilarity < 12 && !(seasonMatched && episodeMatched))
            {
                continue;
            }

            var score = titleSimilarity
                + (series.ExactTitleMatch ? 35 : Math.Max(8, series.TitleSimilarity))
                + (seasonMatched ? 8 : 0)
                + (episodeMatched ? 12 : 0);
            yield return new ImdbEpisodeCandidate(
                episode.Id,
                series.DisplayTitle,
                preferredTitle.Title,
                episode.SeasonNumber,
                episode.EpisodeNumber,
                score,
                titleSimilarity,
                series.ExactTitleMatch);
        }
    }

    private static IReadOnlyList<string> BuildSeriesSearchPrefixes(string normalizedSeries)
    {
        var prefixes = new List<string> { normalizedSeries };
        if (normalizedSeries.Length >= 4)
        {
            var stablePrefix = normalizedSeries[..Math.Min(4, normalizedSeries.Length)].TrimEnd();
            if (stablePrefix.Length >= 2 && !string.Equals(stablePrefix, normalizedSeries, StringComparison.Ordinal))
            {
                prefixes.Add(stablePrefix);
            }
        }

        return prefixes;
    }

    /// <summary>
    /// Ergänzt die etablierte Tokenbewertung um eine zurückhaltende Damerau-Levenshtein-Komponente.
    /// Tippfehler werden damit sichtbar, erreichen aber nie die Schwelle eines automatischen exakten Treffers.
    /// </summary>
    private static int CalculateFuzzyTitleSimilarity(string left, string right)
    {
        var establishedScore = EpisodeMetadataMatchingHeuristics.CalculateTitleSimilarity(left, right);
        if (establishedScore >= 22)
        {
            return establishedScore;
        }

        var normalizedLeft = EpisodeMetadataMatchingHeuristics.NormalizeText(left);
        var normalizedRight = EpisodeMetadataMatchingHeuristics.NormalizeText(right);
        if (normalizedLeft.Length < 4 || normalizedRight.Length < 4)
        {
            return establishedScore;
        }

        var maximumLength = Math.Max(normalizedLeft.Length, normalizedRight.Length);
        var distance = CalculateDamerauLevenshteinDistance(normalizedLeft, normalizedRight);
        var ratio = 1d - (distance / (double)maximumLength);
        var fuzzyScore = ratio switch
        {
            >= 0.90d => 20,
            >= 0.80d => 16,
            >= 0.70d => 12,
            _ => 0
        };
        return Math.Max(establishedScore, fuzzyScore);
    }

    private static int CalculateDamerauLevenshteinDistance(string left, string right)
    {
        var distances = new int[left.Length + 1, right.Length + 1];
        for (var leftIndex = 0; leftIndex <= left.Length; leftIndex++)
        {
            distances[leftIndex, 0] = leftIndex;
        }

        for (var rightIndex = 0; rightIndex <= right.Length; rightIndex++)
        {
            distances[0, rightIndex] = rightIndex;
        }

        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitutionCost = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                distances[leftIndex, rightIndex] = Math.Min(
                    Math.Min(
                        distances[leftIndex - 1, rightIndex] + 1,
                        distances[leftIndex, rightIndex - 1] + 1),
                    distances[leftIndex - 1, rightIndex - 1] + substitutionCost);

                if (leftIndex > 1
                    && rightIndex > 1
                    && left[leftIndex - 1] == right[rightIndex - 2]
                    && left[leftIndex - 2] == right[rightIndex - 1])
                {
                    distances[leftIndex, rightIndex] = Math.Min(
                        distances[leftIndex, rightIndex],
                        distances[leftIndex - 2, rightIndex - 2] + 1);
                }
            }
        }

        return distances[left.Length, right.Length];
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

    private sealed record ImdbEpisodeCatalogEntry(
        string Id,
        string PrimaryTitle,
        int? SeasonNumber,
        int? EpisodeNumber,
        IReadOnlyList<string> Titles);

    private sealed record ImdbEpisodeTitleCandidate(string Title, bool IsLocalizedAlias);

    private sealed record ImdbSeriesTitleRow(
        string Id,
        string Title,
        string NormalizedTitle,
        int SourcePriority,
        string PrimaryTitle,
        int? StartYear);

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
