using System.Threading;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

// Dieser Partial enthält Vorschau, Mux-Ausführung und anschließendes Aufräumen im Einzelmodus.
internal sealed partial class SingleEpisodeMuxViewModel
{
    private async Task CreatePreviewAsync()
    {
        var operationSource = BeginCurrentOperation();
        try
        {
            var cancellationToken = operationSource.Token;
            SetBusy(true);
            SetExecutionStatus(SingleEpisodeExecutionStatusKind.Running);
            SetStatus("Erzeuge Vorschau...", 0);
            _currentPlan = await GetOrBuildPlanAsync(cancellationToken);
            PlanRefreshProblemText = string.Empty;
            ApplyPlanPresentation(_currentPlan);
            PreviewText = _services.SeriesEpisodeMux.BuildPreviewText(_currentPlan);
            InvalidateOperationProgressCallbacks();
            SetStatus("Vorschau bereit", 0);
        }
        catch (OperationCanceledException) when (operationSource.IsCancellationRequested)
        {
            InvalidateOperationProgressCallbacks();
            SetExecutionStatus(SingleEpisodeExecutionStatusKind.Cancelled);
            SetStatus("Abgebrochen", 0);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(ex.Message);
            SetExecutionStatus(SingleEpisodeExecutionStatusKind.Error);
            SetStatus("Fehler", 0);
        }
        finally
        {
            SetBusy(false);
            CompleteCurrentOperation(operationSource);
        }
    }

