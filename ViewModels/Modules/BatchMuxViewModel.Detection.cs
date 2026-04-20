using System.Threading;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

// Dieser Partial enthält die Dateiauswahl und das Anwenden frischer Erkennungsergebnisse auf einzelne Batch-Zeilen.
internal sealed partial class BatchMuxViewModel
{
    private async Task ApplyDetectionToItemAsync(BatchEpisodeItemViewModel item, string selectedVideoPath)
    {
        await ApplyDetectionToItemAsync(item, selectedVideoPath, item.ExcludedSourcePaths);
    }

    private async Task<bool> ApplyDetectionToItemAsync(
        BatchEpisodeItemViewModel item,
        string selectedVideoPath,
        IReadOnlyCollection<string>? excludedSourcePaths)
    {
        try
        {
            SetBusy(true);
            SetStatus("Eintrag wird neu erkannt...", 0);

            var result = await _services.BatchScan.ScanAsync(
                selectedVideoPath,
                OutputDirectory,
                HandleSelectedItemDetectionProgress,
                excludedSourcePaths);
            var outputPath = result.OutputPath;
            var outputAlreadyExists = File.Exists(outputPath);
            var isArchiveTargetPath = _services.OutputPaths.IsArchivePath(outputPath);
            var statusKind = DetermineInitialStatus(result.Detected, outputAlreadyExists, isArchiveTargetPath);

            item.ApplyDetection(
                requestedMainVideoPath: selectedVideoPath,
                localGuess: result.LocalGuess,
                detected: result.Detected,
                metadataResolution: result.MetadataResolution,
                outputPath: outputPath,
                statusKind: statusKind,
                isArchiveTargetPath: isArchiveTargetPath);
            item.ReplaceExcludedSourcePaths(excludedSourcePaths ?? []);
            RefreshOutputTargetCollisions(EpisodeItems);

            AppendLog($"AKTUALISIERT: {Path.GetFileName(selectedVideoPath)} -> {Path.GetFileName(item.MainVideoPath)}");
            SetStatus("Eintrag aktualisiert", 100);
            if (ReferenceEquals(SelectedEpisodeItem, item))
            {
                ScheduleSelectedItemPlanSummaryRefresh();
            }
            return true;
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(ex.Message);
            AppendLog($"FEHLER: {Path.GetFileName(selectedVideoPath)} -> {ex.Message}");
            SetStatus("Fehler", 0);
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static int CalculatePercent(int current, int total)
    {
        return total <= 0 ? 0 : (int)Math.Round(current * 100d / total);
    }

    private static int ScaleProgress(int value, int start, int end)
    {
        value = Math.Clamp(value, 0, 100);
        return start + (int)Math.Round((end - start) * (value / 100d));
    }

    private static BatchEpisodeStatusKind DetermineInitialStatus(
        AutoDetectedEpisodeFiles detected,
        bool outputAlreadyExists,
        bool isArchiveTargetPath)
    {
        if (!detected.HasPrimaryVideoSource)
        {
            return outputAlreadyExists && isArchiveTargetPath
                ? BatchEpisodeStatusKind.ComparisonPending
                : BatchEpisodeStatusKind.Warning;
        }

        return outputAlreadyExists && isArchiveTargetPath
            ? BatchEpisodeStatusKind.ComparisonPending
            : BatchEpisodeStatusKind.Ready;
    }

    private async Task ProcessBatchScanItemAsync(
        BatchScanDirectoryContext directoryContext,
        string file,
        int index,
        int total,
        SemaphoreSlim throttler,
        BatchScanResult[] target,
        CancellationToken cancellationToken,
        Func<int> getCompletedCount,
        Action onCompleted)
    {
        var acquiredThrottler = false;
        var finished = false;
        await throttler.WaitAsync(cancellationToken);
        acquiredThrottler = true;
        try
        {
            var result = await _services.BatchScan.ScanAsync(
                directoryContext,
                file,
                OutputDirectory,
                update => HandleBatchDetectionProgress(getCompletedCount() + 1, total, file, update, cancellationToken),
                cancellationToken: cancellationToken);

            target[index] = new BatchScanResult(
                index,
                file,
                result.Detected,
                result.LocalGuess,
                result.MetadataResolution,
                result.OutputPath,
                null);
            finished = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            target[index] = new BatchScanResult(
                index,
                file,
                null,
                null,
                null,
                null,
                ex.Message);
            finished = true;
        }
        finally
        {
            if (finished)
            {
                onCompleted();
            }

            if (acquiredThrottler)
            {
                throttler.Release();
            }
        }
    }

    private void AppendLog(string line)
    {
        _logBuffer.AppendLine(line);
    }

    private void ResetLog()
    {
        _logBuffer.Reset();
    }

    private void HandleBatchDetectionProgress(
        int currentItem,
        int totalItems,
        string currentFilePath,
        DetectionProgressUpdate update,
        CancellationToken cancellationToken = default)
    {
        void ApplyUpdate()
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var baseProgress = totalItems <= 0
                ? 0
                : ((currentItem - 1) + (update.ProgressPercent / 100d)) / totalItems * 100d;

            var scaledProgress = ScaleProgress((int)Math.Round(baseProgress), 0, AutomaticCompareProgressStart);
            SetStatus(
                $"Scanne Ordner... Datei {currentItem}/{totalItems} - {Path.GetFileName(currentFilePath)} - {update.StatusText}",
                Math.Max(ProgressValue, scaledProgress));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (Application.Current.Dispatcher.CheckAccess())
        {
            ApplyUpdate();
            return;
        }

        _ = Application.Current.Dispatcher.BeginInvoke(ApplyUpdate);
    }

    private void HandleSelectedItemDetectionProgress(DetectionProgressUpdate update)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            SetStatus(update.StatusText, update.ProgressPercent);
            return;
        }

        _ = Application.Current.Dispatcher.BeginInvoke(() => SetStatus(update.StatusText, update.ProgressPercent));
    }

}
