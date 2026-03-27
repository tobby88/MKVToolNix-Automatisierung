using System.Text;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Persistiert den Abschluss eines Batch-Laufs als menschenlesbares Log plus separate Liste neuer Ausgabedateien.
/// </summary>
public sealed class BatchRunLogService
{
    private static readonly UTF8Encoding Utf8Encoding = new(encoderShouldEmitUTF8Identifier: false);

    public BatchRunLogSaveResult SaveBatchRunArtifacts(
        string sourceDirectory,
        string outputDirectory,
        string logText,
        IReadOnlyList<string> newOutputFiles,
        int successCount,
        int warningCount,
        int errorCount)
    {
        PortableAppStorage.EnsureLogsDirectoryForSave();

        var now = DateTime.Now;
        var fileStamp = now.ToString("yyyy-MM-dd HH-mm-ss");
        var normalizedNewFiles = newOutputFiles
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
            newFilesListPath = Path.Combine(PortableAppStorage.LogsDirectory, $"Neu erzeugte Ausgabedateien - {fileStamp}.txt");
            File.WriteAllLines(
                newFilesListPath,
                BuildNewOutputFileReportLines(now, sourceDirectory, outputDirectory, normalizedNewFiles),
                Utf8Encoding);
        }

        return new BatchRunLogSaveResult(batchLogPath, newFilesListPath, normalizedNewFiles);
    }

    private static string BuildBatchLogText(
        DateTime createdAt,
        string sourceDirectory,
        string outputDirectory,
        string logText,
        IReadOnlyList<string> newOutputFiles,
        int successCount,
        int warningCount,
        int errorCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine("MKVToolNix-Automatisierung - Batch-Log");
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
        builder.AppendLine("Batch-Protokoll:");
        builder.AppendLine(string.IsNullOrWhiteSpace(logText) ? "(leer)" : logText.TrimEnd());
        return builder.ToString();
    }

    private static IReadOnlyList<string> BuildNewOutputFileReportLines(
        DateTime createdAt,
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
}

/// <summary>
/// Beschreibt die geschriebenen Batch-Artefakte, damit die UI Pfade und neue Dateien anzeigen kann.
/// </summary>
public sealed record BatchRunLogSaveResult(
    string BatchLogPath,
    string? NewOutputListPath,
    IReadOnlyList<string> NewOutputFiles);
