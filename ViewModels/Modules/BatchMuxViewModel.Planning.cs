using System.Threading;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

public sealed partial class BatchMuxViewModel
{
    private async Task RefreshComparisonPlansAsync(
        IReadOnlyList<BatchEpisodeItemViewModel> items,
        bool automatic)
    {
        if (items.Count == 0)
        {
            SetStatus(
                automatic
                    ? "Scan abgeschlossen - keine vorhandenen Bibliotheksdateien zum Vergleichen"
                    : "Keine vorhandenen Bibliotheksdateien ausgewählt",
                automatic ? 100 : ProgressValue);
            return;
        }

        try
        {
            SetBusy(true);

            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                item.Status = "Läuft";
                SetStatus(
                    automatic
                        ? $"Vergleiche vorhandene Bibliotheksdateien... {index + 1}/{items.Count}"
                        : $"Aktualisiere Vergleiche... {index + 1}/{items.Count}",
                    automatic
                        ? ScaleProgress(CalculatePercent(index + 1, items.Count), AutomaticCompareProgressStart, 100)
                        : CalculatePercent(index + 1, items.Count));

                await RefreshComparisonForItemAsync(item);
            }

            SetStatus(
                automatic
                    ? $"Scan und Zielvergleiche abgeschlossen ({items.Count} vorhandene Bibliotheksdatei(en) geprüft)"
                    : $"Vergleiche aktualisiert ({items.Count} vorhandene Bibliotheksdatei(en) geprüft)",
                100);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshComparisonForItemAsync(BatchEpisodeItemViewModel item)
    {
        if (string.IsNullOrWhiteSpace(item.MainVideoPath)
            || string.IsNullOrWhiteSpace(item.OutputPath)
            || string.IsNullOrWhiteSpace(item.TitleForMux))
        {
            item.SetPlanSummary(string.Empty);
            return;
        }

        item.SetPlanSummary(File.Exists(item.OutputPath)
            ? "Zielvergleich wird berechnet..."
            : "Verwendungsplan wird berechnet...");
        item.SetUsageSummary(EpisodeUsageSummary.CreatePending(
            File.Exists(item.OutputPath) ? "Zielvergleich wird berechnet" : "Verwendungsplan wird berechnet",
            File.Exists(item.OutputPath) ? Path.GetFileName(item.OutputPath) : "Neue MKV wird erstellt"));

        try
        {
            var plan = await BuildPlanForItemAsync(item);
            item.SetPlanSummary(plan.BuildCompactSummaryText());
            item.SetUsageSummary(plan.BuildUsageSummary());

            if (File.Exists(item.OutputPath))
            {
                item.Status = plan.SkipMux ? "Ziel aktuell" : "Bereit";
            }
            else
            {
                item.Status = "Bereit";
            }
        }
        catch (Exception ex)
        {
            item.SetPlanSummary("Plan konnte noch nicht berechnet werden: " + ex.Message);
            item.SetUsageSummary(EpisodeUsageSummary.CreatePending("Plan konnte nicht berechnet werden", ex.Message));
            item.Status = "Warnung";
        }
    }

    private async Task ReviewPendingSourcesAsync()
    {
        var selectedItems = EpisodeItems.Where(item => item.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            _dialogService.ShowWarning("Hinweis", "Bitte zuerst mindestens eine Episode für den Batch auswählen.");
            return;
        }

        var readyItems = selectedItems
            .Where(item => item.Status != "Fehler")
            .ToList();

        if (readyItems.Count == 0)
        {
            _dialogService.ShowWarning("Hinweis", "Es gibt keine gültigen Episoden für den Batch.");
            return;
        }

        var approved = await EnsurePendingChecksApprovedAsync(readyItems);
        if (approved)
        {
            _dialogService.ShowInfo("Hinweis", "Alle offenen Quellen- und TVDB-Prüfungen wurden abgeschlossen.");
        }
    }

    private async Task RunBatchAsync()
    {
        var selectedItems = EpisodeItems.Where(item => item.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            _dialogService.ShowWarning("Hinweis", "Bitte mindestens eine Episode für den Batch auswählen.");
            return;
        }

        var readyItems = selectedItems
            .Where(item => item.Status != "Fehler")
            .ToList();

        if (readyItems.Count == 0)
        {
            _dialogService.ShowWarning("Hinweis", "Es gibt keine gültigen Episoden für den Batch.");
            return;
        }

        var approved = await EnsurePendingChecksApprovedAsync(readyItems);
        if (!approved)
        {
            _dialogService.ShowWarning(
                "Hinweis",
                "Der Batch wurde abgebrochen, weil nicht alle prüfpflichtigen Quellen freigegeben wurden.");
            SetStatus("Batch abgebrochen", 0);
            return;
        }

        SetStatus("Erstelle Mux-Pläne...", 0);
        var planningTracker = new BatchRunProgressTracker(readyItems.Count, SetStatus);
        var executablePlans = await BuildExecutionWorkItemsAsync(readyItems, planningTracker);

        if (executablePlans.Count == 0)
        {
            SetStatus("Keine weiteren Mux-Vorgänge nötig", 100);
            _dialogService.ShowInfo("Hinweis", "Alle ausgewählten Episoden sind bereits vollständig oder wurden wegen Fehlern übersprungen.");
            return;
        }

        var progressTracker = new BatchRunProgressTracker(executablePlans.Count, SetStatus);
        var copyPlans = executablePlans
            .Select(entry => entry.Plan.WorkingCopy)
            .Where(plan => plan is not null)
            .Cast<FileCopyPlan>()
            .GroupBy(plan => plan.SourceFilePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var copyPlansToExecute = copyPlans
            .Where(_services.FileCopy.NeedsCopy)
            .ToList();

        var totalCopyBytes = copyPlansToExecute.Sum(plan => plan.FileSizeBytes);

        if (!_dialogService.ConfirmBatchExecution(executablePlans.Count, copyPlansToExecute.Count, totalCopyBytes))
        {
            SetStatus("Abgebrochen", 0);
            return;
        }

        if (copyPlansToExecute.Count > 0)
        {
            await CopyArchiveFilesAsync(copyPlansToExecute, totalCopyBytes, progressTracker);
        }
        else
        {
            progressTracker.ReportCopyCompleted(reusedExistingCopies: copyPlans.Count > 0);
            if (copyPlans.Count > 0)
            {
                AppendLog("ARBEITSKOPIEN: Bereits vorhandene aktuelle Arbeitskopien werden wiederverwendet.");
            }
        }

        try
        {
            SetBusy(true);
            ResetLog();
            var successCount = 0;
            var warningCount = 0;
            var errorCount = 0;
            var movedDoneFiles = new List<string>();
            var newArchiveFiles = new List<string>();
            var doneDirectory = Path.Combine(SourceDirectory, DoneFolderName);

            for (var index = 0; index < executablePlans.Count; index++)
            {
                var workItem = executablePlans[index];
                var item = workItem.Item;
                var plan = workItem.Plan;
                var outputExistedBeforeRun = File.Exists(item.OutputPath);
                item.Status = "Läuft";
                AppendLog($"STARTE: {item.MainVideoFileName}");

                try
                {
                    var result = await _services.MuxWorkflow.ExecuteMuxAsync(
                        plan,
                        line => AppendLog($"  {line}"),
                        update => progressTracker.ReportMuxProgress(index + 1, update.ProgressPercent, update.HasWarning));

                    if (result.ExitCode == 0 && !result.HasWarning)
                    {
                        item.Status = "Erfolgreich";
                        successCount++;
                        if (!outputExistedBeforeRun && File.Exists(item.OutputPath))
                        {
                            newArchiveFiles.Add(item.OutputPath);
                        }
                        movedDoneFiles.AddRange(await MoveEpisodeFilesToDoneAsync(workItem, doneDirectory, index + 1, progressTracker));
                    }
                    else if ((result.ExitCode == 0 && result.HasWarning)
                        || (result.ExitCode == 1 && File.Exists(item.OutputPath)))
                    {
                        item.Status = "Warnung";
                        warningCount++;
                        if (!outputExistedBeforeRun && File.Exists(item.OutputPath))
                        {
                            newArchiveFiles.Add(item.OutputPath);
                        }
                        movedDoneFiles.AddRange(await MoveEpisodeFilesToDoneAsync(workItem, doneDirectory, index + 1, progressTracker));
                    }
                    else
                    {
                        item.Status = $"Fehler ({result.ExitCode})";
                        errorCount++;
                    }
                }
                catch (Exception ex)
                {
                    item.Status = "Fehler";
                    AppendLog($"  FEHLER: {ex.Message}");
                    errorCount++;
                }
                finally
                {
                    progressTracker.ReportFinalizingItem(index + 1);
                }

                progressTracker.ReportItemCompleted(index + 1);
            }

            await OfferBatchDoneCleanupAsync(doneDirectory, movedDoneFiles, progressTracker);
            WriteNewArchiveFileReport(newArchiveFiles);

            SetStatus(
                $"Batch abgeschlossen: {successCount} erfolgreich, {warningCount} Warnung(en), {errorCount} Fehler",
                100);
        }
        finally
        {
            SetBusy(false);
        }
    }

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

            item.ApplyDetection(
                requestedMainVideoPath: selectedVideoPath,
                localGuess: result.LocalGuess,
                detected: result.Detected,
                metadataResolution: result.MetadataResolution,
                outputPath: outputPath,
                status: outputAlreadyExists ? "Vergleich offen" : "Bereit");
            item.ReplaceExcludedSourcePaths(excludedSourcePaths ?? []);

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

    private async Task<SeriesEpisodeMuxPlan> BuildPlanForItemAsync(BatchEpisodeItemViewModel item)
    {
        return await _services.EpisodePlans.BuildPlanAsync(item);
    }

    private async Task<List<BatchExecutionWorkItem>> BuildExecutionWorkItemsAsync(
        IReadOnlyList<BatchEpisodeItemViewModel> readyItems,
        BatchRunProgressTracker progressTracker)
    {
        var executablePlans = new List<BatchExecutionWorkItem>();

        for (var index = 0; index < readyItems.Count; index++)
        {
            var item = readyItems[index];
            progressTracker.ReportPlanning(index + 1, readyItems.Count);

            try
            {
                var plan = await BuildPlanForItemAsync(item);
                if (plan.SkipMux)
                {
                    item.Status = "Ziel aktuell";
                    AppendLog($"SKIP: {item.MainVideoFileName} -> {plan.SkipReason}");
                    continue;
                }

                executablePlans.Add(new BatchExecutionWorkItem(item, plan, BuildBatchCleanupFileList(item, plan)));
            }
            catch (Exception ex)
            {
                item.Status = "Fehler";
                AppendLog($"PLAN-FEHLER: {item.MainVideoFileName} -> {ex.Message}");
            }
        }

        return executablePlans;
    }

    private string BuildOutputPath(AutoDetectedEpisodeFiles detected)
    {
        var fallbackDirectory = Path.GetDirectoryName(detected.MainVideoPath) ?? SourceDirectory;
        return BuildAutomaticOutputPath(
            fallbackDirectory,
            detected.SeriesName,
            detected.SeasonNumber,
            detected.EpisodeNumber,
            detected.SuggestedTitle);
    }

    private string BuildOutputPath(BatchEpisodeItemViewModel item)
    {
        var fallbackDirectory = Path.GetDirectoryName(item.MainVideoPath) ?? SourceDirectory;
        return BuildAutomaticOutputPath(
            fallbackDirectory,
            item.SeriesName,
            item.SeasonNumber,
            item.EpisodeNumber,
            item.TitleForMux);
    }

    private string BuildAutomaticOutputPath(
        string fallbackDirectory,
        string seriesName,
        string seasonNumber,
        string episodeNumber,
        string title)
    {
        return _services.OutputPaths.BuildOutputPath(
            fallbackDirectory,
            seriesName,
            seasonNumber,
            episodeNumber,
            title,
            OutputDirectory);
    }

    private static string GetPreferredSourceDirectory()
    {
        var downloadsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        var preferredDirectory = PreferredDownloadsSubPath.Aggregate(downloadsDirectory, Path.Combine);

        return Directory.Exists(preferredDirectory)
            ? preferredDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private void RefreshAutomaticOutputPaths()
    {
        using (EpisodeItemsView.DeferRefresh())
        {
            foreach (var item in EpisodeItems)
            {
                RefreshAutomaticOutputPath(item);
            }
        }
    }

    private void RefreshAutomaticOutputPath(BatchEpisodeItemViewModel item)
    {
        if (!item.UsesAutomaticOutputPath)
        {
            return;
        }

        item.SetAutomaticOutputPath(BuildOutputPath(item));
    }

    private void ScheduleSelectedItemPlanSummaryRefresh()
    {
        _selectedPlanSummaryRefreshCts?.Cancel();
        _selectedPlanSummaryRefreshCts?.Dispose();

        var cancellationSource = new CancellationTokenSource();
        _selectedPlanSummaryRefreshCts = cancellationSource;

        _ = RefreshSelectedItemPlanSummaryDebouncedAsync(cancellationSource.Token);
    }

    private async Task RefreshSelectedItemPlanSummaryDebouncedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(200, cancellationToken);
            await RefreshSelectedItemPlanSummaryAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshSelectedItemPlanSummaryAsync()
    {
        var item = SelectedEpisodeItem;
        var version = Interlocked.Increment(ref _selectedPlanSummaryVersion);
        if (item is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.MainVideoPath)
            || string.IsNullOrWhiteSpace(item.OutputPath)
            || string.IsNullOrWhiteSpace(item.TitleForMux))
        {
            item.SetPlanSummary(string.Empty);
            return;
        }

        try
        {
            await RefreshComparisonForItemAsync(item);
            if (version != _selectedPlanSummaryVersion || !ReferenceEquals(SelectedEpisodeItem, item))
            {
                return;
            }
        }
        catch (Exception ex)
        {
            if (version != _selectedPlanSummaryVersion || !ReferenceEquals(SelectedEpisodeItem, item))
            {
                return;
            }

            item.SetPlanSummary("Plan konnte noch nicht berechnet werden: " + ex.Message);
            item.SetUsageSummary(EpisodeUsageSummary.CreatePending("Plan konnte nicht berechnet werden", ex.Message));
        }
    }

    private async Task<IReadOnlyList<string>> MoveEpisodeFilesToDoneAsync(
        BatchExecutionWorkItem workItem,
        string doneDirectory,
        int currentItemIndex,
        BatchRunProgressTracker progressTracker)
    {
        var item = workItem.Item;
        var cleanupFiles = workItem.CleanupFiles;
        if (cleanupFiles.Count == 0)
        {
            return [];
        }

        var moveResult = await _services.Cleanup.MoveFilesToDirectoryAsync(
            cleanupFiles,
            doneDirectory,
            (current, total, filePath) =>
            {
                _ = Application.Current.Dispatcher.BeginInvoke(() =>
                    progressTracker.ReportMoveToDone(currentItemIndex, current, total));
            });

        AppendLog($"DONE: {item.MainVideoFileName} -> {moveResult.MovedFiles.Count} Datei(en) verschoben.");

        if (moveResult.FailedFiles.Count > 0)
        {
            AppendLog("  NICHT VERSCHOBEN: " + string.Join(", ", moveResult.FailedFiles.Select(Path.GetFileName)));
        }

        return moveResult.MovedFiles;
    }

    private List<string> BuildBatchCleanupFileList(BatchEpisodeItemViewModel item, SeriesEpisodeMuxPlan plan)
    {
        return _services.CleanupFiles.BuildCleanupFileList(
            item.RelatedEpisodeFilePaths.Concat(plan.GetReferencedInputFiles()),
            item.OutputPath,
            plan.WorkingCopy?.DestinationFilePath,
            SourceDirectory);
    }

    private async Task OfferBatchDoneCleanupAsync(
        string doneDirectory,
        IReadOnlyList<string> movedDoneFiles,
        BatchRunProgressTracker progressTracker)
    {
        var doneFiles = movedDoneFiles
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (doneFiles.Count == 0)
        {
            _services.Cleanup.DeleteDirectoryIfEmpty(doneDirectory);
            return;
        }

        if (_dialogService.ConfirmBatchRecycleDoneFiles(doneFiles.Count, doneDirectory))
        {
            var recycleResult = await _services.Cleanup.RecycleFilesAsync(
                doneFiles,
                (current, total, _) =>
                {
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        progressTracker.ReportRecycleProgress(current, total);
                    });
                });

            if (recycleResult.FailedFiles.Count > 0)
            {
                _dialogService.ShowWarning(
                    "Warnung",
                    "Einige Dateien aus dem Done-Ordner konnten nicht in den Papierkorb verschoben werden:\n"
                    + string.Join(Environment.NewLine, recycleResult.FailedFiles.Select(Path.GetFileName)));
            }

            _services.Cleanup.DeleteDirectoryIfEmpty(doneDirectory);
            return;
        }

        if (_dialogService.AskOpenDoneDirectory(doneDirectory))
        {
            _dialogService.OpenPathWithDefaultApp(doneDirectory);
        }
    }

