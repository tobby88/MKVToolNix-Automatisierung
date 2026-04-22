using System.Threading;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.Windows;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

// Dieser Partial enthält Dateiauswahl, automatische Erkennung, manuelle Korrekturen und TVDB-Interaktion.
internal sealed partial class SingleEpisodeMuxViewModel
{
    private string ResolveMainVideoInitialDirectory()
    {
        if (!string.IsNullOrWhiteSpace(MainVideoPath))
        {
            var existingDirectory = Path.GetDirectoryName(MainVideoPath);
            if (!string.IsNullOrWhiteSpace(existingDirectory) && Directory.Exists(existingDirectory))
            {
                return existingDirectory;
            }
        }

        return GetPreferredVideoDirectory();
    }

    private string ResolveComponentInitialDirectory()
    {
        if (!string.IsNullOrWhiteSpace(MainVideoPath))
        {
            var directory = Path.GetDirectoryName(MainVideoPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                return directory;
            }
        }

        return ResolveMainVideoInitialDirectory();
    }

    private string ResolveOutputDirectory()
    {
        if (!string.IsNullOrWhiteSpace(OutputPath))
        {
            var outputDirectory = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                return outputDirectory;
            }
        }

        if (!string.IsNullOrWhiteSpace(MainVideoPath))
        {
            var sourceDirectory = Path.GetDirectoryName(MainVideoPath);
            if (!string.IsNullOrWhiteSpace(sourceDirectory))
            {
                return sourceDirectory;
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private string BuildSuggestedOutputPath()
    {
        return _services.OutputPaths.BuildOutputPath(
            ResolveOutputDirectory(),
            SeriesName,
            SeasonNumber,
            EpisodeNumber,
            Title);
    }

    private string BuildFallbackOutputName()
    {
        if (!string.IsNullOrWhiteSpace(OutputPath))
        {
            return Path.GetFileName(OutputPath);
        }

        return Path.GetFileName(BuildSuggestedOutputPath());
    }

    private async Task<bool> ApplyAutoDetectedFilesAsync(
        string selectedVideoPath,
        IReadOnlyCollection<string>? excludedSourcePaths = null)
    {
        var detectionProgressVersion = 0;
        try
        {
            SetBusy(true);
            SetStatus("Dateien werden erkannt...", 0);
            PreviewText = "Erkennung läuft...";

            detectionProgressVersion = BeginDetectionProgressSession();
            var detected = await _services.SeriesEpisodeMux.DetectFromSelectedVideoAsync(
                selectedVideoPath,
                update => HandleDetectionUpdate(detectionProgressVersion, update),
                excludedSourcePaths);
            CompleteDetectionProgressSession(detectionProgressVersion);
            detectionProgressVersion = 0;
            var localGuess = new EpisodeMetadataGuess(
                detected.SeriesName,
                detected.SuggestedTitle,
                detected.SeasonNumber,
                detected.EpisodeNumber);
            SetStatus("TVDB-Metadaten werden abgeglichen...", MetadataProgressValue);
            var automaticMetadata = await ApplyAutomaticMetadataAsync(detected);
            detected = automaticMetadata.Detected;
            var resolvedTitle = ShouldPreserveManualTitle(selectedVideoPath)
                ? Title
                : detected.SuggestedTitle;

            ApplySharedState(() =>
            {
                ApplyDetectedEpisodeState(
                    selectedVideoPath,
                    localGuess,
                    detected,
                    automaticMetadata.Resolution,
                    detected.SuggestedOutputFilePath,
                    resolvedTitle);
            });

            _lastSuggestedTitle = detected.SuggestedTitle;
            SetSuggestedOutputPath(BuildSuggestedOutputPath());
            InvalidateCurrentPlan();
            RefreshOutputTargetStatus();
            PreviewText = BuildDetectionPreview(detected);
            SetStatus("Zielvergleich wird berechnet...", InitialPlanProgressValue);
            var planSummaryReady = await RefreshPlanSummaryImmediatelyAsync(CancellationToken.None);
            if (!planSummaryReady)
            {
                SchedulePlanSummaryRefresh();
            }

            SetStatus(
                planSummaryReady
                    ? "Erkennung und Zielvergleich abgeschlossen"
                    : "Erkennung abgeschlossen, Zielvergleich noch offen",
                planSummaryReady ? 100 : InitialPlanProgressValue);
            RefreshCommands();
            return true;
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(ex.Message);
            SetStatus("Fehler", 0);
            return false;
        }
        finally
        {
            if (detectionProgressVersion != 0)
            {
                CompleteDetectionProgressSession(detectionProgressVersion);
            }

            SetBusy(false);
        }
    }

    private void HandleDetectionUpdate(int detectionProgressVersion, DetectionProgressUpdate update)
    {
        _ = Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (!IsDetectionProgressSessionCurrent(detectionProgressVersion))
            {
                return;
            }

            SetStatus(update.StatusText, ScaleDetectionProgressForOverallProgress(update.ProgressPercent));
            PreviewText = $"{update.StatusText}{Environment.NewLine}{Environment.NewLine}Bitte warten...";
        });
    }

