using System.Text;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Persistiert den Abschluss eines Batch-Laufs als menschenlesbares Log plus separate Liste neuer Ausgabedateien.
/// </summary>
public sealed class BatchRunLogService
{
    private static readonly UTF8Encoding Utf8Encoding = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Schreibt Batch-Log und optionale Reportliste neuer Ausgabedateien in den portablen Log-Ordner.
    /// </summary>
    /// <param name="sourceDirectory">Verarbeiteter Quellordner.</param>
    /// <param name="outputDirectory">Tatsächlicher Ausgabeordner des Batch-Laufs.</param>
    /// <param name="logText">Gesamtes in der UI gesammeltes Batch-Protokoll.</param>
    /// <param name="newOutputFiles">Während des Laufs neu erzeugte Ausgabedateien.</param>
    /// <param name="successCount">Anzahl erfolgreicher Episoden.</param>
    /// <param name="warningCount">Anzahl abgeschlossener Episoden mit Warnungen.</param>
    /// <param name="errorCount">Anzahl fehlgeschlagener Episoden.</param>
    /// <returns>Pfade zu den geschriebenen Artefakten inklusive normalisierter Liste neuer Dateien.</returns>
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
        var normalizedLogText = NormalizeLogText(logText);
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
        builder.AppendLine(string.IsNullOrWhiteSpace(normalizedLogText) ? "(leer)" : normalizedLogText.TrimEnd());
        return builder.ToString();
    }

    private static string NormalizeLogText(string logText)
    {
        if (string.IsNullOrWhiteSpace(logText))
        {
            return logText;
        }

        return string.Join(
            Environment.NewLine,
            logText
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .Select(MojibakeRepair.NormalizeLikelyMojibake));
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
/// <param name="BatchLogPath">Pfad zum vollständigen Batch-Protokoll.</param>
/// <param name="NewOutputListPath">Optionale Liste nur der neu erzeugten Ausgabedateien.</param>
/// <param name="NewOutputFiles">Normalisierte Menge der im Lauf neu erzeugten Dateien.</param>
public sealed record BatchRunLogSaveResult(
    string BatchLogPath,
    string? NewOutputListPath,
    IReadOnlyList<string> NewOutputFiles)
{
    /// <summary>
    /// Liefert den Pfad, der dem Benutzer nach Batch-Abschluss automatisch geöffnet werden soll.
    /// Dafür kommt bewusst nur die separate Liste neuer Ausgabedateien infrage; das vollständige
    /// Batch-Protokoll bleibt zwar gespeichert, wird aber nicht als Fallback geöffnet.
    /// </summary>
    public string? PreferredOpenPath => NewOutputListPath;
}
