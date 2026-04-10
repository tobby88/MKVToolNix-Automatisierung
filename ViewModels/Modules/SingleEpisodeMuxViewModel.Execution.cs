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
            RefreshOutputTargetStatusFromPlan(_currentPlan);
            SetPlanNotes(_currentPlan.Notes);
            PlanSummaryText = _currentPlan.BuildCompactSummaryText();
            UsageSummary = _currentPlan.BuildUsageSummary();
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
            PlanRefreshProblemText = string.Empty;
            RefreshOutputTargetStatusFromPlan(_currentPlan);
            SetPlanNotes(_currentPlan.Notes);
            PlanSummaryText = _currentPlan.BuildCompactSummaryText();
            UsageSummary = _currentPlan.BuildUsageSummary();
            PreviewText = _services.SeriesEpisodeMux.BuildPreviewText(_currentPlan)
                + Environment.NewLine
                + Environment.NewLine
                + "MKVToolNix-Ausgabe:"
                + Environment.NewLine;
            ResetPreviewOutputBuffer(PreviewText);

            if (_currentPlan.SkipMux)
            {
                SetStatus("Zieldatei bereits aktuell", 100);
                _dialogService.ShowInfo("Hinweis", _currentPlan.SkipReason ?? "Die Zieldatei ist bereits vollständig.");
                await OfferSingleEpisodeCleanupAsync(_currentPlan, cancellationToken);
                return;
            }

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
                _currentPlan.HasTrackHeaderEdits
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
                    _currentPlan.HasTrackHeaderEdits
                        ? "Header-Metadaten erfolgreich aktualisiert"
                        : "Muxing erfolgreich abgeschlossen",
                    100);
                _dialogService.ShowInfo(
                    "Erfolg",
                    _currentPlan.HasTrackHeaderEdits
                        ? $"Die relevanten Header-Metadaten wurden direkt aktualisiert:\n{_currentPlan.OutputFilePath}"
                        : $"MKV erfolgreich erstellt:\n{_currentPlan.OutputFilePath}");
                await OfferSingleEpisodeCleanupAsync(_currentPlan, cancellationToken);
            }
            else if ((result.ExitCode == 0 && result.HasWarning)
                || (result.ExitCode == 1 && File.Exists(_currentPlan.OutputFilePath)))
            {
                SetStatus(
                    _currentPlan.HasTrackHeaderEdits
                        ? "Header-Metadaten mit Warnungen aktualisiert"
                        : "Muxing mit Warnungen abgeschlossen",
                    100);
                _dialogService.ShowWarning(
                    "Warnung",
                    _currentPlan.HasTrackHeaderEdits
                        ? $"Die Header-Metadaten wurden aktualisiert, aber {_currentPlan.ExecutionToolDisplayName} hat Warnungen gemeldet.\n\n{_currentPlan.OutputFilePath}"
                        : $"Die MKV wurde erstellt, aber {_currentPlan.ExecutionToolDisplayName} hat Warnungen gemeldet.\n\n{_currentPlan.OutputFilePath}");
                await OfferSingleEpisodeCleanupAsync(_currentPlan, cancellationToken);
            }
            else
            {
                SetStatus(
                    _currentPlan.HasTrackHeaderEdits
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

    private async Task<SeriesEpisodeMuxPlan> GetOrBuildPlanAsync(CancellationToken cancellationToken = default)
    {
        var cachedPlan = await _planCache.TryGetAsync(this, this, cancellationToken);
        if (cachedPlan is not null)
        {
            return _currentPlan = cachedPlan;
        }

        return _currentPlan = await BuildFreshPlanAsync(cancellationToken);
    }

    private async Task<SeriesEpisodeMuxPlan> BuildFreshPlanAsync(CancellationToken cancellationToken = default)
    {
        if (HasPrimaryVideoSource)
        {
            RequireValue(MainVideoPath, "Bitte ein Hauptvideo auswählen.");
        }
        else
        {
            RequireValue(AudioDescriptionPath, "Bitte eine AD-Datei auswählen.");
        }

        RequireValue(OutputPath, "Bitte eine Ausgabedatei wählen.");
        RequireValue(Title.Trim(), "Bitte einen Dateititel eingeben.");
        var plan = await _services.EpisodePlans.BuildPlanAsync(this, cancellationToken);
        await _planCache.StoreAsync(this, this, plan, cancellationToken);
        return plan;
    }

    private async Task<bool> RefreshPlanSummaryImmediatelyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var plan = await GetOrBuildPlanAsync(cancellationToken);
            _currentPlan = plan;
            PlanRefreshProblemText = string.Empty;
            RefreshOutputTargetStatusFromPlan(plan);
            SetPlanNotes(plan.Notes);
            PlanSummaryText = plan.BuildCompactSummaryText();
            UsageSummary = plan.BuildUsageSummary();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            PlanRefreshProblemText = "Plan konnte gerade nicht aktualisiert werden: " + ex.Message;
            return false;
        }
    }

    private void RefreshOutputTargetStatusFromPlan(SeriesEpisodeMuxPlan plan)
    {
        if (plan.SkipMux)
        {
            OutputTargetStatusText = plan.SkipReason ?? "Die Zieldatei ist bereits vollständig.";
            return;
        }

        if (plan.HasTrackHeaderEdits)
        {
            OutputTargetStatusText = "Am Ziel liegt bereits eine passende MKV. Es werden nur die relevanten Spurnamen direkt im Matroska-Header vereinheitlicht; eine Arbeitskopie und ein kompletter Remux sind nicht nötig.";
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
            var baseText = _currentPlan?.HasTrackHeaderEdits == true
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

        if (recycleResult.FailedFiles.Count > 0)
        {
            _dialogService.ShowWarning(
                "Warnung",
                "Einige Quelldateien konnten nicht in den Papierkorb verschoben werden:\n"
                + string.Join(Environment.NewLine, recycleResult.FailedFiles.Select(Path.GetFileName)));
        }
    }

    private List<string> BuildCleanupFileList(IEnumerable<string> sourceFilePaths, SeriesEpisodeMuxPlan plan)
    {
        return _services.CleanupFiles.BuildCleanupFileList(
            sourceFilePaths,
            plan.OutputFilePath,
            plan.WorkingCopy?.DestinationFilePath);
    }

    private static string GetPreferredVideoDirectory()
    {
        var downloadsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        var preferredDirectory = PreferredDownloadsSubPath.Aggregate(downloadsDirectory, Path.Combine);

        return Directory.Exists(preferredDirectory)
            ? preferredDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
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
            PlanSummaryText = string.Empty;
            SetPlanNotes([]);
            UsageSummary = null;
            PlanRefreshProblemText = string.Empty;
            return;
        }

        try
        {
            var updated = await RefreshPlanSummaryImmediatelyAsync(cancellationToken);
            if (!_planSummaryRefresh.IsCurrent(version) || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!updated)
            {
                return;
            }
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

            PlanRefreshProblemText = "Plan konnte gerade nicht aktualisiert werden: " + ex.Message;
        }
    }

}
