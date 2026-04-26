using System.Globalization;
using System.Threading;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.Windows;

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
            SetStatus("Erzeuge Vorschau...", 0);
            _currentPlan = await GetOrBuildPlanAsync(cancellationToken);
            PlanRefreshProblemText = string.Empty;
            ApplyPlanPresentation(_currentPlan);
            PreviewText = _services.SeriesEpisodeMux.BuildPreviewText(_currentPlan);
            SetStatus("Vorschau bereit", 0);
        }
        catch (OperationCanceledException) when (operationSource.IsCancellationRequested)
        {
            SetStatus("Abgebrochen", 0);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(ex.Message);
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
            _currentPlan = await GetOrBuildPlanAsync(cancellationToken);
            var outputExistedBeforeRun = File.Exists(_currentPlan.OutputFilePath);
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
                SetStatus("Freigabe der Quelle fehlt", 0);
                return;
            }

            if (RequiresMetadataReview && !IsMetadataReviewApproved)
            {
                _dialogService.ShowWarning("Hinweis", "Die TVDB-Zuordnung ist noch nicht freigegeben. Bitte zuerst 'TVDB prüfen' ausführen oder die Metadaten manuell korrigieren.");
                SetStatus("Freigabe der TVDB-Metadaten fehlt", 0);
                return;
            }

            if (HasPendingPlanReview)
            {
                _dialogService.ShowWarning(
                    "Hinweis",
                    "Vor dem Muxen ist noch ein offener Archiv-/Mehrfachfolgenhinweis zu prüfen. Bitte bestätige den Hinweis zuerst explizit.");
                SetStatus("Hinweisprüfung fehlt", 0);
                return;
            }

            if (_currentPlan.SkipMux)
            {
                SetStatus("Zieldatei bereits aktuell", 100);
                _dialogService.ShowInfo("Hinweis", _currentPlan.SkipReason ?? "Die Zieldatei ist bereits vollständig.");
                await OfferSingleEpisodeCleanupAsync(_currentPlan, cancellationToken);
                return;
            }

            if (!_dialogService.ConfirmMuxStart())
            {
                SetStatus("Abgebrochen", 0);
                return;
            }

            if (!await PrepareWorkingCopyAsync(_currentPlan, cancellationToken))
            {
                return;
            }

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

            if (result.ExitCode == 0 && !result.HasWarning)
            {
                SetStatus(
                    _currentPlan.HasHeaderEdits
                        ? "Header-Metadaten erfolgreich aktualisiert"
                        : "Muxing erfolgreich abgeschlossen",
                    100);
                _dialogService.ShowInfo(
                    "Erfolg",
                    _currentPlan.HasHeaderEdits
                        ? $"Die relevanten Header-Metadaten wurden direkt aktualisiert:\n{_currentPlan.OutputFilePath}"
                        : $"MKV erfolgreich erstellt:\n{_currentPlan.OutputFilePath}");
                PersistSingleEpisodeArtifactsIfNeeded(_currentPlan, outputExistedBeforeRun, hasWarning: false);
                await OfferSingleEpisodeCleanupAsync(_currentPlan, cancellationToken);
            }
            else if ((result.ExitCode == 0 && result.HasWarning)
                || (result.ExitCode == 1 && File.Exists(_currentPlan.OutputFilePath)))
            {
                SetStatus(
                    _currentPlan.HasHeaderEdits
                        ? "Header-Metadaten mit Warnungen aktualisiert"
                        : "Muxing mit Warnungen abgeschlossen",
                    100);
                _dialogService.ShowWarning(
                    "Warnung",
                    _currentPlan.HasHeaderEdits
                        ? $"Die Header-Metadaten wurden aktualisiert, aber {_currentPlan.ExecutionToolDisplayName} hat Warnungen gemeldet.\n\n{_currentPlan.OutputFilePath}"
                        : $"Die MKV wurde erstellt, aber {_currentPlan.ExecutionToolDisplayName} hat Warnungen gemeldet.\n\n{_currentPlan.OutputFilePath}");
                PersistSingleEpisodeArtifactsIfNeeded(_currentPlan, outputExistedBeforeRun, hasWarning: true);
                await OfferSingleEpisodeCleanupAsync(_currentPlan, cancellationToken);
            }
            else
            {
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
            SetStatus("Abgebrochen", 0);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(ex.Message);
            SetStatus("Fehler", 0);
        }
        finally
        {
            SetBusy(false);
            CompleteCurrentOperation(operationSource);
        }
    }

    private void PersistSingleEpisodeArtifactsIfNeeded(
        SeriesEpisodeMuxPlan plan,
        bool outputExistedBeforeRun,
        bool hasWarning)
    {
        if (outputExistedBeforeRun || !File.Exists(plan.OutputFilePath))
        {
            return;
        }

        try
        {
            _ = _services.BatchLogs.SaveBatchRunArtifacts(
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
        }
    }

    private BatchOutputMetadataEntry CreateSingleEpisodeOutputMetadata(string outputPath)
    {
        var tvdbEpisodeId = TvdbEpisodeId?.ToString(CultureInfo.InvariantCulture);
        return new BatchOutputMetadataEntry
        {
            OutputPath = outputPath,
            NfoPath = Path.ChangeExtension(outputPath, ".nfo"),
            SeriesName = SeriesName,
            SeasonNumber = SeasonNumber,
            EpisodeNumber = EpisodeNumber,
            EpisodeTitle = Title,
            TvdbEpisodeId = tvdbEpisodeId,
            ProviderIds = string.IsNullOrWhiteSpace(tvdbEpisodeId)
                ? null
                : new BatchOutputProviderIds
                {
                    Tvdb = tvdbEpisodeId
                },
            Tvdb = TvdbEpisodeId is null && TvdbSeriesId is null && string.IsNullOrWhiteSpace(TvdbSeriesName)
                ? null
                : new BatchOutputTvdbMetadata
                {
                    SeriesId = TvdbSeriesId,
                    SeriesName = TvdbSeriesName,
                    EpisodeId = TvdbEpisodeId
                }
        };
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
        _ = Application.Current.Dispatcher.BeginInvoke(() =>
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
        _ = Application.Current.Dispatcher.BeginInvoke(() =>
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
        var usedFiles = BuildCleanupFileList(plan.GetReferencedInputFiles(), plan);
        var relatedFiles = BuildCleanupFileList(RelatedEpisodeFilePaths, plan);
        var unusedFiles = relatedFiles
            .Where(path => !usedFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (usedFiles.Count == 0 && unusedFiles.Count == 0)
        {
            return;
        }

        if (!_dialogService.ConfirmSingleEpisodeCleanup(usedFiles, unusedFiles))
        {
            return;
        }

        var cleanupFiles = usedFiles
            .Concat(unusedFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        SetStatus("Verschiebe Quelldateien in den Papierkorb...", 0);
        var recycleResult = await _services.Cleanup.RecycleFilesAsync(
            cleanupFiles,
            (current, total, _) =>
            {
                var progress = total <= 0 ? 0 : (int)Math.Round(current * 100d / total);
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    SetStatus($"Verschiebe Quelldateien in den Papierkorb... {current}/{total}", progress);
                });
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
        }
    }

}
