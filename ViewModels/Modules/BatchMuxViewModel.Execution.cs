using System.Threading;
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

        var unresolvedEpisodeCodeItems = readyItems
            .Where(item => !EpisodeFileNameHelper.HasKnownEpisodeCode(item.SeasonNumber, item.EpisodeNumber))
            .ToList();
        if (unresolvedEpisodeCodeItems.Count > 0)
        {
            foreach (var item in unresolvedEpisodeCodeItems)
            {
                item.SetStatus(BatchEpisodeStatusKind.Warning, "Episodencode fehlt (SxxExx). Bitte vor dem Batch prüfen.");
            }

            _dialogService.ShowWarning(
                "Episodencode fehlt",
                $"{unresolvedEpisodeCodeItems.Count} ausgewählte Episode(n) haben noch Staffel oder Folge 'xx'. Bitte TVDB-Zuordnung prüfen oder Staffel/Folge manuell korrigieren.");
            SetStatus("Batch blockiert: Episodencode fehlt", ProgressValue);
            return;
        }

        var cancellationToken = CancellationToken.None;
        var planSummaryFrozenForExecution = false;
        var planningErrorCount = 0;
        var planningOnlyUpToDateCount = 0;
        BatchExecutionOutcome? executionOutcome = null;
        BatchRunLogSaveResult? logSaveResult = null;
        // Persistierte Logs enthalten nur diesen Lauf, auch wenn er vorzeitig endet.
        var batchRunLogBuffer = new BufferedTextStore(static flush => flush(), static _ => { });
        void AppendBatchRunLogCore(string line)
        {
            AppendLog(line);
            batchRunLogBuffer.AppendLine(line);
        }

        try
        {
            SetBusy(true);
            cancellationToken = BeginBatchOperation(BatchOperationKind.Execution);

            var approved = await EnsurePendingChecksApprovedAsync(readyItems, cancellationToken);
            if (!approved)
            {
                _dialogService.ShowWarning(
                    "Hinweis",
                    BuildPendingReviewAbortMessage(readyItems));
                SetStatus("Batch abgebrochen: Prüfungen offen", 0);
                return;
            }

            // Metadaten-/Quellenreviews können den Episodencode nach der ersten Prüfung ändern.
            if (readyItems.Any(item => !EpisodeFileNameHelper.HasKnownEpisodeCode(item.SeasonNumber, item.EpisodeNumber)))
            {
                _dialogService.ShowWarning("Episodencode fehlt", "Nach der Prüfung ist mindestens ein Episodencode noch offen. Bitte Staffel/Folge korrigieren.");
                SetStatus("Batch blockiert: Episodencode fehlt", ProgressValue);
                return;
            }

            FreezeSelectedItemPlanSummaryForExecution();
            planSummaryFrozenForExecution = true;
            SetStatus("Erstelle Mux-Pläne...", 0);
            var planningTracker = new BatchRunProgressTracker(readyItems.Count, SetStatusFromAnyThread);
            var executablePlans = await BuildExecutionWorkItemsAsync(
                readyItems,
                planningTracker,
                AppendBatchRunLogCore,
                cancellationToken);
            planningErrorCount = readyItems.Count(item => item.HasErrorStatus);
            var scheduledItems = executablePlans.Select(entry => entry.Item).ToHashSet();
            planningOnlyUpToDateCount = readyItems.Count(item => item.StatusKind == BatchEpisodeStatusKind.UpToDate
                && !scheduledItems.Contains(item));

            cancellationToken.ThrowIfCancellationRequested();
            if (readyItems.Any(item => item.HasPendingChecks))
            {
                _dialogService.ShowWarning("Hinweis", BuildPendingReviewAbortMessage(readyItems));
                SetStatus("Batch blockiert: Neuer Plan benötigt Freigabe", ProgressValue);
                return;
            }

            var conflictingPlans = executablePlans
                .GroupBy(entry => NormalizeOutputCollisionPath(entry.Plan.OutputFilePath), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .SelectMany(group => group)
                .ToList();
            if (conflictingPlans.Count > 0)
            {
                foreach (var entry in conflictingPlans)
                {
                    entry.Item.SetStatus(BatchEpisodeStatusKind.Warning, "Ausgabeziel mehrfach belegt");
                }

                _dialogService.ShowWarning("Ausgabezielkonflikt", "Mehrere ausgewählte Episoden schreiben dieselbe Ausgabedatei. Bitte unterschiedliche Ziele wählen oder die Auswahl reduzieren.");
                SetStatus("Batch blockiert: Ausgabeziel mehrfach belegt", ProgressValue);
                return;
            }

            var inputOwners = executablePlans
                .SelectMany(entry => entry.Plan.GetReferencedInputFiles()
                    .Select(path => (Path: NormalizeOutputCollisionPath(path), Entry: entry)))
                .GroupBy(value => value.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Select(value => value.Entry).ToList(), StringComparer.OrdinalIgnoreCase);
            if (executablePlans.Any(entry => !entry.Plan.SkipMux
                && inputOwners.TryGetValue(NormalizeOutputCollisionPath(entry.Plan.OutputFilePath), out var owners)
                && owners.Any(owner => !ReferenceEquals(owner, entry))))
            {
                _dialogService.ShowWarning("Ausgabe-/Quellkonflikt", "Ein Ausgabeziel wird von einer anderen ausgewählten Episode als Quelle benötigt. Bitte Ausgabeziele oder Auswahl korrigieren.");
                SetStatus("Batch blockiert: Ausgabe überschreibt andere Quelle", ProgressValue);
                return;
            }

            if (executablePlans.Count == 0)
            {
                executionOutcome = BatchExecutionOutcome.Empty with
                {
                    ErrorCount = planningErrorCount,
                    UpToDateCount = planningOnlyUpToDateCount
                };
                InvalidateBatchProgressCallbacks();
                SetStatus(BuildBatchCompletionStatusText(executionOutcome), 100);
                logSaveResult = TryPersistBatchRunArtifacts(executionOutcome, batchRunLogBuffer, AppendBatchRunLogCore);
                if (logSaveResult is not null)
                {
                    ShowBatchRunArtifactInfo(logSaveResult);
                }
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var progressTracker = new BatchRunProgressTracker(executablePlans.Count, SetStatusFromAnyThread);
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
            executionOutcome = await _executionRunner.ExecutePlansAsync(
                executablePlans,
                doneDirectory,
                progressTracker,
                AppendBatchRunLogCore,
                cancellationToken,
                item => SelectedEpisodeItem = item);
            executionOutcome = executionOutcome with
            {
                ErrorCount = executionOutcome.ErrorCount + planningErrorCount,
                UpToDateCount = executionOutcome.UpToDateCount + planningOnlyUpToDateCount
            };

            if (executionOutcome.FailedDoneMoveFiles.Count > 0)
            {
                _dialogService.ShowWarning(
                    "Batch-Cleanup",
                    "Einige Quelldateien konnten nicht in den Done-Ordner verschoben werden. Der Batch wurde fortgesetzt; Details stehen im Batch-Protokoll.");
            }

            if (executionOutcome.WasCanceled)
            {
                AppendBatchRunLogCore("ABGEBROCHEN: Batch-Lauf durch Benutzer abgebrochen; abgeschlossene Ergebnisse bleiben erhalten.");
            }
            else
            {
                AppendBatchRunLogCore("MUX-ERGEBNIS: Ausführung beendet. Artefakte werden vor dem optionalen Papierkorb-Cleanup gespeichert.");
            }

            // Kein abgebrochener Cleanup darf erfolgreiche Ausgaben und ihre Metadaten verschlucken.
            logSaveResult = TryPersistBatchRunArtifacts(executionOutcome, batchRunLogBuffer, AppendBatchRunLogCore);
            if (!executionOutcome.WasCanceled && logSaveResult is not null)
            {
                var cleanupCompleted = await OfferBatchDoneCleanupAsync(
                    doneDirectory,
                    executionOutcome.MovedDoneFiles,
                    progressTracker,
                    cancellationToken);
                if (!cleanupCompleted)
                {
                    executionOutcome = executionOutcome with { WasCanceled = true };
                    const string cleanupMessage = "ABGEBROCHEN: Papierkorb-Cleanup abgebrochen; Mux-Ergebnisse und Reports bleiben erhalten.";
                    AppendBatchRunLogCore(cleanupMessage);
                    PersistBatchCleanupLog(cleanupMessage);
                }
            }

            InvalidateBatchProgressCallbacks();
            SetStatus(
                BuildBatchCompletionStatusText(executionOutcome),
                executionOutcome.WasCanceled ? ProgressValue : 100);

            if (logSaveResult is not null)
            {
                ShowBatchRunArtifactInfo(logSaveResult);
            }

            if (!executionOutcome.WasCanceled && executionOutcome.ErrorCount == 0 && logSaveResult is not null)
            {
                ResetCompletedBatchSession();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            InvalidateBatchProgressCallbacks();
            const string cancellationMessage = "ABGEBROCHEN: Batch-Lauf durch Benutzer abgebrochen; abgeschlossene Ergebnisse bleiben erhalten.";
            AppendBatchRunLogCore(cancellationMessage);
            executionOutcome ??= BatchExecutionOutcome.Empty with
            {
                ErrorCount = readyItems.Count(item => item.HasErrorStatus),
                UpToDateCount = planningOnlyUpToDateCount
            };
            executionOutcome = executionOutcome with { WasCanceled = true };
            if (logSaveResult is null)
            {
                logSaveResult = TryPersistBatchRunArtifacts(executionOutcome, batchRunLogBuffer, AppendBatchRunLogCore);
            }
            else
            {
                // Nur den Cleanup-Nachtrag speichern, keine zweite JSON-/Dateiliste erzeugen.
                PersistBatchCleanupLog(cancellationMessage);
            }
            SetStatus(BuildBatchCompletionStatusText(executionOutcome), ProgressValue);
            if (logSaveResult is not null)
            {
                ShowBatchRunArtifactInfo(logSaveResult);
            }
        }
        finally
        {
            if (planSummaryFrozenForExecution)
            {
                UnfreezeSelectedItemPlanSummaryAfterExecution();
            }

            CompleteBatchOperation(BatchOperationKind.Execution);
            SetBusy(false);
        }
    }

    private BatchRunLogSaveResult? TryPersistBatchRunArtifacts(
        BatchExecutionOutcome outcome,
        BufferedTextStore logBuffer,
        Action<string> appendLog)
    {
        try
        {
            return BatchRunArtifactPersistence.Persist(
                _services.BatchLogs,
                SourceDirectory,
                OutputDirectory,
                outcome.NewOutputFiles,
                outcome.NewOutputMetadata,
                outcome.SuccessCount,
                outcome.WarningCount,
                outcome.ErrorCount,
                logBuffer,
                appendLog);
        }
        catch (Exception ex)
        {
            appendLog($"LOG-FEHLER: {ex.Message}");
            _dialogService.ShowWarning("Warnung", $"Das Batch-Protokoll konnte nicht gespeichert werden. Der optionale Papierkorb-Cleanup wird nicht gestartet.\n\n{ex.Message}");
            return null;
        }
    }

    private void PersistBatchCleanupLog(string message)
    {
        try
        {
            _services.BatchLogs.SaveBatchRunArtifacts(
                SourceDirectory, OutputDirectory, message, [], 0, 0, 0, runLabel: "Batch-Cleanup");
        }
        catch (Exception ex)
        {
            AppendLog($"LOG-FEHLER beim Cleanup-Nachtrag: {ex.Message}");
            _dialogService.ShowWarning("Warnung", $"Der Cleanup-Nachtrag konnte nicht gespeichert werden. Die zuvor gespeicherten Mux-Reports bleiben erhalten.\n\n{ex.Message}");
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
            $"{(executionOutcome.WasCanceled ? "Batch abgebrochen" : "Batch abgeschlossen")}: {executionOutcome.SuccessCount} erfolgreich",
            $"{executionOutcome.WarningCount} Warnung(en)",
            $"{executionOutcome.ErrorCount} Fehler"
        };

        if (executionOutcome.UpToDateCount > 0)
        {
            parts.Add($"{executionOutcome.UpToDateCount} bereits aktuell");
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Erklärt nach einem abgebrochenen Pflichtcheck konkret, welche Freigabearten noch offen sind.
    /// </summary>
    private static string BuildPendingReviewAbortMessage(IReadOnlyList<BatchEpisodeItemViewModel> readyItems)
    {
        var pendingReviewParts = new List<string>();
        var pendingSourceCount = readyItems.Count(item => item.HasPendingManualCheck);
        var pendingMetadataCount = readyItems.Count(item => item.HasPendingMetadataReview);
        var pendingPlanCount = readyItems.Count(item => item.HasPendingPlanReview);

        if (pendingSourceCount > 0)
        {
            pendingReviewParts.Add($"{pendingSourceCount} Quellenprüfung(en)");
        }

        if (pendingMetadataCount > 0)
        {
            pendingReviewParts.Add($"{pendingMetadataCount} TVDB-Prüfung(en)");
        }

        if (pendingPlanCount > 0)
        {
            pendingReviewParts.Add($"{pendingPlanCount} Archiv-/Planhinweis(e)");
        }

        if (pendingReviewParts.Count == 0)
        {
            return "Der Batch wurde abgebrochen, bevor alle Pflichtprüfungen abgeschlossen wurden.";
        }

        return "Der Batch wurde abgebrochen, weil noch Pflichtprüfungen offen sind: "
            + string.Join(", ", pendingReviewParts)
            + ".";
    }

    private List<string> BuildBatchCleanupFileList(BatchEpisodeItemViewModel item, SeriesEpisodeMuxPlan plan)
    {
        var regularCleanupFiles = _services.CleanupFiles.BuildCleanupFileList(
            item.RelatedEpisodeFilePaths.Concat(plan.GetReferencedInputFiles()),
            item.OutputPath,
            plan.WorkingCopy?.DestinationFilePath,
            SourceDirectory,
            item.ExcludedSourcePaths);
        var rejectedSourceCleanupFiles = _services.CleanupFiles.BuildRejectedSourceCleanupFileList(
            item.RejectedManualCheckSourcePaths,
            item.OutputPath,
            plan.WorkingCopy?.DestinationFilePath,
            SourceDirectory);

        return regularCleanupFiles
            .Concat(rejectedSourceCleanupFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<bool> OfferBatchDoneCleanupAsync(
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
            DeleteEmptyBatchCleanupDirectory(doneDirectory);
            return true;
        }

        if (_dialogService.ConfirmBatchRecycleDoneFiles(doneFiles.Count, doneDirectory))
        {
            var recycleResult = await _services.Cleanup.RecycleFilesAsync(
                doneFiles,
                (current, total, _filePath) =>
                {
                    progressTracker.ReportRecycleProgress(current, total);
                },
                cancellationToken);

            if (recycleResult.WasCanceled)
            {
                _dialogService.ShowWarning(
                    "Warnung",
                    BuildCanceledDoneRecycleWarningMessage(recycleResult));
            }

            if (recycleResult.FailedFiles.Count > 0)
            {
                _dialogService.ShowWarning(
                    "Warnung",
                    "Einige Dateien aus dem Done-Ordner konnten nicht in den Papierkorb verschoben werden:\n"
                    + string.Join(Environment.NewLine, recycleResult.FailedFiles.Select(Path.GetFileName)));
            }

            DeleteEmptyBatchCleanupDirectory(doneDirectory);
            return !recycleResult.WasCanceled && !cancellationToken.IsCancellationRequested;
        }

        if (_dialogService.AskOpenDoneDirectory(doneDirectory))
        {
            _dialogService.OpenPathWithDefaultApp(doneDirectory);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    /// <summary>
    /// Der Done-Ordner ist ein internes Zwischenziel und darf verschwinden. Danach darf auch
    /// der gewählte Batch-Quellordner verschwinden, wenn er durch den Lauf leer geworden ist
    /// (typisch: ein vorher einsortierter Serien-Unterordner unterhalb der MediathekView-Downloads).
    /// Nicht leere Ordner bleiben durch <see cref="IEpisodeCleanupService.DeleteDirectoryIfEmpty(string?)"/>
    /// automatisch erhalten.
    /// </summary>
    private void DeleteEmptyBatchCleanupDirectory(string doneDirectory)
    {
        _services.Cleanup.DeleteDirectoryIfEmpty(doneDirectory);
        if (!string.IsNullOrWhiteSpace(SourceDirectory) && !IsFileSystemRoot(SourceDirectory))
        {
            _services.Cleanup.DeleteDirectoryIfEmpty(SourceDirectory);
        }
    }

    private static bool IsFileSystemRoot(string directoryPath)
    {
        try
        {
            var fullPath = Path.GetFullPath(directoryPath);
            var rootPath = Path.GetPathRoot(fullPath);
            return string.Equals(
                fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                rootPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return true;
        }
    }

    private static string BuildCanceledDoneRecycleWarningMessage(FileRecycleResult recycleResult)
    {
        if (recycleResult.PendingFiles.Count == 0)
        {
            return "Das Verschieben des Done-Ordners in den Papierkorb wurde vorzeitig abgebrochen. Bereits verschobene Dateien bleiben im Papierkorb.";
        }

        return "Das Verschieben des Done-Ordners in den Papierkorb wurde vorzeitig abgebrochen. "
            + "Bereits verschobene Dateien bleiben im Papierkorb; folgende Dateien verbleiben im Done-Ordner:\n"
            + string.Join(Environment.NewLine, recycleResult.PendingFiles.Select(Path.GetFileName));
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

            if (!string.IsNullOrWhiteSpace(logSaveResult.NewOutputMetadataReportPath))
            {
                lines.Add("Metadaten-Report:");
                lines.Add(logSaveResult.NewOutputMetadataReportPath!);
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
