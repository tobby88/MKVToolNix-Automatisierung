using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Führt die eigentliche Batch-Ausführung aus: Arbeitskopien, Mux-Läufe und Done-Verschiebungen.
/// </summary>
internal sealed class BatchExecutionRunner
{
    private readonly IFileCopyService _fileCopyService;
    private readonly IMuxWorkflowCoordinator _muxWorkflow;
    private readonly IEpisodeCleanupService _cleanupService;

    public BatchExecutionRunner(
        IFileCopyService fileCopyService,
        IMuxWorkflowCoordinator muxWorkflow,
        IEpisodeCleanupService cleanupService)
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
        Action<string> appendLog,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
            cancellationToken.ThrowIfCancellationRequested();
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
                            copyPreparation.TotalCopyBytes,
                            copiedBytes,
                            copyPlan.FileSizeBytes);
                    });
                },
                cancellationToken);

            copiedBeforeCurrentFile += copyPlan.FileSizeBytes;
        }

        progressTracker.ReportCopyCompleted(reusedExistingCopies: false);
    }

    public async Task<BatchExecutionOutcome> ExecutePlansAsync(
        IReadOnlyList<BatchExecutionWorkItem> executablePlans,
        string doneDirectory,
        BatchRunProgressTracker progressTracker,
        Action<string> appendLog,
        CancellationToken cancellationToken = default,
        Action<BatchEpisodeItemViewModel>? onCurrentItemChanged = null)
    {
        var successCount = 0;
        var warningCount = 0;
        var errorCount = 0;
        var upToDateCount = 0;
        var movedDoneFiles = new List<string>();
        var failedDoneMoveFiles = new List<string>();
        var newOutputFiles = new List<string>();
        var newOutputMetadata = new List<BatchOutputMetadataEntry>();

        for (var index = 0; index < executablePlans.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var workItem = executablePlans[index];
            var item = workItem.Item;
            var plan = workItem.Plan;
            item.RefreshArchivePresence();
            var outputExistedBeforeRun = item.ArchiveState == EpisodeArchiveState.Existing;
            var outputSnapshotBeforeRun = FileStateSnapshot.TryCreate(item.OutputPath);
            item.SetStatus(BatchEpisodeStatusKind.Running);
            onCurrentItemChanged?.Invoke(item);
            appendLog($"STARTE: {item.MainVideoFileName}");

            try
            {
                if (plan.SkipMux)
                {
                    upToDateCount++;
                    item.SetStatus(BatchEpisodeStatusKind.UpToDate);
                    appendLog($"  KEIN MUX: {plan.SkipReason ?? "Zieldatei bereits aktuell."}");

                    var doneMoveResult = await MoveEpisodeFilesToDoneAsync(
                        workItem,
                        doneDirectory,
                        index + 1,
                        progressTracker,
                        appendLog,
                        cancellationToken);
                    movedDoneFiles.AddRange(doneMoveResult.MovedFiles);
                    failedDoneMoveFiles.AddRange(doneMoveResult.FailedFiles);
                    if (doneMoveResult.FailedFiles.Count > 0)
                    {
                        warningCount++;
                        item.SetStatus(BatchEpisodeStatusKind.Warning, "Warnung (Quellen nicht vollständig verschoben)");
                    }
                    if (doneMoveResult.WasCanceled)
                    {
                        appendLog(
                            $"  ABGEBROCHEN: Done-Verschiebung nach {doneMoveResult.MovedFiles.Count} Datei(en), {doneMoveResult.PendingFiles.Count} Datei(en) noch offen.");
                        throw new OperationCanceledException(cancellationToken);
                    }

                    continue;
                }

                var result = await _muxWorkflow.ExecuteMuxAsync(
                    plan,
                    line => appendLog($"  {line}"),
                    update => progressTracker.ReportMuxProgress(index + 1, update.ProgressPercent, update.HasWarning),
                    cancellationToken,
                    MuxWorkflowTemporaryCleanup.KeepWorkingCopy);
                var outcomeKind = MuxExecutionResultClassifier.Classify(
                    result,
                    outputSnapshotBeforeRun,
                    item.OutputPath);

                if (outcomeKind == MuxExecutionOutcomeKind.Success)
                {
                    item.SetStatus(BatchEpisodeStatusKind.Success);
                    successCount++;
                    item.RefreshArchivePresence(BatchEpisodeStatusKind.Success);
                    if (!outputExistedBeforeRun && item.ArchiveState == EpisodeArchiveState.Existing)
                    {
                        AddNewOutput(newOutputFiles, newOutputMetadata, item);
                    }

                    var doneMoveResult = await MoveEpisodeFilesToDoneAsync(
                        workItem,
                        doneDirectory,
                        index + 1,
                        progressTracker,
                        appendLog,
                        cancellationToken);
                    movedDoneFiles.AddRange(doneMoveResult.MovedFiles);
                    failedDoneMoveFiles.AddRange(doneMoveResult.FailedFiles);
                    if (doneMoveResult.FailedFiles.Count > 0)
                    {
                        warningCount++;
                        item.SetStatus(BatchEpisodeStatusKind.Warning, "Warnung (Quellen nicht vollständig verschoben)");
                    }
                    if (doneMoveResult.WasCanceled)
                    {
                        appendLog(
                            $"  ABGEBROCHEN: Done-Verschiebung nach {doneMoveResult.MovedFiles.Count} Datei(en), {doneMoveResult.PendingFiles.Count} Datei(en) noch offen.");
                        throw new OperationCanceledException(cancellationToken);
                    }
                }
                else if (outcomeKind == MuxExecutionOutcomeKind.Warning)
                {
                    warningCount++;
                    var warningStatusText = BuildWarningStatusText(plan, result);
                    item.RefreshArchivePresence(BatchEpisodeStatusKind.Warning, warningStatusText);
                    appendLog($"  WARNUNG: {warningStatusText}");
                    if (!outputExistedBeforeRun && item.ArchiveState == EpisodeArchiveState.Existing)
                    {
                        AddNewOutput(newOutputFiles, newOutputMetadata, item);
                    }

                    var doneMoveResult = await MoveEpisodeFilesToDoneAsync(
                        workItem,
                        doneDirectory,
                        index + 1,
                        progressTracker,
                        appendLog,
                        cancellationToken);
                    movedDoneFiles.AddRange(doneMoveResult.MovedFiles);
                    failedDoneMoveFiles.AddRange(doneMoveResult.FailedFiles);
                    if (doneMoveResult.FailedFiles.Count > 0)
                    {
                        item.SetStatus(BatchEpisodeStatusKind.Warning, "Warnung (Quellen nicht vollständig verschoben)");
                    }
                    if (doneMoveResult.WasCanceled)
                    {
                        appendLog(
                            $"  ABGEBROCHEN: Done-Verschiebung nach {doneMoveResult.MovedFiles.Count} Datei(en), {doneMoveResult.PendingFiles.Count} Datei(en) noch offen.");
                        throw new OperationCanceledException(cancellationToken);
                    }
                }
                else
                {
                    item.SetStatus(BatchEpisodeStatusKind.Error, $"Fehler ({result.ExitCode})");
                    errorCount++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                item.SetStatus(BatchEpisodeStatusKind.Cancelled);
                appendLog($"  ABGEBROCHEN: {item.MainVideoFileName}");
                throw;
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
            upToDateCount,
            movedDoneFiles,
            failedDoneMoveFiles,
            newOutputFiles,
            newOutputMetadata);
    }

    private async Task<BatchDoneMoveResult> MoveEpisodeFilesToDoneAsync(
        BatchExecutionWorkItem workItem,
        string doneDirectory,
        int currentItemIndex,
        BatchRunProgressTracker progressTracker,
        Action<string> appendLog,
        CancellationToken cancellationToken = default)
    {
        var item = workItem.Item;
        var cleanupFiles = BuildDoneCleanupFileList(workItem);
        if (cleanupFiles.Count == 0)
        {
            return new BatchDoneMoveResult([], [], [], WasCanceled: false);
        }

        var moveResult = await _cleanupService.MoveFilesToDirectoryAsync(
            cleanupFiles,
            doneDirectory,
            (current, total, _filePath) =>
            {
                _ = Application.Current.Dispatcher.BeginInvoke(() =>
                    progressTracker.ReportMoveToDone(currentItemIndex, current, total));
            },
            cancellationToken);

        appendLog($"DONE: {item.MainVideoFileName} -> {moveResult.MovedFiles.Count} Datei(en) verschoben.");

        if (moveResult.FailedFiles.Count > 0)
        {
            appendLog("  NICHT VERSCHOBEN: " + string.Join(", ", moveResult.FailedFiles.Select(Path.GetFileName)));
        }
        if (moveResult.PendingFiles.Count > 0)
        {
            appendLog("  NOCH OFFEN: " + string.Join(", ", moveResult.PendingFiles.Select(Path.GetFileName)));
        }

        _cleanupService.DeleteEmptyParentDirectories(cleanupFiles, Path.GetDirectoryName(doneDirectory));

        return new BatchDoneMoveResult(
            moveResult.MovedFiles,
            moveResult.FailedFiles,
            moveResult.PendingFiles,
            moveResult.WasCanceled);
    }

    private static IReadOnlyList<string> BuildDoneCleanupFileList(BatchExecutionWorkItem workItem)
    {
        var cleanupFiles = workItem.CleanupFiles.ToList();

        // Arbeitskopien liegen technisch im Quellordner, wurden bisher aber bewusst aus
        // der normalen Quellenliste herausgefiltert, damit sie im Single-Modus nicht
        // versehentlich als echte Quelle gelten. Im Batch sollen sie nach erfolgreichem
        // Lauf trotzdem denselben Done-/Papierkorb-Pfad wie die übrigen Quellen nehmen.
        var workingCopyPath = workItem.Plan.WorkingCopy?.DestinationFilePath;
        if (!string.IsNullOrWhiteSpace(workingCopyPath)
            && File.Exists(workingCopyPath)
            && !cleanupFiles.Contains(workingCopyPath, StringComparer.OrdinalIgnoreCase))
        {
            cleanupFiles.Add(workingCopyPath);
        }

        return cleanupFiles
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildWarningStatusText(SeriesEpisodeMuxPlan plan, MuxExecutionResult result)
    {
        return result.HasWarning
            ? $"Warnung ({plan.ExecutionToolDisplayName} meldet Warnungen)"
            : plan.HasHeaderEdits
                ? "Warnung (Header aktualisiert, Exit-Code 1)"
                : "Warnung (Datei erstellt, Exit-Code 1)";
    }

    private static void AddNewOutput(
        List<string> newOutputFiles,
        List<BatchOutputMetadataEntry> newOutputMetadata,
        BatchEpisodeItemViewModel item)
    {
        if (newOutputFiles.Contains(item.OutputPath, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        newOutputFiles.Add(item.OutputPath);
        newOutputMetadata.Add(CreateNewOutputMetadata(item));
    }

    private static BatchOutputMetadataEntry CreateNewOutputMetadata(BatchEpisodeItemViewModel item)
    {
        return BatchOutputMetadataEntryFactory.Create(
            item.OutputPath,
            item.SeriesName,
            item.SeasonNumber,
            item.EpisodeNumber,
            item.Title,
            item.TvdbEpisodeId,
            item.TvdbSeriesId,
            item.TvdbSeriesName);
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
    int UpToDateCount,
    IReadOnlyList<string> MovedDoneFiles,
    IReadOnlyList<string> FailedDoneMoveFiles,
    IReadOnlyList<string> NewOutputFiles,
    IReadOnlyList<BatchOutputMetadataEntry> NewOutputMetadata);

/// <summary>
/// Ergebnis des Done-Verschiebens, damit erfolgreiche Mux-Läufe bei gesperrten Quelldateien
/// nicht abbrechen, aber trotzdem als Cleanup-Warnung im Batch sichtbar bleiben.
/// </summary>
internal sealed record BatchDoneMoveResult(
    IReadOnlyList<string> MovedFiles,
    IReadOnlyList<string> FailedFiles,
    IReadOnlyList<string> PendingFiles,
    bool WasCanceled);
