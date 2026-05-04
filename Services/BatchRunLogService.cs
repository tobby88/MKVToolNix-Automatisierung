using System.Globalization;
using System.Text;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Persistiert abgeschlossene Einzel- und Batch-Mux-Läufe in ein gemeinsames Sitzungslog
/// plus optionale separate Reports neuer Ausgabedateien.
/// </summary>
public sealed class BatchRunLogService
{
    private static readonly UTF8Encoding Utf8Encoding = new(encoderShouldEmitUTF8Identifier: false);
    private readonly object _sync = new();
    private readonly DateTimeOffset _sessionStartedAt = DateTimeOffset.Now;
    private string? _sessionLogPath;

    /// <summary>
    /// Hängt den abgeschlossenen Mux-Lauf an das aktuelle Sitzungslog an und schreibt
    /// optionale Reportlisten neuer Ausgabedateien in den portablen Log-Ordner.
    /// </summary>
    /// <param name="sourceDirectory">Verarbeiteter Quellordner.</param>
    /// <param name="outputDirectory">Tatsächlicher Ausgabeordner des Batch-Laufs.</param>
    /// <param name="logText">Gesamtes in der UI gesammeltes Batch-Protokoll.</param>
    /// <param name="newOutputFiles">Während des Laufs neu erzeugte Ausgabedateien.</param>
    /// <param name="successCount">Anzahl erfolgreicher Episoden.</param>
    /// <param name="warningCount">Anzahl abgeschlossener Episoden mit Warnungen.</param>
    /// <param name="errorCount">Anzahl fehlgeschlagener Episoden.</param>
    /// <param name="newOutputMetadata">Optionale strukturierte Metadaten zu den neu erzeugten Ausgabedateien.</param>
    /// <param name="runLabel">Kurzes Label für Dateiname und Logkopf, z. B. <c>Batch</c> oder <c>Einzel</c>.</param>
    /// <returns>Pfade zu den geschriebenen Artefakten inklusive normalisierter Liste neuer Dateien.</returns>
    public BatchRunLogSaveResult SaveBatchRunArtifacts(
        string sourceDirectory,
        string outputDirectory,
        string logText,
        IReadOnlyList<string> newOutputFiles,
        int successCount,
        int warningCount,
        int errorCount,
        IReadOnlyList<BatchOutputMetadataEntry>? newOutputMetadata = null,
        string runLabel = "Batch")
    {
        PortableAppStorage.EnsureLogsDirectoryForSave();

        var now = DateTimeOffset.Now;
        var fileStamp = now.LocalDateTime.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
        var normalizedRunLabel = string.IsNullOrWhiteSpace(runLabel) ? "Batch" : runLabel.Trim();
        var normalizedNewFiles = newOutputFiles
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var normalizedMetadata = BuildNormalizedMetadataEntries(normalizedNewFiles, newOutputMetadata);

        var artifactPaths = CreateUniqueArtifactPaths(fileStamp, normalizedRunLabel, normalizedNewFiles.Count > 0);

        var runLogText = BuildBatchLogText(
            now,
            sourceDirectory,
            outputDirectory,
            logText,
            normalizedNewFiles,
            normalizedMetadata,
            artifactPaths.NewOutputMetadataReportPath,
            normalizedRunLabel,
            successCount,
            warningCount,
            errorCount);

        string batchLogPath;
        lock (_sync)
        {
            _sessionLogPath ??= CreateSessionLogPath(_sessionStartedAt);
            EnsureSessionHeader(_sessionLogPath, _sessionStartedAt);
            File.AppendAllText(
                _sessionLogPath,
                BuildRunSection(runLogText),
                Utf8Encoding);
            batchLogPath = _sessionLogPath;
        }

        if (artifactPaths.NewOutputListPath is not null)
        {
            File.WriteAllLines(
                artifactPaths.NewOutputListPath,
                BuildNewOutputFileReportLines(now, sourceDirectory, outputDirectory, normalizedNewFiles),
                Utf8Encoding);
        }

        if (artifactPaths.NewOutputMetadataReportPath is not null)
        {
            var metadataReport = new BatchOutputMetadataReport
            {
                CreatedAt = now,
                SourceDirectory = sourceDirectory,
                OutputDirectory = outputDirectory,
                Items = normalizedMetadata.ToList()
            };

            File.WriteAllText(
                artifactPaths.NewOutputMetadataReportPath,
                BatchOutputMetadataReportJson.Serialize(metadataReport),
                Utf8Encoding);
        }

        return new BatchRunLogSaveResult(
            batchLogPath,
            artifactPaths.NewOutputListPath,
            artifactPaths.NewOutputMetadataReportPath,
            normalizedNewFiles);
    }