    private async Task ExecuteMuxAsync()
    {
        var operationSource = BeginCurrentOperation();
        try
        {
            var cancellationToken = operationSource.Token;
            SetBusy(true);
            SetExecutionStatus(SingleEpisodeExecutionStatusKind.Running);
            _currentPlan = await GetOrBuildPlanAsync(cancellationToken);
            var outputSnapshotBeforeRun = FileStateSnapshot.TryCreate(_currentPlan.OutputFilePath);
            var outputExistedBeforeRun = outputSnapshotBeforeRun is not null;
            PlanRefreshProblemText = string.Empty;
            ApplyPlanPresentation(_currentPlan);
            PreviewText = _services.SeriesEpisodeMux.BuildPreviewText(_currentPlan)
                + Environment.NewLine
                + Environment.NewLine
                + "MKVToolNix-Ausgabe:"
                + Environment.NewLine;
            ResetPreviewOutputBuffer(PreviewText);

            if (RequiresManualCheck && !IsManualCheckApproved)
            {
                _dialogService.ShowWarning("Hinweis", "Diese Episode nutzt eine prüfpflichtige Quelle. Bitte zuerst 'Quelle prüfen' ausführen und die Quelle freigeben.");
                SetExecutionStatus(SingleEpisodeExecutionStatusKind.Warning);
                SetStatus("Freigabe der Quelle fehlt", 0);
                return;
            }

            if (RequiresMetadataReview && !IsMetadataReviewApproved)
            {
                _dialogService.ShowWarning("Hinweis", "Die TVDB-Zuordnung ist noch nicht freigegeben. Bitte zuerst 'TVDB prüfen' ausführen oder die Metadaten manuell korrigieren.");
                SetExecutionStatus(SingleEpisodeExecutionStatusKind.Warning);
                SetStatus("Freigabe der TVDB-Metadaten fehlt", 0);
                return;
            }

            if (HasPendingPlanReview)
            {
                _dialogService.ShowWarning(
                    "Hinweis",
                    "Vor dem Muxen ist noch ein offener Archiv-/Mehrfachfolgenhinweis zu prüfen. Bitte bestätige den Hinweis zuerst explizit.");
                SetExecutionStatus(SingleEpisodeExecutionStatusKind.Warning);
                SetStatus("Hinweisprüfung fehlt", 0);
                return;
            }

            if (_currentPlan.SkipMux)
            {
                SetExecutionStatus(SingleEpisodeExecutionStatusKind.UpToDate);
                SetStatus("Zieldatei bereits aktuell", 100);
                _dialogService.ShowInfo("Hinweis", _currentPlan.SkipReason ?? "Die Zieldatei ist bereits vollständig.");
                await OfferSingleEpisodeCleanupAsync(_currentPlan, cancellationToken);
                return;
            }

            if (!_dialogService.ConfirmMuxStart())
            {
                SetExecutionStatus(SingleEpisodeExecutionStatusKind.Cancelled);
                SetStatus("Abgebrochen", 0);
                return;
            }

            if (!await PrepareWorkingCopyAsync(_currentPlan, cancellationToken))
            {
                SetExecutionStatus(SingleEpisodeExecutionStatusKind.Cancelled);
                return;
            }

            SetExecutionStatus(SingleEpisodeExecutionStatusKind.Running);
            SetStatus(
                _currentPlan.HasHeaderEdits
                    ? "Header-Metadaten werden aktualisiert..."
                    : "Muxing läuft...",
                0);
            var result = await _services.MuxWorkflow.ExecuteMuxAsync(
                _currentPlan,
                HandleMuxOutput,
                HandleMuxUpdate,
                cancellationToken);

            var outcomeKind = MuxExecutionResultClassifier.Classify(
                result,
                outputSnapshotBeforeRun,
                _currentPlan.OutputFilePath);

            if (outcomeKind == MuxExecutionOutcomeKind.Success)
            {
                var finalStatusText = _currentPlan.HasHeaderEdits
                    ? "Header-Metadaten erfolgreich aktualisiert"
                    : "Muxing erfolgreich abgeschlossen";
                var completedPlan = _currentPlan ?? throw new InvalidOperationException("Der abgeschlossene Mux-Plan fehlt.");
                var cleanupCandidate = BuildSingleEpisodeCleanupCandidate(completedPlan);
                SetExecutionStatus(SingleEpisodeExecutionStatusKind.Success);
                InvalidateOperationProgressCallbacks();
                SetStatus(finalStatusText, 100);
                var logSaveResult = PersistSingleEpisodeArtifactsIfNeeded(completedPlan, outputExistedBeforeRun, hasWarning: false);
                ClearCompletedSingleEpisodeInput(finalStatusText, SingleEpisodeExecutionStatusKind.Success);
                _dialogService.ShowInfo(
                    "Erfolg",
                    completedPlan.HasHeaderEdits
                        ? $"Die relevanten Header-Metadaten wurden direkt aktualisiert:\n{completedPlan.OutputFilePath}"
                        : $"MKV erfolgreich erstellt:\n{completedPlan.OutputFilePath}");
                OpenSingleEpisodeRunArtifactIfAvailable(logSaveResult);
                await OfferSingleEpisodeCleanupCandidateAsync(cleanupCandidate, cancellationToken);
                SetExecutionStatus(SingleEpisodeExecutionStatusKind.Success);
                InvalidateOperationProgressCallbacks();
                SetStatus(finalStatusText, 100);
            }
            else if (outcomeKind == MuxExecutionOutcomeKind.Warning)
            {
                var finalStatusText = _currentPlan.HasHeaderEdits
                    ? "Header-Metadaten mit Warnungen aktualisiert"
                    : "Muxing mit Warnungen abgeschlossen";
                var completedPlan = _currentPlan ?? throw new InvalidOperationException("Der abgeschlossene Mux-Plan fehlt.");
                var cleanupCandidate = BuildSingleEpisodeCleanupCandidate(completedPlan);
                SetExecutionStatus(SingleEpisodeExecutionStatusKind.Warning);
                InvalidateOperationProgressCallbacks();
                SetStatus(finalStatusText, 100);
                var logSaveResult = PersistSingleEpisodeArtifactsIfNeeded(completedPlan, outputExistedBeforeRun, hasWarning: true);
                ClearCompletedSingleEpisodeInput(finalStatusText, SingleEpisodeExecutionStatusKind.Warning);
                _dialogService.ShowWarning(
                    "Warnung",
                    completedPlan.HasHeaderEdits
                        ? $"Die Header-Metadaten wurden aktualisiert, aber {completedPlan.ExecutionToolDisplayName} hat Warnungen gemeldet.\n\n{completedPlan.OutputFilePath}"
                        : $"Die MKV wurde erstellt, aber {completedPlan.ExecutionToolDisplayName} hat Warnungen gemeldet.\n\n{completedPlan.OutputFilePath}");
                OpenSingleEpisodeRunArtifactIfAvailable(logSaveResult);
                await OfferSingleEpisodeCleanupCandidateAsync(cleanupCandidate, cancellationToken);
                SetExecutionStatus(SingleEpisodeExecutionStatusKind.Warning);
                InvalidateOperationProgressCallbacks();
                SetStatus(finalStatusText, 100);
            }
            else
            {
                InvalidateOperationProgressCallbacks();
                SetExecutionStatus(SingleEpisodeExecutionStatusKind.Error);
                SetStatus(
                    _currentPlan.HasHeaderEdits
                        ? $"Header-Aktualisierung fehlgeschlagen (Exit-Code {result.ExitCode})"
                        : $"Muxing fehlgeschlagen (Exit-Code {result.ExitCode})",
                    0);
                _dialogService.ShowWarning("Hinweis", $"{_currentPlan.ExecutionToolDisplayName} wurde mit Exit-Code {result.ExitCode} beendet.");
            }
        }
        catch (OperationCanceledException) when (operationSource.IsCancellationRequested)
        {
            InvalidateOperationProgressCallbacks();
            SetExecutionStatus(SingleEpisodeExecutionStatusKind.Cancelled);
            SetStatus("Abgebrochen", 0);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(ex.Message);
            SetExecutionStatus(SingleEpisodeExecutionStatusKind.Error);
            SetStatus("Fehler", 0);
        }
        finally
        {
            SetBusy(false);
            CompleteCurrentOperation(operationSource);
        }
    }

