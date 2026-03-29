using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Führt die eigentliche Batch-Ausführung aus: Arbeitskopien, Mux-Läufe und Done-Verschiebungen.
/// </summary>
internal sealed class BatchExecutionRunner
{
    private readonly FileCopyService _fileCopyService;
    private readonly MuxWorkflowCoordinator _muxWorkflow;
    private readonly EpisodeCleanupService _cleanupService;

    public BatchExecutionRunner(
        FileCopyService fileCopyService,
        MuxWorkflowCoordinator muxWorkflow,
        EpisodeCleanupService cleanupService)
    {
        _fileCopyService = fileCopyService;
        _muxWorkflow = muxWorkflow;
        _cleanupService = cleanupService;
    }

    public BatchCopyPreparation BuildCopyPreparation(IReadOnlyList<BatchExecutionWorkItem> executablePlans)
    {
        var copyPlans = executablePlans
            .Select(entry => entry.Plan.WorkingCopy)
            .Where(plan => plan is not null)
            .Cast<FileCopyPlan>()
            .GroupBy(plan => plan.SourceFilePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var copyPlansToExecute = copyPlans
            .Where(_fileCopyService.NeedsCopy)
            .ToList();

        var totalCopyBytes = copyPlansToExecute.Sum(plan => plan.FileSizeBytes);
        return new BatchCopyPreparation(copyPlans, copyPlansToExecute, totalCopyBytes);
    }

    public async Task PrepareWorkingCopiesAsync(
        BatchCopyPreparation copyPreparation,
        BatchRunProgressTracker progressTracker,
        Action<string> appendLog)
    {
        if (copyPreparation.CopyPlansToExecute.Count == 0)
        {
            progressTracker.ReportCopyCompleted(reusedExistingCopies: copyPreparation.CopyPlans.Count > 0);
            if (copyPreparation.CopyPlans.Count > 0)
            {
                appendLog("ARBEITSKOPIEN: Bereits vorhandene aktuelle Arbeitskopien werden wiederverwendet.");
            }

            return;
        }

        long copiedBeforeCurrentFile = 0;

        for (var index = 0; index < copyPreparation.CopyPlansToExecute.Count; index++)
        {
            var copyPlan = copyPreparation.CopyPlansToExecute[index];
            appendLog($"KOPIERE: {Path.GetFileName(copyPlan.SourceFilePath)}");

            await _fileCopyService.CopyAsync(
                copyPlan,
                (copiedBytes, _) =>
                {
                    var combinedCopiedBytes = copiedBeforeCurrentFile + copiedBytes;

                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        progressTracker.ReportCopyProgress(
                            index + 1,
                            copyPreparation.CopyPlansToExecute.Count,
                            combinedCopiedBytes,
                            copyPreparation.TotalCopyBytes);
                    });
                });

            copiedBeforeCurrentFile += copyPlan.FileSizeBytes;
        }