    private static void EnsureSessionHeader(string filePath, DateTimeOffset sessionStartedAt)
    {
        if (File.Exists(filePath))
        {
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine("MKVToolNix-Automatisierung - Mux-Sitzungsprotokoll");
        builder.AppendLine($"Gestartet am: {sessionStartedAt:dd.MM.yyyy HH:mm:ss}");
        builder.AppendLine();
        File.WriteAllText(filePath, builder.ToString(), Utf8Encoding);
    }

    private static string CreateSessionLogPath(DateTimeOffset sessionStartedAt)
    {
        var fileStamp = sessionStartedAt.LocalDateTime.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
        return GetUniqueLogPath($"Mux - {fileStamp}.log.txt");
    }

    private static string BuildRunSection(string runLogText)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.Append(runLogText.TrimEnd());
        builder.AppendLine();
        builder.AppendLine();
        return builder.ToString();
    }

    private static string GetUniqueLogPath(string fileName)
    {
        var path = Path.Combine(PortableAppStorage.LogsDirectory, fileName);
        if (!File.Exists(path))
        {
            return path;
        }

        var stem = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(fileName));
        for (var index = 2; index < 10_000; index++)
        {
            var candidate = Path.Combine(PortableAppStorage.LogsDirectory, $"{stem} ({index}).log.txt");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(PortableAppStorage.LogsDirectory, $"{stem} - {Guid.NewGuid():N}.log.txt");
    }

    internal static BatchRunArtifactPathSet CreateUniqueArtifactPaths(
        string fileStamp,
        string runLabel,
        bool includeNewFileReports)
    {
        var safeRunLabel = NormalizeFileNameSegment(runLabel, fallback: "Batch");
        for (var index = 1; index < 10_000; index++)
        {
            var suffix = index == 1 ? string.Empty : $" ({index})";
            var paths = new BatchRunArtifactPathSet(
                Path.Combine(PortableAppStorage.LogsDirectory, $"{safeRunLabel} - {fileStamp}{suffix}.log.txt"),
                includeNewFileReports
                    ? Path.Combine(PortableAppStorage.LogsDirectory, $"Neu erzeugte Ausgabedateien - {fileStamp}{suffix}.txt")
                    : null,
                includeNewFileReports
                    ? Path.Combine(PortableAppStorage.LogsDirectory, $"Neu erzeugte Ausgabedateien - {fileStamp}{suffix}.metadata.json")
                    : null);
            if (!paths.AllPaths.Any(File.Exists))
            {
                return paths;
            }
        }

        var uniqueSuffix = Guid.NewGuid().ToString("N");
        return new BatchRunArtifactPathSet(
            Path.Combine(PortableAppStorage.LogsDirectory, $"{safeRunLabel} - {fileStamp} - {uniqueSuffix}.log.txt"),
            includeNewFileReports
                ? Path.Combine(PortableAppStorage.LogsDirectory, $"Neu erzeugte Ausgabedateien - {fileStamp} - {uniqueSuffix}.txt")
                : null,
            includeNewFileReports
                ? Path.Combine(PortableAppStorage.LogsDirectory, $"Neu erzeugte Ausgabedateien - {fileStamp} - {uniqueSuffix}.metadata.json")
                : null);
    }