    private BatchRunLogSaveResult? PersistSingleEpisodeArtifactsIfNeeded(
        SeriesEpisodeMuxPlan plan,
        bool outputExistedBeforeRun,
        bool hasWarning)
    {
        if (outputExistedBeforeRun || !File.Exists(plan.OutputFilePath))
        {
            return null;
        }

        try
        {
            return _services.BatchLogs.SaveBatchRunArtifacts(
                Path.GetDirectoryName(DetectionSeedPath ?? MainVideoPath ?? plan.OutputFilePath) ?? string.Empty,
                Path.GetDirectoryName(plan.OutputFilePath) ?? string.Empty,
                _previewOutputBuffer.GetTextSnapshot(),
                [plan.OutputFilePath],
                successCount: hasWarning ? 0 : 1,
                warningCount: hasWarning ? 1 : 0,
                errorCount: 0,
                newOutputMetadata: [CreateSingleEpisodeOutputMetadata(plan.OutputFilePath)],
                runLabel: "Einzel");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _dialogService.ShowWarning(
                "Warnung",
                "Der Mux-Lauf war erfolgreich, aber Log- oder Metadatenartefakte konnten nicht gespeichert werden:\n"
                + ex.Message);
            return null;
        }
    }

    private void OpenSingleEpisodeRunArtifactIfAvailable(BatchRunLogSaveResult? logSaveResult)
    {
        if (!string.IsNullOrWhiteSpace(logSaveResult?.PreferredOpenPath))
        {
            _dialogService.OpenPathWithDefaultApp(logSaveResult.PreferredOpenPath);
        }
    }

    private BatchOutputMetadataEntry CreateSingleEpisodeOutputMetadata(string outputPath)
    {
        return BatchOutputMetadataEntryFactory.Create(
            outputPath,
            SeriesName,
            SeasonNumber,
            EpisodeNumber,
            Title,
            TvdbEpisodeId,
            TvdbSeriesId,
            TvdbSeriesName);
    }

