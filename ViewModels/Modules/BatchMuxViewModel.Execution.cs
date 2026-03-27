using System.Threading;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

// Dieser Partial enthält die Ausführung des Batch-Laufs einschließlich Done-Aufräumen und Log-Speicherung.
public sealed partial class BatchMuxViewModel
{
    private async Task RunBatchAsync()
    {
        var selectedItems = EpisodeItems.Where(item => item.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            _dialogService.ShowWarning("Hinweis", "Bitte mindestens eine Episode f\u00FCr den Batch ausw\u00E4hlen.");
            return;
        }

        var readyItems = selectedItems
            .Where(item => !item.HasErrorStatus)
            .ToList();

        if (readyItems.Count == 0)
        {
            _dialogService.ShowWarning("Hinweis", "Es gibt keine g\u00FCltigen Episoden f\u00FCr den Batch.");
            return;
        }

        try
        {
            SetBusy(true);

            var approved = await EnsurePendingChecksApprovedAsync(readyItems);
            if (!approved)
            {
                _dialogService.ShowWarning(
                    "Hinweis",
                    "Der Batch wurde abgebrochen, weil nicht alle pr\u00FCfpflichtigen Quellen freigegeben wurden.");
                SetStatus("Batch abgebrochen", 0);
                return;
            }

            SetStatus("Erstelle Mux-Pl\u00E4ne...", 0);
            var planningTracker = new BatchRunProgressTracker(readyItems.Count, SetStatus);
            var executablePlans = await BuildExecutionWorkItemsAsync(readyItems, planningTracker);

            if (executablePlans.Count == 0)
            {
                SetStatus("Keine weiteren Mux-Vorg\u00E4nge n\u00F6tig", 100);
                _dialogService.ShowInfo("Hinweis", "Alle ausgew\u00E4hlten Episoden sind bereits vollst\u00E4ndig oder wurden wegen Fehlern \u00FCbersprungen.");
                return;
            }

            var progressTracker = new BatchRunProgressTracker(executablePlans.Count, SetStatus);
            var copyPreparation = _executionRunner.BuildCopyPreparation(executablePlans);

            if (!_dialogService.ConfirmBatchExecution(
                executablePlans.Count,
                copyPreparation.CopyPlansToExecute.Count,
                copyPreparation.TotalCopyBytes))
            {
                SetStatus("Abgebrochen", 0);
                return;
            }

            await _executionRunner.PrepareWorkingCopiesAsync(copyPreparation, progressTracker, AppendLog);
            ResetLog();
            var doneDirectory = Path.Combine(SourceDirectory, DoneFolderName);
            var executionOutcome = await _executionRunner.ExecutePlansAsync(
                executablePlans,
                doneDirectory,
                progressTracker,
                AppendLog);

            await OfferBatchDoneCleanupAsync(doneDirectory, executionOutcome.MovedDoneFiles, progressTracker);
            var logSaveResult = PersistBatchRunArtifacts(
                executionOutcome.NewOutputFiles,
                executionOutcome.SuccessCount,
                executionOutcome.WarningCount,
                executionOutcome.ErrorCount);

            SetStatus(
                $"Batch abgeschlossen: {executionOutcome.SuccessCount} erfolgreich, {executionOutcome.WarningCount} Warnung(en), {executionOutcome.ErrorCount} Fehler",
                100);

            if (logSaveResult is not null)
            {
                ShowBatchRunArtifactInfo(logSaveResult);
            }
        }
        finally
        {
            SetBusy(false);
        }
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
                (current, total, _filePath) =>
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

    private BatchRunLogSaveResult? PersistBatchRunArtifacts(
        IReadOnlyList<string> newOutputFiles,
        int successCount,
        int warningCount,
        int errorCount)
    {
        var files = newOutputFiles
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            AppendLog(string.Empty);
            AppendLog("NEU ERZEUGTE AUSGABEDATEIEN: keine");
        }
        else
        {
            AppendLog(string.Empty);
            AppendLog("NEU ERZEUGTE AUSGABEDATEIEN:");
            foreach (var file in files)
            {
                AppendLog("  " + file);
            }
        }

        try
        {
            return _services.BatchLogs.SaveBatchRunArtifacts(
                SourceDirectory,
                OutputDirectory,
                _logBuffer.GetTextSnapshot(),
                files,
                successCount,
                warningCount,
                errorCount);
        }
        catch (Exception ex)
        {
            AppendLog($"LOG-FEHLER: {ex.Message}");
            _dialogService.ShowWarning("Warnung", $"Das Batch-Protokoll konnte nicht gespeichert werden.\n\n{ex.Message}");
            return null;
        }
    }

    private void ShowBatchRunArtifactInfo(BatchRunLogSaveResult logSaveResult)
    {
        var lines = new List<string>
        {
            $"Batch-Protokoll gespeichert unter:",
            logSaveResult.BatchLogPath
        };

        if (logSaveResult.NewOutputFiles.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"{logSaveResult.NewOutputFiles.Count} neue Datei(en) wurden in diesem Lauf erzeugt.");

            if (!string.IsNullOrWhiteSpace(logSaveResult.NewOutputListPath))
            {
                lines.Add("Dateiliste:");
                lines.Add(logSaveResult.NewOutputListPath!);
            }
        }

        _dialogService.ShowInfo("Batch-Protokoll", string.Join(Environment.NewLine, lines));
    }
}