        progressTracker.ReportCopyCompleted(reusedExistingCopies: false);
    }

    public async Task<BatchExecutionOutcome> ExecutePlansAsync(
        IReadOnlyList<BatchExecutionWorkItem> executablePlans,
        string doneDirectory,
        BatchRunProgressTracker progressTracker,
        Action<string> appendLog)
    {
        var successCount = 0;
        var warningCount = 0;
        var errorCount = 0;
        var movedDoneFiles = new List<string>();
        var newOutputFiles = new List<string>();

        for (var index = 0; index < executablePlans.Count; index++)
        {
            var workItem = executablePlans[index];
            var item = workItem.Item;
            var plan = workItem.Plan;
            item.RefreshArchivePresence();
            var outputExistedBeforeRun = item.ArchiveState == EpisodeArchiveState.Existing;
            item.SetStatus(BatchEpisodeStatusKind.Running);
            appendLog($"STARTE: {item.MainVideoFileName}");

            try
            {
                var result = await _muxWorkflow.ExecuteMuxAsync(
                    plan,
                    line => appendLog($"  {line}"),
                    update => progressTracker.ReportMuxProgress(index + 1, update.ProgressPercent, update.HasWarning));

                if (result.ExitCode == 0 && !result.HasWarning)
                {
                    item.SetStatus(BatchEpisodeStatusKind.Success);
                    successCount++;
                    item.RefreshArchivePresence(BatchEpisodeStatusKind.Success);
                    if (!outputExistedBeforeRun && item.ArchiveState == EpisodeArchiveState.Existing)
                    {
                        newOutputFiles.Add(item.OutputPath);
                    }

                    movedDoneFiles.AddRange(await MoveEpisodeFilesToDoneAsync(workItem, doneDirectory, index + 1, progressTracker, appendLog));
                }
                else if ((result.ExitCode == 0 && result.HasWarning)
                    || (result.ExitCode == 1 && File.Exists(item.OutputPath)))
                {
                    item.SetStatus(BatchEpisodeStatusKind.Warning);
                    warningCount++;
                    item.RefreshArchivePresence(BatchEpisodeStatusKind.Warning);
                    if (!outputExistedBeforeRun && item.ArchiveState == EpisodeArchiveState.Existing)
                    {
                        newOutputFiles.Add(item.OutputPath);
                    }

                    movedDoneFiles.AddRange(await MoveEpisodeFilesToDoneAsync(workItem, doneDirectory, index + 1, progressTracker, appendLog));
                }
                else
                {
                    item.SetStatus(BatchEpisodeStatusKind.Error, $"Fehler ({result.ExitCode})");
                    errorCount++;
                }
            }
            catch (Exception ex)
            {
                item.SetStatus(BatchEpisodeStatusKind.Error);
                appendLog($"  FEHLER: {ex.Message}");
                errorCount++;
            }
            finally
            {
                progressTracker.ReportFinalizingItem(index + 1);
            }

            progressTracker.ReportItemCompleted(index + 1);
        }

        return new BatchExecutionOutcome(
            successCount,
            warningCount,
            errorCount,
            movedDoneFiles,
            newOutputFiles);
    }

    private async Task<IReadOnlyList<string>> MoveEpisodeFilesToDoneAsync(
        BatchExecutionWorkItem workItem,
        string doneDirectory,
        int currentItemIndex,
        BatchRunProgressTracker progressTracker,
        Action<string> appendLog)
    {
        var item = workItem.Item;
        var cleanupFiles = workItem.CleanupFiles;
        if (cleanupFiles.Count == 0)
        {
            return [];
        }

        var moveResult = await _cleanupService.MoveFilesToDirectoryAsync(
            cleanupFiles,
            doneDirectory,
            (current, total, _filePath) =>
            {
                _ = Application.Current.Dispatcher.BeginInvoke(() =>
                    progressTracker.ReportMoveToDone(currentItemIndex, current, total));
            });

        appendLog($"DONE: {item.MainVideoFileName} -> {moveResult.MovedFiles.Count} Datei(en) verschoben.");

        if (moveResult.FailedFiles.Count > 0)
        {
            appendLog("  NICHT VERSCHOBEN: " + string.Join(", ", moveResult.FailedFiles.Select(Path.GetFileName)));
        }

        return moveResult.MovedFiles;
    }
}

/// <summary>
/// Vorbereitete Liste einzigartiger Arbeitskopien für einen Batch-Lauf.
/// </summary>
internal sealed record BatchCopyPreparation(
    IReadOnlyList<FileCopyPlan> CopyPlans,
    IReadOnlyList<FileCopyPlan> CopyPlansToExecute,
    long TotalCopyBytes);

/// <summary>
/// Zusammenfassung eines kompletten Batch-Laufs für UI und Log-Speicherung.
/// </summary>
internal sealed record BatchExecutionOutcome(
    int SuccessCount,
    int WarningCount,
    int ErrorCount,
    IReadOnlyList<string> MovedDoneFiles,
    IReadOnlyList<string> NewOutputFiles);