    private async Task<SeriesEpisodeMuxPlan> GetOrBuildPlanAsync(
        CancellationToken cancellationToken = default,
        bool assignCurrentPlan = true)
    {
        var snapshot = EpisodePlanInputSnapshot.Create(this);
        var cachedPlan = await _planCache.TryGetAsync(this, snapshot, cancellationToken);
        if (cachedPlan is not null)
        {
            if (assignCurrentPlan)
            {
                _currentPlan = cachedPlan;
            }

            return cachedPlan;
        }

        var plan = await BuildFreshPlanAsync(snapshot, cancellationToken);
        await _planCache.StoreAsync(this, snapshot, plan, cancellationToken);
        if (assignCurrentPlan)
        {
            _currentPlan = plan;
        }

        return plan;
    }

    private Task<SeriesEpisodeMuxPlan> BuildFreshPlanAsync(CancellationToken cancellationToken = default)
    {
        return BuildFreshPlanAsync(EpisodePlanInputSnapshot.Create(this), cancellationToken);
    }

    private async Task<SeriesEpisodeMuxPlan> BuildFreshPlanAsync(
        IEpisodePlanInput input,
        CancellationToken cancellationToken = default)
    {
        if (input.HasPrimaryVideoSource)
        {
            RequireValue(input.MainVideoPath, "Bitte ein Hauptvideo auswählen.");
        }
        else if (!HasSupplementOnlySource(input))
        {
            throw new InvalidOperationException("Bitte eine AD-Datei oder Untertitel auswählen.");
        }

        RequireValue(input.OutputPath, "Bitte eine Ausgabedatei wählen.");
        RequireValue(input.TitleForMux.Trim(), "Bitte einen Dateititel eingeben.");
        return await _services.EpisodePlans.BuildPlanAsync(input, cancellationToken);
    }

    private async Task<bool> RefreshPlanSummaryImmediatelyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var plan = await GetOrBuildPlanAsync(cancellationToken, assignCurrentPlan: false);
            PlanRefreshProblemText = string.Empty;
            ApplyPlanPresentation(plan);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ClearPlanPresentation();
            PlanRefreshProblemText = "Plan konnte gerade nicht aktualisiert werden: " + ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Erkennt Zusatzmaterial-only-Fälle des Einzelmodus, in denen bewusst keine frische
    /// Hauptvideoquelle vorliegt, aber dennoch ein valider Plan gebaut werden kann.
    /// </summary>
    /// <returns>
    /// <see langword="true"/>, wenn mindestens eine AD- oder Untertitelquelle vorhanden ist;
    /// andernfalls <see langword="false"/>.
    /// </returns>
    private bool HasSupplementOnlySource(IEpisodePlanInput input)
    {
        return !string.IsNullOrWhiteSpace(input.AudioDescriptionPath) || input.SubtitlePaths.Count > 0;
    }

    private void ApplyPlanPresentation(SeriesEpisodeMuxPlan plan)
    {
        _currentPlan = plan;
        RefreshOutputTargetStatusFromPlan(plan);
        SetPlanNotes(plan.Notes);
        PlanSummaryText = plan.BuildCompactSummaryText();
        UsageSummary = plan.BuildUsageSummary();
        SetExecutionStatus(plan.SkipMux
            ? SingleEpisodeExecutionStatusKind.UpToDate
            : SingleEpisodeExecutionStatusKind.Ready);
    }

    /// <summary>
    /// Räumt alle planabgeleiteten UI-Zustände, sobald kein belastbarer Plan mehr vorliegt.
    /// </summary>
    /// <remarks>
    /// Fehler beim Hintergrund-Refresh dürfen keine alte Vergleichsansicht oder veraltete
    /// Archivhinweise sichtbar lassen. Stattdessen fällt die Anzeige auf den rein aus den
    /// aktuellen Eingaben berechenbaren Zielstatus zurück.
    /// </remarks>
    private void ClearPlanPresentation()
    {
        _currentPlan = null;
        PlanSummaryText = string.Empty;
        SetPlanNotes([]);
        UsageSummary = null;
        RefreshOutputTargetStatus();
    }

