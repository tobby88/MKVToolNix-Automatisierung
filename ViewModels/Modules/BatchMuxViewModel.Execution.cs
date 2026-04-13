using System.Threading;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

// Dieser Partial enthält die Ausführung des Batch-Laufs einschließlich Done-Aufräumen und Log-Speicherung.
internal sealed partial class BatchMuxViewModel
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
            .Where(item => !item.HasErrorStatus)
            .ToList();

        if (readyItems.Count == 0)
        {
            _dialogService.ShowWarning("Hinweis", "Es gibt keine gültigen Episoden für den Batch.");
            return;
        }

        var cancellationToken = CancellationToken.None;
        Action<string>? appendBatchRunLog = null;
        try
        {
            SetBusy(true);
            cancellationToken = BeginBatchOperation(BatchOperationKind.Execution);
            FreezeSelectedItemPlanSummaryForExecution();
            // Persistierte Batch-Logs sollen genau diesen Lauf enthalten, ohne ältere Scan-Einträge mitzuschleppen.
            var batchRunLogBuffer = new BufferedTextStore(static flush => flush(), static _ => { });
            void AppendBatchRunLogCore(string line)
            {
                AppendLog(line);
                batchRunLogBuffer.AppendLine(line);
            }
            appendBatchRunLog = AppendBatchRunLogCore;

            var approved = await EnsurePendingChecksApprovedAsync(readyItems, cancellationToken);
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
            var executablePlans = await BuildExecutionWorkItemsAsync(
                readyItems,
                planningTracker,
                AppendBatchRunLogCore,
                cancellationToken);

            if (executablePlans.Count == 0)
            {
                SetStatus("Keine weiteren Mux-Vorgänge nötig", 100);
                _dialogService.ShowInfo("Hinweis", "Alle ausgewählten Episoden sind bereits vollständig oder wurden wegen Fehlern übersprungen.");
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
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

            await _executionRunner.PrepareWorkingCopiesAsync(
                copyPreparation,
                progressTracker,
                AppendBatchRunLogCore,
                cancellationToken);
            var doneDirectory = Path.Combine(SourceDirectory, DoneFolderName);
            var executionOutcome = await _executionRunner.ExecutePlansAsync(
                executablePlans,
                doneDirectory,
                progressTracker,
                AppendBatchRunLogCore,
                cancellationToken);

            await OfferBatchDoneCleanupAsync(
                doneDirectory,
                executionOutcome.MovedDoneFiles,
                progressTracker,
                cancellationToken);
            BatchRunLogSaveResult? logSaveResult;
            try
            {
                logSaveResult = BatchRunArtifactPersistence.Persist(
                    _services.BatchLogs,
                    SourceDirectory,
                    OutputDirectory,
                    executionOutcome.NewOutputFiles,
                    executionOutcome.SuccessCount,
                    executionOutcome.WarningCount,
                    executionOutcome.ErrorCount,
                    batchRunLogBuffer,
                    AppendBatchRunLogCore);
            }
            catch (Exception ex)
            {
                AppendBatchRunLogCore($"LOG-FEHLER: {ex.Message}");
                _dialogService.ShowWarning("Warnung", $"Das Batch-Protokoll konnte nicht gespeichert werden.\n\n{ex.Message}");
                logSaveResult = null;
            }

            SetStatus(
                BuildBatchCompletionStatusText(executionOutcome),
                100);

            if (logSaveResult is not null)
            {
                ShowBatchRunArtifactInfo(logSaveResult);
            }

            ResetCompletedBatchSession();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            appendBatchRunLog?.Invoke("ABGEBROCHEN: Batch-Lauf durch Benutzer abgebrochen.");
            SetStatus("Batch abgebrochen", ProgressValue);
        }
        finally
        {
            UnfreezeSelectedItemPlanSummaryAfterExecution();
            CompleteBatchOperation(BatchOperationKind.Execution);
            SetBusy(false);
        }
    }

    /// <summary>
    /// Verdichtet die Batch-Endstatistik zu einem lesbaren Abschlussstatus.
    /// Bereits vollständige Episoden werden separat ausgewiesen, damit Cleanup-only-Fälle
    /// nicht wie "0 erfolgreich" ohne weitere Erklärung wirken.
    /// </summary>
    private static string BuildBatchCompletionStatusText(BatchExecutionOutcome executionOutcome)
    {
        var parts = new List<string>
        {
            $"Batch abgeschlossen: {executionOutcome.SuccessCount} erfolgreich",
            $"{executionOutcome.WarningCount} Warnung(en)",
            $"{executionOutcome.ErrorCount} Fehler"
        };

        if (executionOutcome.UpToDateCount > 0)
        {
            parts.Add($"{executionOutcome.UpToDateCount} bereits aktuell");
        }

        return string.Join(", ", parts);
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
        BatchRunProgressTracker progressTracker,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var doneFiles = movedDoneFiles
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (doneFiles.Count == 0)
        {
            _services.Cleanup.DeleteDirectoryIfEmpty(doneDirectory);
            _services.Cleanup.DeleteDirectoryIfEmpty(SourceDirectory);
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
                },
                cancellationToken);

            if (recycleResult.FailedFiles.Count > 0)
            {
                _dialogService.ShowWarning(
                    "Warnung",
                    "Einige Dateien aus dem Done-Ordner konnten nicht in den Papierkorb verschoben werden:\n"
                    + string.Join(Environment.NewLine, recycleResult.FailedFiles.Select(Path.GetFileName)));
            }

            _services.Cleanup.DeleteDirectoryIfEmpty(doneDirectory);
            _services.Cleanup.DeleteDirectoryIfEmpty(SourceDirectory);
            return;
        }

        if (_dialogService.AskOpenDoneDirectory(doneDirectory))
        {
            _dialogService.OpenPathWithDefaultApp(doneDirectory);
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

        // Der Info-Dialog ist modal. Erst nach der Bestätigung öffnen wir direkt die für den
        // Benutzer nützlichste Auswertung des gerade abgeschlossenen Laufs. Ohne neue Dateien
        // gibt es keine separate Prüfliste; das vollständige Laufprotokoll soll dann nicht
        // als wenig hilfreicher Fallback automatisch geöffnet werden.
        if (!string.IsNullOrWhiteSpace(logSaveResult.PreferredOpenPath))
        {
            _dialogService.OpenPathWithDefaultApp(logSaveResult.PreferredOpenPath);
        }
    }
}
