namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Rechnet die mehrphasige Batch-Ausführung in eine einzige UI-Fortschrittsanzeige um.
/// </summary>
internal sealed class BatchRunProgressTracker
{
    private const double PlanningStart = 0d;
    private const double PlanningEnd = 5d;
    private const double CopyStart = 5d;
    private const double CopyEnd = 15d;
    private const double ExecutionStart = 15d;
    private const double ExecutionEnd = 95d;
    private const double CleanupStart = 95d;
    private const double CleanupEnd = 100d;
    private const double MuxPhaseShare = 0.82d;
    private const double MovePhaseShare = 0.16d;
    private const double FinalizePhaseShare = 0.02d;

    private readonly int _totalItems;
    private readonly Action<string, int> _reportStatus;

    public BatchRunProgressTracker(int totalItems, Action<string, int> reportStatus)
    {
        _totalItems = Math.Max(1, totalItems);
        _reportStatus = reportStatus;
    }

    public void ReportPlanning(int current, int total)
    {
        var ratio = total <= 0 ? 1d : current / (double)total;
        _reportStatus(
            $"Erstelle Mux-Pläne... {current}/{Math.Max(total, 1)}",
            MapPhase(PlanningStart, PlanningEnd, ratio));
    }

    public void ReportCopyProgress(
        int currentFile,
        int totalFiles,
        long copiedBytes,
        long totalBytes,
        long currentFileCopiedBytes = 0,
        long currentFileTotalBytes = 0)
    {
        var ratio = totalBytes > 0
            ? copiedBytes / (double)totalBytes
            : currentFile / (double)Math.Max(totalFiles, 1);
        var currentFilePercent = currentFileTotalBytes > 0
            ? (int)Math.Round(Math.Clamp(currentFileCopiedBytes / (double)currentFileTotalBytes, 0d, 1d) * 100)
            : (int?)null;
        var currentFileProgressText = currentFilePercent is int percent
            ? $" ({percent}% der aktuellen Datei)"
            : string.Empty;

        _reportStatus(
            $"Kopiere Zieldateien... {currentFile}/{Math.Max(totalFiles, 1)}{currentFileProgressText}",
            MapPhase(CopyStart, CopyEnd, ratio));
    }

    public void ReportCopyCompleted(bool reusedExistingCopies)
    {
        _reportStatus(
            reusedExistingCopies
                ? "Arbeitskopien vorbereitet - vorhandene Kopien werden wiederverwendet"
                : "Arbeitskopien vorbereitet",
            MapPhase(CopyStart, CopyEnd, 1d));
    }

    public void ReportMuxProgress(int currentItem, int? itemProgressPercent, bool hasWarning)
    {
        var displayPercent = itemProgressPercent is int progress
            ? Math.Clamp(progress, 0, 99)
            : 0;
        var ratio = itemProgressPercent is int
            ? (displayPercent / 100d) * MuxPhaseShare
            : 0d;

        var statusText = itemProgressPercent is int
            ? $"Batch läuft... {currentItem}/{_totalItems} ({displayPercent}% in aktueller Episode)"
            : $"Batch läuft... {currentItem}/{_totalItems}";

        if (hasWarning)
        {
            statusText += " - Warnung erkannt";
        }

        _reportStatus(statusText, MapExecutionProgress(currentItem, ratio));
    }

    public void ReportMoveToDone(int currentItem, int currentFile, int totalFiles)
    {
        var ratio = totalFiles <= 0
            ? MuxPhaseShare + MovePhaseShare
            : MuxPhaseShare + ((currentFile / (double)totalFiles) * MovePhaseShare);

        _reportStatus(
            $"Batch läuft... {currentItem}/{_totalItems} (räume Quellen auf {currentFile}/{Math.Max(totalFiles, 1)})",
            MapExecutionProgress(currentItem, ratio));
    }

    public void ReportFinalizingItem(int currentItem)
    {
        _reportStatus(
            $"Batch läuft... {currentItem}/{_totalItems} (Episode wird abgeschlossen)",
            MapExecutionProgress(currentItem, MuxPhaseShare + MovePhaseShare + FinalizePhaseShare / 2d));
    }

    public void ReportItemCompleted(int currentItem)
    {
        _reportStatus(
            $"Batch läuft... {currentItem}/{_totalItems}",
            MapExecutionProgress(currentItem, 1d));
    }

    public void ReportRecycleProgress(int currentFile, int totalFiles)
    {
        var ratio = totalFiles <= 0 ? 1d : currentFile / (double)totalFiles;
        _reportStatus(
            $"Batch läuft... Done-Dateien werden in den Papierkorb verschoben {currentFile}/{Math.Max(totalFiles, 1)}",
            MapPhase(CleanupStart, CleanupEnd, ratio));
    }

    private int MapExecutionProgress(int currentItem, double itemRatio)
    {
        var clampedItem = Math.Clamp(currentItem, 1, _totalItems);
        var sliceSize = (ExecutionEnd - ExecutionStart) / _totalItems;
        var sliceStart = ExecutionStart + ((clampedItem - 1) * sliceSize);
        return MapPhase(sliceStart, sliceStart + sliceSize, itemRatio);
    }

    private static int MapPhase(double start, double end, double ratio)
    {
        var clampedRatio = Math.Clamp(ratio, 0d, 1d);
        return (int)Math.Round(start + ((end - start) * clampedRatio));
    }
}