    /// <summary>
    /// Setzt den Einzelmodus nach einem abgeschlossenen Werkzeuglauf auf eine neue leere Eingabe zurück,
    /// lässt aber das Ergebnis des gerade beendeten Laufs in Statuszeile und Badge sichtbar.
    /// </summary>
    private void ClearCompletedSingleEpisodeInput(
        string finalStatusText,
        SingleEpisodeExecutionStatusKind finalStatusKind)
    {
        ApplySharedState(() =>
        {
            SetRequestedMainVideoPath(string.Empty);
            SetDetectionSeedPath(string.Empty);
            SetRequestedSourcePaths([]);
            SetLocalMetadataGuess(new EpisodeMetadataGuess(string.Empty, "xx", "xx", string.Empty));
            HasPrimaryVideoSource = true;
            MainVideoPath = string.Empty;
            SeriesName = string.Empty;
            SeasonNumber = "xx";
            EpisodeNumber = "xx";
            Title = string.Empty;
            SetAdditionalVideoPaths([]);
            AudioDescriptionPath = string.Empty;
            SetSubtitles([]);
            SetAttachments([]);
            SetRelatedEpisodeFilePaths([]);
            SetMetadataOriginalLanguage(null);
            ClearTvdbSelection();
            MetadataStatusText = string.Empty;
            RequiresMetadataReview = false;
            IsMetadataReviewApproved = true;
            SetManualCheckFiles(requiresManualCheck: false, []);
            ReplaceExcludedSourcePaths([]);
            MarkOutputPathAsAutomatic();
            OutputPath = string.Empty;
            SetVideoLanguageOverride(null);
            SetAudioLanguageOverride(null);
            SetOriginalLanguageOverride(null);
            SetNotes([]);
            SetPlanNotes([]);
        });

        _lastSuggestedTitle = string.Empty;
        _planCache.Invalidate(this);
        _currentPlan = null;
        PlanRefreshProblemText = string.Empty;
        OutputTargetStatusText = string.Empty;
        PlanSummaryText = string.Empty;
        UsageSummary = null;
        PreviewText = string.Empty;
        ResetPreviewOutputBuffer(string.Empty);
        SetExecutionStatus(finalStatusKind);
        SetStatus(finalStatusText, 100);
        RefreshCommands();
    }

    private void RefreshOutputTargetStatusFromPlan(SeriesEpisodeMuxPlan plan)
    {
        if (plan.SkipMux)
        {
            OutputTargetStatusText = plan.SkipReason ?? "Die Zieldatei ist bereits vollständig.";
            return;
        }

        if (plan.HasHeaderEdits)
        {
            OutputTargetStatusText = plan.ContainerTitleEdit is null
                ? "Am Ziel liegt bereits eine passende MKV. Es werden nur die relevanten Spurnamen direkt im Matroska-Header vereinheitlicht; eine Arbeitskopie und ein kompletter Remux sind nicht nötig."
                : "Am Ziel liegt bereits eine passende MKV. Es werden nur Header-Metadaten wie MKV-Titel und relevante Spurnamen direkt im Matroska-Header vereinheitlicht; eine Arbeitskopie und ein kompletter Remux sind nicht nötig.";
            return;
        }

        if (plan.WorkingCopy is not null)
        {
            OutputTargetStatusText = plan.WorkingCopy.IsReusable
                ? "Am Ziel liegt bereits eine MKV. Eine aktuelle Arbeitskopie ist schon vorhanden und wird direkt weiterverwendet."
                : "Am Ziel liegt bereits eine MKV. Vor dem Mux wird eine lokale Arbeitskopie erstellt und die fehlenden oder besseren Spuren werden eingearbeitet.";
            return;
        }

        RefreshOutputTargetStatus();
    }