    private void WriteNewArchiveFileReport(IReadOnlyList<string> newArchiveFiles)
    {
        var files = newArchiveFiles
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            return;
        }

        AppendLog(string.Empty);
        AppendLog("NEU IN SERIENBIBLIOTHEK EINGEFÜGT:");
        foreach (var file in files)
        {
            AppendLog("  " + file);
        }

        var timeStamp = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
        var reportPath = Path.Combine(SourceDirectory, $"Neu in Serienbibliothek - {timeStamp}.txt");
        File.WriteAllLines(reportPath,
        [
            "Neu in Serienbibliothek eingefügte Dateien",
            $"Erstellt am: {DateTime.Now:dd.MM.yyyy HH:mm:ss}",
            string.Empty,
            .. files
        ]);

        _dialogService.ShowInfo(
            "Neue Dateien in der Serienbibliothek",
            $"{files.Count} neue Datei(en) wurden neu in der Serienbibliothek angelegt.\n\nListe im Protokoll und in:\n{reportPath}");
    }

    private async Task CopyArchiveFilesAsync(
        IReadOnlyList<FileCopyPlan> copyPlans,
        long totalCopyBytes,
        BatchRunProgressTracker progressTracker)
    {
        long copiedBeforeCurrentFile = 0;

        for (var index = 0; index < copyPlans.Count; index++)
        {
            var copyPlan = copyPlans[index];
            AppendLog($"KOPIERE: {Path.GetFileName(copyPlan.SourceFilePath)}");

            await _services.FileCopy.CopyAsync(
                copyPlan,
                (copiedBytes, _) =>
                {
                    var combinedCopiedBytes = copiedBeforeCurrentFile + copiedBytes;

                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        progressTracker.ReportCopyProgress(index + 1, copyPlans.Count, combinedCopiedBytes, totalCopyBytes);
                    });
                });

            copiedBeforeCurrentFile += copyPlan.FileSizeBytes;
        }

        progressTracker.ReportCopyCompleted(reusedExistingCopies: false);
    }

    private async Task<bool> ReviewEpisodeAsync(BatchEpisodeItemViewModel item, bool isBatchPreparation)
    {
        return await _reviewWorkflow.ReviewManualSourceAsync(
            item,
            SetStatus,
            ProgressValue,
            isBatchPreparation
                ? $"Prüfe Quelle für '{item.Title}'..."
                : "Prüfe Quelle...",
            "Quellenprüfung abgebrochen",
            isBatchPreparation
                ? $"Quelle für '{item.Title}' freigegeben"
                : "Quelle freigegeben",
            isBatchPreparation
                ? $"Alternative Quelle für '{item.Title}' gewählt"
                : "Auf alternative Quelle umgestellt",
            tentativeExclusions => ApplyDetectionToItemAsync(item, item.DetectionSeedPath, tentativeExclusions));
    }

    private async Task<bool> ReviewEpisodeMetadataAsync(BatchEpisodeItemViewModel item, bool isBatchPreparation)
    {
        if (!item.RequiresMetadataReview || item.IsMetadataReviewApproved)
        {
            return true;
        }

        var outcome = await _reviewWorkflow.ReviewMetadataAsync(
            item,
            SetStatus,
            ProgressValue,
            isBatchPreparation
                ? $"Prüfe TVDB-Zuordnung für '{item.Title}'..."
                : "Prüfe TVDB-Zuordnung...",
            "TVDB-Prüfung abgebrochen",
            isBatchPreparation
                ? $"Lokale Erkennung für '{item.Title}' freigegeben"
                : "Lokale Erkennung freigegeben",
            isBatchPreparation
                ? $"TVDB-Zuordnung für '{item.Title}' freigegeben"
                : "TVDB-Zuordnung freigegeben",
            () =>
            {
                RefreshAutomaticOutputPath(item);
                if (ReferenceEquals(SelectedEpisodeItem, item))
                {
                    ScheduleSelectedItemPlanSummaryRefresh();
                }
            });

        return outcome != EpisodeMetadataReviewOutcome.Cancelled;
    }

    private bool CanReviewPendingSources()
    {
        return !_isBusy && EpisodeItems.Any(item => item.IsSelected && item.HasPendingChecks);
    }

    private async Task<bool> EnsurePendingChecksApprovedAsync(IReadOnlyList<BatchEpisodeItemViewModel> readyItems)
    {
        var pendingSourceItems = readyItems
            .Where(item => item.RequiresManualCheck && !item.IsManualCheckApproved)
            .ToList();

        var pendingMetadataItems = readyItems
            .Where(item => item.RequiresMetadataReview && !item.IsMetadataReviewApproved)
            .ToList();

        if (pendingSourceItems.Count == 0 && pendingMetadataItems.Count == 0)
        {
            SetStatus("Keine offenen Pflichtprüfungen", ProgressValue);
            return true;
        }

        SetStatus("Pflichtprüfungen werden vorbereitet...", 0);

        foreach (var item in pendingSourceItems)
        {
            SelectedEpisodeItem = item;
            var approved = await ReviewEpisodeAsync(item, isBatchPreparation: true);
            if (!approved)
            {
                return false;
            }
        }

        foreach (var item in pendingMetadataItems)
        {
            SelectedEpisodeItem = item;
            var approved = await ReviewEpisodeMetadataAsync(item, isBatchPreparation: true);
            if (!approved)
            {
                return false;
            }
        }

        return true;
    }

    private static string ResolveSelectedItemDirectory(BatchEpisodeItemViewModel item)
    {
        var paths = item.SourceFilePaths
            .Concat([item.RequestedMainVideoPath, item.OutputPath])
            .Where(path => !string.IsNullOrWhiteSpace(path));

        foreach (var path in paths)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                return directory;
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private void SelectAllEpisodes()
    {
        _episodeCollection.SelectAll();
    }

    private void DeselectAllEpisodes()
    {
        _episodeCollection.DeselectAll();
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

    private async Task ProcessBatchScanItemAsync(
        string file,
        int index,
        int total,
        SemaphoreSlim throttler,
        BatchScanResult[] target,
        Func<int> getCompletedCount,
        Action onCompleted)
    {
        await throttler.WaitAsync();
        try
        {
            var result = await _services.BatchScan.ScanAsync(
                file,
                OutputDirectory,
                update => HandleBatchDetectionProgress(getCompletedCount() + 1, total, file, update));

            target[index] = new BatchScanResult(
                index,
                file,
                result.Detected,
                result.LocalGuess,
                result.MetadataResolution,
                result.OutputPath,
                null);
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
        }
        finally
        {
            onCompleted();
            throttler.Release();
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
        DetectionProgressUpdate update)
    {
        void ApplyUpdate()
        {
            var baseProgress = totalItems <= 0
                ? 0
                : ((currentItem - 1) + (update.ProgressPercent / 100d)) / totalItems * 100d;

            var scaledProgress = ScaleProgress((int)Math.Round(baseProgress), 0, AutomaticCompareProgressStart);
            SetStatus(
                $"Scanne Ordner... {currentItem}/{totalItems} - {Path.GetFileName(currentFilePath)} - {update.StatusText}",
                Math.Max(ProgressValue, scaledProgress));
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