    private static string NormalizeFileNameSegment(string value, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            normalized = normalized.Replace(invalidCharacter, '_');
        }

        return normalized;
    }

    private static string BuildBatchLogText(
        DateTimeOffset createdAt,
        string sourceDirectory,
        string outputDirectory,
        string logText,
        IReadOnlyList<string> newOutputFiles,
        IReadOnlyList<BatchOutputMetadataEntry> newOutputMetadata,
        string? newOutputMetadataReportPath,
        string runLabel,
        int successCount,
        int warningCount,
        int errorCount)
    {
        var normalizedLogText = PersistedLogTextCleaner.Clean(logText);
        var builder = new StringBuilder();
        builder.AppendLine($"MKVToolNix-Automatisierung - {runLabel}-Log");
        builder.AppendLine($"Erstellt am: {createdAt:dd.MM.yyyy HH:mm:ss}");
        builder.AppendLine($"Quellordner: {sourceDirectory}");
        builder.AppendLine($"Ausgabeordner: {outputDirectory}");
        builder.AppendLine($"Ergebnis: {successCount} erfolgreich, {warningCount} Warnung(en), {errorCount} Fehler");
        builder.AppendLine();
        builder.AppendLine("Neu erzeugte Ausgabedateien:");

        if (newOutputFiles.Count == 0)
        {
            builder.AppendLine("(keine)");
        }
        else
        {
            foreach (var file in newOutputFiles)
            {
                builder.AppendLine(file);
            }
        }

        builder.AppendLine();
        builder.AppendLine("Strukturierter Metadaten-Report:");
        builder.AppendLine(newOutputMetadataReportPath ?? "(keiner)");

        if (newOutputMetadata.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Metadaten neuer Ausgabedateien:");
            foreach (var entry in newOutputMetadata)
            {
                builder.AppendLine(Path.GetFileName(entry.OutputPath));
                builder.AppendLine($"  Pfad: {entry.OutputPath}");
                builder.AppendLine($"  TVDB-Episode: {entry.ProviderIds?.Tvdb ?? "(keine)"}");
                builder.AppendLine($"  IMDB: {entry.ProviderIds?.Imdb ?? "(keine)"}");

                if (entry.Tvdb is not null)
                {
                    builder.AppendLine($"  TVDB-Serie: {entry.Tvdb.SeriesId?.ToString(CultureInfo.InvariantCulture) ?? "(keine)"}");
                    if (!string.IsNullOrWhiteSpace(entry.Tvdb.SeriesName))
                    {
                        builder.AppendLine($"  TVDB-Serienname: {entry.Tvdb.SeriesName}");
                    }
                }
            }
        }

        builder.AppendLine();
        builder.AppendLine("Batch-Protokoll:");
        builder.AppendLine(string.IsNullOrWhiteSpace(normalizedLogText) ? "(leer)" : normalizedLogText.TrimEnd());
        return builder.ToString();
    }

    private static IReadOnlyList<string> BuildNewOutputFileReportLines(
        DateTimeOffset createdAt,
        string sourceDirectory,
        string outputDirectory,
        IReadOnlyList<string> newOutputFiles)
    {
        return
        [
            "Neu erzeugte Ausgabedateien",
            $"Erstellt am: {createdAt:dd.MM.yyyy HH:mm:ss}",
            $"Quellordner: {sourceDirectory}",
            $"Ausgabeordner: {outputDirectory}",
            string.Empty,
            .. newOutputFiles
        ];
    }

    private static IReadOnlyList<BatchOutputMetadataEntry> BuildNormalizedMetadataEntries(
        IReadOnlyList<string> normalizedNewFiles,
        IReadOnlyList<BatchOutputMetadataEntry>? metadataEntries)
    {
        if (normalizedNewFiles.Count == 0)
        {
            return [];
        }

        var metadataByOutputPath = (metadataEntries ?? [])
            .Where(entry => !string.IsNullOrWhiteSpace(entry.OutputPath))
            .GroupBy(entry => entry.OutputPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return normalizedNewFiles
            .Select(path =>
            {
                metadataByOutputPath.TryGetValue(path, out var metadataEntry);
                return NormalizeMetadataEntry(path, metadataEntry);
            })
            .ToList();
    }

    private static BatchOutputMetadataEntry NormalizeMetadataEntry(string outputPath, BatchOutputMetadataEntry? entry)
    {
        var tvdbEpisodeId = entry?.Tvdb?.EpisodeId;
        var providerTvdbId = NormalizeOptional(entry?.ProviderIds?.Tvdb)
            ?? tvdbEpisodeId?.ToString(CultureInfo.InvariantCulture);
        var providerImdbId = NormalizeOptional(entry?.ProviderIds?.Imdb);
        var providerIds = string.IsNullOrWhiteSpace(providerTvdbId) && string.IsNullOrWhiteSpace(providerImdbId)
            ? null
            : new BatchOutputProviderIds
            {
                Tvdb = providerTvdbId,
                Imdb = providerImdbId
            };

        var tvdbMetadata = entry?.Tvdb is null
            ? null
            : new BatchOutputTvdbMetadata
            {
                SeriesId = entry.Tvdb.SeriesId,
                SeriesName = NormalizeOptional(entry.Tvdb.SeriesName),
                EpisodeId = entry.Tvdb.EpisodeId
            };

        return new BatchOutputMetadataEntry
        {
            OutputPath = outputPath,
            NfoPath = NormalizeOptional(entry?.NfoPath) ?? Path.ChangeExtension(outputPath, ".nfo"),
            SeriesName = NormalizeOptional(entry?.SeriesName),
            SeasonNumber = NormalizeOptional(entry?.SeasonNumber),
            EpisodeNumber = NormalizeOptional(entry?.EpisodeNumber),
            EpisodeTitle = NormalizeOptional(entry?.EpisodeTitle),
            TvdbEpisodeId = providerTvdbId,
            ProviderIds = providerIds,
            Tvdb = tvdbMetadata
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

/// <summary>
/// Beschreibt die geschriebenen Batch-Artefakte, damit die UI Pfade und neue Dateien anzeigen kann.
/// </summary>
/// <param name="BatchLogPath">Pfad zum vollständigen Batch-Protokoll.</param>
/// <param name="NewOutputListPath">Optionale Liste nur der neu erzeugten Ausgabedateien.</param>
/// <param name="NewOutputMetadataReportPath">Optionaler JSON-Report mit importierbaren Metadaten der neuen Ausgabedateien.</param>
/// <param name="NewOutputFiles">Normalisierte Menge der im Lauf neu erzeugten Dateien.</param>
public sealed record BatchRunLogSaveResult(
    string BatchLogPath,
    string? NewOutputListPath,
    string? NewOutputMetadataReportPath,
    IReadOnlyList<string> NewOutputFiles)
{
    /// <summary>
    /// Liefert den Pfad, der dem Benutzer nach Batch-Abschluss automatisch geöffnet werden soll.
    /// Dafür kommt bewusst nur die separate Liste neuer Ausgabedateien infrage; das vollständige
    /// Batch-Protokoll bleibt zwar gespeichert, wird aber nicht als Fallback geöffnet.
    /// </summary>
    public string? PreferredOpenPath => NewOutputListPath;
}

internal sealed record BatchRunArtifactPathSet(
    string BatchLogPath,
    string? NewOutputListPath,
    string? NewOutputMetadataReportPath)
{
    public IEnumerable<string> AllPaths
    {
        get
        {
            yield return BatchLogPath;
            if (NewOutputListPath is not null)
            {
                yield return NewOutputListPath;
            }

            if (NewOutputMetadataReportPath is not null)
            {
                yield return NewOutputMetadataReportPath;
            }
        }
    }
}