    private async Task<bool> PrepareWorkingCopyAsync(
        SeriesEpisodeMuxPlan plan,
        CancellationToken cancellationToken)
    {
        if (plan.WorkingCopy is null)
        {
            return true;
        }

        if (_services.MuxWorkflow.NeedsWorkingCopyPreparation(plan)
            && !_dialogService.ConfirmArchiveCopy(plan.WorkingCopy))
        {
            SetStatus("Abgebrochen", 0);
            return false;
        }

        await _services.MuxWorkflow.PrepareWorkingCopyAsync(plan, HandleWorkingCopyPreparationUpdate, cancellationToken);
        return true;
    }

    private void HandleWorkingCopyPreparationUpdate(WorkingCopyPreparationUpdate update)
    {
        DispatchCurrentOperationProgress(() =>
        {
            if (update.ReusesExistingCopy)
            {
                SetStatus("Arbeitskopie bereits aktuell - übernehme vorhandene Kopie...", 100);
                return;
            }

            SetStatus($"Kopiere Zieldatei... {update.ProgressPercent}%", update.ProgressPercent);
        });
    }

    private void HandleMuxOutput(string line)
    {
        _previewOutputBuffer.AppendLine(line);
    }

    private void HandleMuxUpdate(MuxExecutionUpdate update)
    {
        DispatchCurrentOperationProgress(() =>
        {
            var progressValue = update.ProgressPercent ?? ProgressValue;
            var baseText = _currentPlan?.HasHeaderEdits == true
                ? "Header-Metadaten werden aktualisiert..."
                : "Muxing läuft...";
            var statusText = update.ProgressPercent is int progressPercent
                ? $"{baseText} {progressPercent}%"
                : baseText;

            if (update.HasWarning)
            {
                statusText += " - Warnung erkannt";
            }

            SetStatus(statusText, progressValue);
        });
    }

    private static string RequireValue(string? value, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return value;
    }

    private async Task OfferSingleEpisodeCleanupAsync(
        SeriesEpisodeMuxPlan plan,
        CancellationToken cancellationToken)
    {
        await OfferSingleEpisodeCleanupCandidateAsync(BuildSingleEpisodeCleanupCandidate(plan), cancellationToken);
    }

