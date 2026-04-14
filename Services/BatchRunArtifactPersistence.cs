namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Fuegt die Laufzusammenfassung in den aktuellen Batch-Logpuffer ein und persistiert danach die Batch-Artefakte.
/// </summary>
internal static class BatchRunArtifactPersistence
{
    /// <summary>
    /// Schreibt die im aktuellen Lauf neu erzeugten Dateien in den Laufpuffer und speichert danach Logdatei und Dateiliste.
    /// </summary>
    /// <param name="batchLogs">Service für die eigentliche Dateipersistenz.</param>
    /// <param name="sourceDirectory">Verarbeiteter Quellordner.</param>
    /// <param name="outputDirectory">Aktueller Ausgabeordner des Batch-Laufs.</param>
    /// <param name="newOutputFiles">Neu erzeugte Ausgabedateien des aktuellen Laufs.</param>
    /// <param name="newOutputMetadata">Importierbare Metadaten zu den neu erzeugten Ausgabedateien.</param>
    /// <param name="successCount">Anzahl erfolgreicher Episoden.</param>
    /// <param name="warningCount">Anzahl Episoden mit Warnungen.</param>
    /// <param name="errorCount">Anzahl fehlgeschlagener Episoden.</param>
    /// <param name="batchRunLogBuffer">Puffer nur für den aktuellen Batch-Lauf.</param>
    /// <param name="appendBatchRunLog">Callback zum Spiegeln neuer Laufzeilen in die sichtbare UI.</param>
    /// <returns>Persistierte Batch-Artefakte für den abgeschlossenen Lauf.</returns>
    public static BatchRunLogSaveResult Persist(
        BatchRunLogService batchLogs,
        string sourceDirectory,
        string outputDirectory,
        IReadOnlyList<string> newOutputFiles,
        IReadOnlyList<BatchOutputMetadataEntry> newOutputMetadata,
        int successCount,
        int warningCount,
        int errorCount,
        BufferedTextStore batchRunLogBuffer,
        Action<string> appendBatchRunLog)
    {
        ArgumentNullException.ThrowIfNull(batchLogs);
        ArgumentNullException.ThrowIfNull(batchRunLogBuffer);
        ArgumentNullException.ThrowIfNull(appendBatchRunLog);

        var files = newOutputFiles
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            appendBatchRunLog(string.Empty);
            appendBatchRunLog("NEU ERZEUGTE AUSGABEDATEIEN: keine");
        }
        else
        {
            appendBatchRunLog(string.Empty);
            appendBatchRunLog("NEU ERZEUGTE AUSGABEDATEIEN:");
            foreach (var file in files)
            {
                appendBatchRunLog("  " + file);
            }
        }

        var result = batchLogs.SaveBatchRunArtifacts(
            sourceDirectory,
            outputDirectory,
            batchRunLogBuffer.GetTextSnapshot(),
            files,
            successCount,
            warningCount,
            errorCount,
            newOutputMetadata);

        if (!string.IsNullOrWhiteSpace(result.NewOutputMetadataReportPath))
        {
            appendBatchRunLog("METADATEN-REPORT:");
            appendBatchRunLog("  " + result.NewOutputMetadataReportPath);
        }

        return result;
    }
}
