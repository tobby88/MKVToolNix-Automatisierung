using System.Text;

namespace MkvToolnixAutomatisierung.Services;

public sealed class BatchRunLogService
{
    private static readonly UTF8Encoding Utf8Encoding = new(encoderShouldEmitUTF8Identifier: false);

    public BatchRunLogSaveResult SaveBatchRunArtifacts(
        string sourceDirectory,
        string outputDirectory,
        string logText,
        IReadOnlyList<string> newArchiveFiles,
        int successCount,
        int warningCount,
        int errorCount)
    {
        PortableAppStorage.EnsureLogsDirectoryForSave();

        var now = DateTime.Now;
        var fileStamp = now.ToString("yyyy-MM-dd HH-mm-ss");
        var normalizedNewFiles = newArchiveFiles
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var batchLogPath = Path.Combine(PortableAppStorage.LogsDirectory, $"Batch - {fileStamp}.log.txt");
        File.WriteAllText(
            batchLogPath,
            BuildBatchLogText(
                now,
                sourceDirectory,
                outputDirectory,
                logText,
                normalizedNewFiles,
                successCount,
                warningCount,
                errorCount),
            Utf8Encoding);

        string? newFilesListPath = null;
        if (normalizedNewFiles.Count > 0)
        {
            newFilesListPath = Path.Combine(PortableAppStorage.LogsDirectory, $"Neu in Serienbibliothek - {fileStamp}.txt");
            File.WriteAllLines(
                newFilesListPath,
                BuildNewArchiveFileReportLines(now, sourceDirectory, outputDirectory, normalizedNewFiles),
                Utf8Encoding);
        }

        return new BatchRunLogSaveResult(batchLogPath, newFilesListPath, normalizedNewFiles);
    }

    private static string BuildBatchLogText(
        DateTime createdAt,
        string sourceDirectory,
        string outputDirectory,
        string logText,
        IReadOnlyList<string> newArchiveFiles,
        int successCount,
        int warningCount,
        int errorCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine("MKVToolNix-Automatisierung - Batch-Log");
        builder.AppendLine($"Erstellt am: {createdAt:dd.MM.yyyy HH:mm:ss}");
        builder.AppendLine($"Quellordner: {sourceDirectory}");
        builder.AppendLine($"Serienbibliothek: {outputDirectory}");
        builder.AppendLine($"Ergebnis: {successCount} erfolgreich, {warningCount} Warnung(en), {errorCount} Fehler");
        builder.AppendLine();
        builder.AppendLine("Neue Dateien in der Serienbibliothek:");

        if (newArchiveFiles.Count == 0)
        {
            builder.AppendLine("(keine)");
        }
        else
        {
            foreach (var file in newArchiveFiles)
            {
                builder.AppendLine(file);
            }
        }

        builder.AppendLine();
        builder.AppendLine("Batch-Protokoll:");
        builder.AppendLine(string.IsNullOrWhiteSpace(logText) ? "(leer)" : logText.TrimEnd());
        return builder.ToString();
    }

    private static IReadOnlyList<string> BuildNewArchiveFileReportLines(
        DateTime createdAt,
        string sourceDirectory,
        string outputDirectory,
        IReadOnlyList<string> newArchiveFiles)
    {
        return
        [
            "Neu in Serienbibliothek eingefügte Dateien",
            $"Erstellt am: {createdAt:dd.MM.yyyy HH:mm:ss}",
            $"Quellordner: {sourceDirectory}",
            $"Serienbibliothek: {outputDirectory}",
            string.Empty,
            .. newArchiveFiles
        ];
    }
}

public sealed record BatchRunLogSaveResult(
    string BatchLogPath,
    string? NewArchiveListPath,
    IReadOnlyList<string> NewArchiveFiles);