    private SingleEpisodeCleanupCandidate BuildSingleEpisodeCleanupCandidate(SeriesEpisodeMuxPlan plan)
    {
        var usedFiles = BuildCleanupFileList(plan.GetReferencedInputFiles(), plan);
        var relatedFiles = BuildCleanupFileList(RelatedEpisodeFilePaths, plan);
        var unusedFiles = relatedFiles
            .Where(path => !usedFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return new SingleEpisodeCleanupCandidate(usedFiles, unusedFiles);
    }

    private async Task OfferSingleEpisodeCleanupCandidateAsync(
        SingleEpisodeCleanupCandidate cleanupCandidate,
        CancellationToken cancellationToken)
    {
        if (cleanupCandidate.UsedFiles.Count == 0 && cleanupCandidate.UnusedFiles.Count == 0)
        {
            return;
        }

        if (!_dialogService.ConfirmSingleEpisodeCleanup(cleanupCandidate.UsedFiles, cleanupCandidate.UnusedFiles))
        {
            return;
        }

        var cleanupFiles = cleanupCandidate.UsedFiles
            .Concat(cleanupCandidate.UnusedFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        SetStatus("Verschiebe Quelldateien in den Papierkorb...", 0);
        var recycleResult = await _services.Cleanup.RecycleFilesAsync(
            cleanupFiles,
            (current, total, _) =>
            {
                var progress = total <= 0 ? 0 : (int)Math.Round(current * 100d / total);
                DispatchCurrentOperationProgress(() =>
                    SetStatus($"Verschiebe Quelldateien in den Papierkorb... {current}/{total}", progress));
            },
            cancellationToken);

        if (recycleResult.WasCanceled)
        {
            _dialogService.ShowWarning(
                "Warnung",
                BuildCanceledSourceRecycleWarningMessage(recycleResult));
            return;
        }

        if (recycleResult.FailedFiles.Count > 0)
        {
            _dialogService.ShowWarning(
                "Warnung",
                "Einige Quelldateien konnten nicht in den Papierkorb verschoben werden:\n"
                + string.Join(Environment.NewLine, recycleResult.FailedFiles.Select(Path.GetFileName)));
            return;
        }

        DeleteEmptySingleEpisodeSourceDirectories(cleanupFiles);
    }

    private static string BuildCanceledSourceRecycleWarningMessage(FileRecycleResult recycleResult)
    {
        if (recycleResult.PendingFiles.Count == 0)
        {
            return "Das Verschieben der Quelldateien in den Papierkorb wurde vorzeitig abgebrochen. Bereits verschobene Dateien bleiben im Papierkorb.";
        }

        return "Das Verschieben der Quelldateien in den Papierkorb wurde vorzeitig abgebrochen. "
            + "Bereits verschobene Dateien bleiben im Papierkorb; folgende Dateien verbleiben am Quellort:\n"
            + string.Join(Environment.NewLine, recycleResult.PendingFiles.Select(Path.GetFileName));
    }

    private List<string> BuildCleanupFileList(IEnumerable<string> sourceFilePaths, SeriesEpisodeMuxPlan plan)
    {
        return _services.CleanupFiles.BuildCleanupFileList(
            sourceFilePaths,
            plan.OutputFilePath,
            plan.WorkingCopy?.DestinationFilePath,
            excludedSourcePaths: ExcludedSourcePaths);
    }

    private void DeleteEmptySingleEpisodeSourceDirectories(IEnumerable<string> cleanupFiles)
    {
        var sourceDirectories = cleanupFiles
            .Select(Path.GetDirectoryName)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => path.Length)
            .ToList();

        foreach (var sourceDirectory in sourceDirectories)
        {
            _services.Cleanup.DeleteDirectoryIfEmpty(sourceDirectory);
        }
    }

    private sealed record SingleEpisodeCleanupCandidate(
        IReadOnlyList<string> UsedFiles,
        IReadOnlyList<string> UnusedFiles);

    private static string GetPreferredVideoDirectory()
    {
        return PreferredDownloadDirectoryHelper.GetPreferredMediathekDownloadsDirectory();
    }

    private void SchedulePlanSummaryRefresh()
    {
        _planSummaryRefresh.Schedule(RefreshPlanSummaryAsync);
    }

    private async Task RefreshPlanSummaryAsync(int version, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(MainVideoPath)
            || string.IsNullOrWhiteSpace(OutputPath)
            || string.IsNullOrWhiteSpace(Title))
        {
            ClearPlanPresentation();
            PlanRefreshProblemText = string.Empty;
            if (ExecutionStatusKind is SingleEpisodeExecutionStatusKind.Running or SingleEpisodeExecutionStatusKind.ComparisonPending)
            {
                SetExecutionStatus(SingleEpisodeExecutionStatusKind.Ready);
            }

            return;
        }

        try
        {
            var plan = await GetOrBuildPlanAsync(cancellationToken, assignCurrentPlan: false);
            if (!_planSummaryRefresh.IsCurrent(version) || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            PlanRefreshProblemText = string.Empty;
            ApplyPlanPresentation(plan);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_planSummaryRefresh.IsCurrent(version) || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            ClearPlanPresentation();
            PlanRefreshProblemText = "Plan konnte gerade nicht aktualisiert werden: " + ex.Message;
            SetExecutionStatus(SingleEpisodeExecutionStatusKind.ComparisonPending);
        }
    }

}
