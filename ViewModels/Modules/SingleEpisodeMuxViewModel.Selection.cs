using System.Threading;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.Windows;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

public sealed partial class SingleEpisodeMuxViewModel
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
        try
        {
            SetBusy(true);
            SetStatus("Dateien werden erkannt...", 0);
            PreviewText = "Erkennung läuft...";

            var detected = await _services.SeriesEpisodeMux.DetectFromSelectedVideoAsync(
                selectedVideoPath,
                HandleDetectionUpdate,
                excludedSourcePaths);
            var localGuess = new EpisodeMetadataGuess(
                detected.SeriesName,
                detected.SuggestedTitle,
                detected.SeasonNumber,
                detected.EpisodeNumber);
            SetStatus("TVDB-Metadaten werden abgeglichen...", 88);
            var automaticMetadata = await ApplyAutomaticMetadataAsync(detected);
            detected = automaticMetadata.Detected;
            var resolvedTitle = string.IsNullOrWhiteSpace(Title) || Title == _lastSuggestedTitle
                ? detected.SuggestedTitle
                : Title;

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
            _currentPlan = null;
            RefreshOutputTargetStatus();
            PreviewText = BuildDetectionPreview(detected);
            SetStatus("Dateien erkannt", 100);
            RefreshCommands();
            SchedulePlanSummaryRefresh();
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
            SetBusy(false);
        }
    }

    private void HandleDetectionUpdate(DetectionProgressUpdate update)
    {
        _ = Application.Current.Dispatcher.BeginInvoke(() =>
        {
            SetStatus(update.StatusText, update.ProgressPercent);
            PreviewText = $"{update.StatusText}{Environment.NewLine}{Environment.NewLine}Bitte warten...";
        });
    }

    private static string BuildDetectionPreview(AutoDetectedEpisodeFiles detected)
    {
        var lines = new List<string>
        {
            "Dateien wurden automatisch erkannt. Mit 'Vorschau erzeugen' kannst du den mkvmerge-Aufruf prüfen.",
            $"Hauptquelle: {Path.GetFileName(detected.MainVideoPath)}",
            $"Erkannte Episode: {detected.SeriesName} - S{detected.SeasonNumber}E{detected.EpisodeNumber} - {detected.SuggestedTitle}"
        };

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
        _currentPlan = null;
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
        _currentPlan = null;
        SchedulePlanSummaryRefresh();
    }

    private void ClearSubtitles()
    {
        SetSubtitles([]);
    }

    public override void SetAttachments(IEnumerable<string> paths)
    {
        base.SetAttachments(paths);
        _currentPlan = null;
        SchedulePlanSummaryRefresh();
    }

    private void ClearAttachments()
    {
        SetAttachments([]);
    }

    public override void SetOutputPath(string outputPath)
    {
        base.SetOutputPath(outputPath);
        _currentPlan = null;
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
        _currentPlan = null;
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

        if (File.Exists(OutputPath))
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
        var path = _dialogService.SelectOutput(ResolveComponentInitialDirectory(), BuildFallbackOutputName());
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

    private void TestSelectedSources()
    {
        _ = ReviewSourcesAsync();
    }

    private async Task ReviewSourcesAsync()
    {
        await _reviewWorkflow.ReviewManualSourceAsync(
            this,
            SetStatus,
            ProgressValue,
            "Prüfe Quelle...",
            "Quellenprüfung abgebrochen",
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

    public override void ApproveMetadataReview(string statusText)
    {
        base.ApproveMetadataReview(statusText);
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
            ApproveMetadataReview("Metadaten manuell angepasst.");
        }
    }

    private void ApplySharedState(Action applyAction)
    {
        _isApplyingSharedState = true;
        try
        {
            applyAction();
        }
        finally
        {
            _isApplyingSharedState = false;
        }
    }

}