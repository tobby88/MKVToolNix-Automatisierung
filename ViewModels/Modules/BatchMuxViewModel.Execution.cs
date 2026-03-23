using System.Threading;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

public sealed partial class BatchMuxViewModel
{
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
                item.RefreshArchivePresence();
                var outputExistedBeforeRun = item.ArchiveState == EpisodeArchiveState.Existing;
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
                        item.RefreshArchivePresence();
                        if (!outputExistedBeforeRun && item.ArchiveState == EpisodeArchiveState.Existing)
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
                        item.RefreshArchivePresence();
                        if (!outputExistedBeforeRun && item.ArchiveState == EpisodeArchiveState.Existing)
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

}