    private static string BuildDetectionPreview(AutoDetectedEpisodeFiles detected)
    {
        var lines = new List<string>
        {
            "Dateien wurden automatisch erkannt. Mit 'Vorschau erzeugen' kannst du den geplanten MKVToolNix-Aufruf prüfen."
        };

        if (detected.HasPrimaryVideoSource)
        {
            lines.Add($"Hauptquelle: {Path.GetFileName(detected.MainVideoPath)}");
        }
        else
        {
            var supplementalSourceLabel = string.IsNullOrWhiteSpace(detected.AudioDescriptionPath)
                ? "Zusatzquelle"
                : "AD-Quelle";
            lines.Add($"{supplementalSourceLabel}: {Path.GetFileName(detected.AudioDescriptionPath ?? detected.MainVideoPath)}");
            lines.Add("Zu dieser Episode wurde noch keine frische Hauptvideoquelle gefunden.");
        }

        lines.Add($"Erkannte Episode: {detected.SeriesName} - {EpisodeFileNameHelper.BuildEpisodeCode(detected.SeasonNumber, detected.EpisodeNumber)} - {detected.SuggestedTitle}");

        if (detected.AdditionalVideoPaths.Count > 0)
        {
            lines.Add("Weitere Videospuren: " + string.Join(", ", detected.AdditionalVideoPaths.Select(Path.GetFileName)));
        }

        if (detected.AttachmentPaths.Count > 0)
        {
            lines.Add("Anhänge: " + string.Join(", ", detected.AttachmentPaths.Select(Path.GetFileName)));
        }

        if (detected.Notes.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Hinweise:");
            lines.AddRange(detected.Notes.Select(note => "- " + note));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private async Task RescanFromMainVideoAsync()
    {
        if (string.IsNullOrWhiteSpace(MainVideoPath))
        {
            _dialogService.ShowError("Bitte zuerst ein Hauptvideo auswählen.");
            return;
        }

        await ApplyAutoDetectedFilesAsync(MainVideoPath, ExcludedSourcePaths);
    }

    public override void SetAudioDescription(string? path)
    {
        base.SetAudioDescription(path);
        InvalidateCurrentPlan();
        RefreshOutputTargetStatus();
        SchedulePlanSummaryRefresh();
    }

    private void ClearAudioDescription()
    {
        SetAudioDescription(null);
    }

    public override void SetSubtitles(IEnumerable<string> paths)
    {
        base.SetSubtitles(paths);
        InvalidateCurrentPlan();
        SchedulePlanSummaryRefresh();
    }

    private void ClearSubtitles()
    {
        SetSubtitles([]);
    }

    public override void SetAttachments(IEnumerable<string> paths)
    {
        base.SetAttachments(paths);
        InvalidateCurrentPlan();
        SchedulePlanSummaryRefresh();
    }

    private void ClearAttachments()
    {
        SetAttachments([]);
    }

    public override void SetOutputPath(string outputPath)
    {
        base.SetOutputPath(outputPath);
        InvalidateCurrentPlan();
        RefreshOutputTargetStatus();
        SchedulePlanSummaryRefresh();
    }

    private void SetSuggestedOutputPath(string path)
    {
        MarkOutputPathAsAutomatic();
        SetAutomaticOutputPath(path);
    }

    private void UpdateSuggestedOutputPathIfAutomatic()
    {
        if (OutputPathWasManuallyChanged)
        {
            return;
        }

        SetSuggestedOutputPath(BuildSuggestedOutputPath());
    }

    public override void SetAutomaticOutputPath(string outputPath)
    {
        base.SetAutomaticOutputPath(outputPath);
        InvalidateCurrentPlan();
        RefreshOutputTargetStatus();
        SchedulePlanSummaryRefresh();
    }

    private void RefreshOutputTargetStatus()
    {
        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            OutputTargetStatusText = string.Empty;
            return;
        }

        if (!HasPrimaryVideoSource)
        {
            OutputTargetStatusText = ArchiveState == EpisodeArchiveState.Existing
                && _services.OutputPaths.IsArchivePath(OutputPath)
                ? "Es liegt nur Zusatzmaterial ohne frische Hauptvideoquelle vor. Bei Vorschau oder Mux kann die vorhandene Bibliotheks-MKV als Hauptquelle weiterverwendet und ergänzt werden."
                : "Es liegt nur Zusatzmaterial ohne frische Hauptvideoquelle vor. Ohne vorhandene Bibliotheks-MKV kann aktuell kein vollständiger Mux geplant werden.";
            return;
        }

        // Die Basisklasse hält den Archivzustand bereits beim Setzen des Zielpfads aktuell. Das vermeidet,
        // dass diese rein UI-nahe Statusberechnung denselben Pfad bei jeder Aktualisierung erneut anfasst.
        if (ArchiveState == EpisodeArchiveState.Existing)
        {
            OutputTargetStatusText = _services.OutputPaths.IsArchivePath(OutputPath)
                ? "Am Ziel liegt bereits eine MKV. Bei Vorschau oder Mux wird geprüft, ob etwas fehlt oder ersetzt werden muss."
                : "Die Zieldatei existiert bereits und würde beim Mux überschrieben.";
            return;
        }

        OutputTargetStatusText = _services.OutputPaths.IsArchivePath(OutputPath)
            ? "Das Ziel in der Serienbibliothek ist noch frei. Die Episode kann direkt dort einsortiert werden."
            : "Die Zieldatei existiert noch nicht.";
    }

    private async Task SelectMainVideoAsync()
    {
        var path = _dialogService.SelectMainVideo(ResolveMainVideoInitialDirectory());
        if (!string.IsNullOrWhiteSpace(path))
        {
            ReplaceExcludedSourcePaths([]);
            await ApplyAutoDetectedFilesAsync(path, ExcludedSourcePaths);
        }
    }

    private async Task SelectAudioDescriptionAsync()
    {
        if (string.IsNullOrWhiteSpace(MainVideoPath))
        {
            var selectedAudioDescriptionPath = _dialogService.SelectAudioDescription(ResolveMainVideoInitialDirectory());
            if (!string.IsNullOrWhiteSpace(selectedAudioDescriptionPath))
            {
                ReplaceExcludedSourcePaths([]);
                await ApplyAutoDetectedFilesAsync(selectedAudioDescriptionPath, ExcludedSourcePaths);
            }

            return;
        }

        var choice = _dialogService.AskAudioDescriptionChoice();
        if (choice == MessageBoxResult.No)
        {
            ClearAudioDescription();
            return;
        }

        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        var path = _dialogService.SelectAudioDescription(ResolveComponentInitialDirectory());
        if (!string.IsNullOrWhiteSpace(path))
        {
            SetAudioDescription(path);
        }
    }

    private void SelectSubtitles()
    {
        if (string.IsNullOrWhiteSpace(MainVideoPath))
        {
            _dialogService.ShowError("Bitte zuerst ein Hauptvideo auswählen.");
            return;
        }

        var choice = _dialogService.AskSubtitlesChoice();
        if (choice == MessageBoxResult.No)
        {
            ClearSubtitles();
            return;
        }

        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        var paths = _dialogService.SelectSubtitles(ResolveComponentInitialDirectory());
        if (paths is not null)
        {
            SetSubtitles(paths);
        }
    }

    private void SelectAttachments()
    {
        if (string.IsNullOrWhiteSpace(MainVideoPath))
        {
            _dialogService.ShowError("Bitte zuerst ein Hauptvideo auswählen.");
            return;
        }

        var choice = _dialogService.AskAttachmentChoice();
        if (choice == MessageBoxResult.No)
        {
            ClearAttachments();
            return;
        }

        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        var paths = _dialogService.SelectAttachments(ResolveComponentInitialDirectory());
        if (paths is not null)
        {
            SetAttachments(paths);
        }
    }

    private void SelectOutput()
    {
        var path = _dialogService.SelectOutput(ResolveOutputDirectory(), BuildFallbackOutputName());
        if (!string.IsNullOrWhiteSpace(path))
        {
            SetOutputPath(path);
        }
    }

    private async Task OpenTvdbLookupAsync()
    {
        var outcome = await _reviewWorkflow.ReviewMetadataAsync(
            this,
            SetStatus,
            ProgressValue,
            "Prüfe TVDB-Zuordnung...",
            "TVDB-Prüfung abgebrochen",
            "Lokale Erkennung freigegeben",
            "TVDB-Zuordnung freigegeben",
            () =>
            {
                // Bereits geplante Hintergrund-Refreshes dürfen nach einer manuellen TVDB-Korrektur
                // nicht mehr mit dem alten Episodencode in die UI zurückschreiben.
                _planSummaryRefresh.Cancel(invalidateInFlightRefreshes: true);
                UpdateSuggestedOutputPathIfAutomatic();
                RefreshOutputTargetStatus();
                SchedulePlanSummaryRefresh();
            });

        switch (outcome)
        {
            case EpisodeMetadataReviewOutcome.KeptLocalDetection:
                PreviewText = "Lokale Metadaten beibehalten. Bitte bei Bedarf 'Vorschau erzeugen' erneut ausführen.";
                break;
            case EpisodeMetadataReviewOutcome.AppliedTvdbSelection:
                PreviewText = "TVDB-Metadaten übernommen. Bitte bei Bedarf 'Vorschau erzeugen' erneut ausführen.";
                break;
            default:
                return;
        }

        RefreshCommands();
    }

    private async Task ReviewSourcesAsync()
    {
        await _reviewWorkflow.ReviewManualSourceAsync(
            this,
            SetStatus,
            ProgressValue,
            "Prüfe Quelle...",
            "Quellenprüfung abgebrochen",
            "Quellenprüfung konnte nicht geöffnet werden",
            "Quelle freigegeben",
            "Auf alternative Quelle umgestellt",
            async tentativeExclusions =>
            {
                var detectionSeedPath = string.IsNullOrWhiteSpace(DetectionSeedPath)
                    ? MainVideoPath
                    : DetectionSeedPath;
                if (string.IsNullOrWhiteSpace(detectionSeedPath))
                {
                    _dialogService.ShowWarning("Hinweis", "Es konnte keine alternative Quelle ermittelt werden.");
                    return false;
                }

                var updated = await ApplyAutoDetectedFilesAsync(detectionSeedPath, tentativeExclusions);
                if (!updated)
                {
                    return false;
                }

                ReplaceExcludedSourcePaths(tentativeExclusions);
                return true;
            });
    }

    public override void ApproveCurrentReviewTarget()
    {
        base.ApproveCurrentReviewTarget();
        RefreshCommands();
    }

    public override void ApplyTvdbSelection(TvdbEpisodeSelection selection)
    {
        ApplySharedState(() => base.ApplyTvdbSelection(selection));
        _lastSuggestedTitle = selection.EpisodeTitle;
        UpdateSuggestedOutputPathIfAutomatic();
        RefreshOutputTargetStatus();
        SchedulePlanSummaryRefresh();
    }

    public override void ApplyLocalMetadataGuess()
    {
        ApplySharedState(base.ApplyLocalMetadataGuess);
        _lastSuggestedTitle = LocalTitle;
        UpdateSuggestedOutputPathIfAutomatic();
        RefreshOutputTargetStatus();
        SchedulePlanSummaryRefresh();
    }

    private async Task<(AutoDetectedEpisodeFiles Detected, EpisodeMetadataResolutionResult Resolution)> ApplyAutomaticMetadataAsync(AutoDetectedEpisodeFiles detected)
    {
        var resolution = await _services.EpisodeMetadata.ResolveAutomaticallyAsync(new EpisodeMetadataGuess(
            detected.SeriesName,
            detected.SuggestedTitle,
            detected.SeasonNumber,
            detected.EpisodeNumber));

        if (resolution.Selection is not null)
        {
            detected = EpisodeMetadataMergeHelper.ApplySelection(detected, resolution.Selection);
        }

        return (detected, resolution);
    }

    private bool ShouldPreserveManualTitle(string selectedVideoPath)
    {
        return SingleEpisodeManualTitlePolicy.ShouldPreserve(
            Title,
            _lastSuggestedTitle,
            DetectionSeedPath,
            MainVideoPath,
            selectedVideoPath);
    }

    public override void ApproveMetadataReview(string statusText)
    {
        base.ApproveMetadataReview(statusText);
        RefreshOutputTargetStatus();
        SchedulePlanSummaryRefresh();
    }

    public void HandleArchiveConfigurationChanged()
    {
        InvalidateCurrentPlan();
        if (UsesAutomaticOutputPath)
        {
            UpdateSuggestedOutputPathIfAutomatic();
            return;
        }

        RefreshOutputTargetStatus();
        SchedulePlanSummaryRefresh();
    }

    private void HandleManualMetadataOverride()
    {
        if (_isApplyingSharedState)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(MetadataStatusText) || RequiresMetadataReview)
        {
            ClearTvdbSelection();
            ApproveMetadataReview("Metadaten manuell angepasst.");
        }
    }

    private void ApplySharedState(Action applyAction)
    {
        _isApplyingSharedState = true;
        try
        {
            applyAction();
            // Metadatenwechsel können den Archivvergleich und Mehrfachfolgenhinweise vollständig ändern.
            // Alte Hinweise wie "Archiv prüfen" dürfen deshalb nicht sichtbar bleiben, bis der
            // nachfolgende Plan-Refresh neue, tatsächlich passende Hinweise berechnet.
            SetPlanNotes([]);
        }
        finally
        {
            _isApplyingSharedState = false;
        }
    }

    private void InvalidateCurrentPlan()
    {
        _planCache.Invalidate(this);
        ClearPlanPresentation();
        PlanRefreshProblemText = string.Empty;
    }

}
