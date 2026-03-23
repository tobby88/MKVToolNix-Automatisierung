using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels.Commands;
using MkvToolnixAutomatisierung.Windows;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

public sealed class SingleEpisodeMuxViewModel : EpisodeEditModel
{
    private static readonly string[] PreferredDownloadsSubPath = ["MediathekView-latest-win", "Downloads"];

    private readonly AppServices _services;
    private readonly UserDialogService _dialogService;
    private readonly BufferedTextStore _previewOutputBuffer;
    private readonly EpisodeReviewWorkflow _reviewWorkflow;
    private CancellationTokenSource? _planSummaryRefreshCts;

    private string _previewText = string.Empty;
    private string _statusText = "Bereit";
    private int _progressValue;
    private bool _isBusy;
    private string _lastSuggestedTitle = string.Empty;
    private bool _isApplyingSharedState;
    private string _outputTargetStatusText = string.Empty;
    private SeriesEpisodeMuxPlan? _currentPlan;
    private int _planSummaryVersion;

    public SingleEpisodeMuxViewModel(AppServices services, UserDialogService dialogService)
    {
        _services = services;
        _dialogService = dialogService;
        _reviewWorkflow = new EpisodeReviewWorkflow(dialogService, services.EpisodeMetadata);
        _previewOutputBuffer = new BufferedTextStore(
            flush => _ = Application.Current.Dispatcher.BeginInvoke(flush),
            text => PreviewText = text);

        SelectMainVideoCommand = new AsyncRelayCommand(SelectMainVideoAsync, () => !_isBusy);
        SelectAudioDescriptionCommand = new AsyncRelayCommand(SelectAudioDescriptionAsync, () => !_isBusy);
        SelectSubtitlesCommand = new RelayCommand(SelectSubtitles, () => !_isBusy && !string.IsNullOrWhiteSpace(MainVideoPath));
        SelectAttachmentCommand = new RelayCommand(SelectAttachments, () => !_isBusy && !string.IsNullOrWhiteSpace(MainVideoPath));
        SelectOutputCommand = new RelayCommand(SelectOutput, () => !_isBusy);
        RescanCommand = new AsyncRelayCommand(RescanFromMainVideoAsync, () => !_isBusy && !string.IsNullOrWhiteSpace(MainVideoPath));
        OpenTvdbLookupCommand = new AsyncRelayCommand(OpenTvdbLookupAsync, () => !_isBusy && !string.IsNullOrWhiteSpace(MainVideoPath));
        TestSelectedSourcesCommand = new RelayCommand(TestSelectedSources, () => !_isBusy && ManualCheckFilePaths.Count > 0);
        CreatePreviewCommand = new AsyncRelayCommand(CreatePreviewAsync, () => !_isBusy);
        ExecuteMuxCommand = new AsyncRelayCommand(ExecuteMuxAsync, () => !_isBusy);
    }

    public AsyncRelayCommand SelectMainVideoCommand { get; }
    public AsyncRelayCommand SelectAudioDescriptionCommand { get; }
    public RelayCommand SelectSubtitlesCommand { get; }
    public RelayCommand SelectAttachmentCommand { get; }
    public RelayCommand SelectOutputCommand { get; }
    public AsyncRelayCommand RescanCommand { get; }
    public AsyncRelayCommand OpenTvdbLookupCommand { get; }
    public RelayCommand TestSelectedSourcesCommand { get; }
    public AsyncRelayCommand CreatePreviewCommand { get; }
    public AsyncRelayCommand ExecuteMuxCommand { get; }

    public string AudioDescriptionButtonText => string.IsNullOrWhiteSpace(MainVideoPath)
        ? "AD-Datei wählen"
        : "AD korrigieren";

    public override string SubtitleDisplayText => SubtitlePaths.Count == 0
        ? string.Empty
        : string.Join(Environment.NewLine, SubtitlePaths.Select(Path.GetFileName));

    public override string AttachmentDisplayText => AttachmentPaths.Count == 0
        ? string.Empty
        : string.Join(Environment.NewLine, AttachmentPaths.Select(Path.GetFileName));

    public string PreviewText
    {
        get => _previewText;
        private set
        {
            _previewText = value;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public int ProgressValue
    {
        get => _progressValue;
        private set
        {
            _progressValue = value;
            OnPropertyChanged();
        }
    }

    public string ManualCheckButtonText => IsManualCheckApproved
        ? "Quelle erneut prüfen"
        : "Quelle prüfen / freigeben";

    public string ManualCheckBadgeText => RequiresManualCheck
        ? IsManualCheckApproved ? "Quelle freigegeben" : "Quelle prüfen"
        : "Quelle ok";

    public bool HasMetadataStatus => !string.IsNullOrWhiteSpace(MetadataStatusText);
    public string MetadataActionButtonText => RequiresMetadataReview && !IsMetadataReviewApproved
        ? "TVDB prüfen"
        : "TVDB anpassen";

    public string MetadataBadgeText => RequiresMetadataReview
        ? IsMetadataReviewApproved ? "TVDB freigegeben" : "TVDB prüfen"
        : HasMetadataStatus ? "TVDB ok" : "TVDB offen";

    public string OutputTargetStatusText
    {
        get => _outputTargetStatusText;
        private set
        {
            if (_outputTargetStatusText == value)
            {
                return;
            }

            _outputTargetStatusText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasOutputTargetStatus));
            OnPropertyChanged(nameof(OutputTargetBadgeText));
        }
    }

    public bool HasOutputTargetStatus => !string.IsNullOrWhiteSpace(OutputTargetStatusText);

    public string OutputTargetBadgeText => string.IsNullOrWhiteSpace(OutputPath)
        ? "Bibliothek offen"
        : File.Exists(OutputPath) ? "In Bibliothek" : "Neu für Bibliothek";

    public bool HasPlanSummary => !string.IsNullOrWhiteSpace(PlanSummaryText);

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

    private async Task CreatePreviewAsync()
    {
        try
        {
            SetBusy(true);
            SetStatus("Erzeuge Vorschau...", 0);
            _currentPlan = await BuildPlanAsync();
            RefreshOutputTargetStatusFromPlan(_currentPlan);
            PlanSummaryText = _currentPlan.BuildCompactSummaryText();
            UsageSummary = _currentPlan.BuildUsageSummary();
            PreviewText = _services.SeriesEpisodeMux.BuildPreviewText(_currentPlan);
            SetStatus("Vorschau bereit", 0);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(ex.Message);
            SetStatus("Fehler", 0);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ExecuteMuxAsync()
    {
        try
        {
            SetBusy(true);
            _currentPlan ??= await BuildPlanAsync();
            RefreshOutputTargetStatusFromPlan(_currentPlan);
            PlanSummaryText = _currentPlan.BuildCompactSummaryText();
            UsageSummary = _currentPlan.BuildUsageSummary();
            PreviewText = _services.SeriesEpisodeMux.BuildPreviewText(_currentPlan)
                + Environment.NewLine
                + Environment.NewLine
                + "mkvmerge-Ausgabe:"
                + Environment.NewLine;
            ResetPreviewOutputBuffer(PreviewText);

            if (_currentPlan.SkipMux)
            {
                SetStatus("Zieldatei bereits aktuell", 100);
                _dialogService.ShowInfo("Hinweis", _currentPlan.SkipReason ?? "Die Zieldatei ist bereits vollständig.");
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

            if (!await PrepareWorkingCopyAsync(_currentPlan))
            {
                return;
            }

            SetStatus("Muxing läuft...", 0);
            var result = await _services.MuxWorkflow.ExecuteMuxAsync(_currentPlan, HandleMuxOutput, HandleMuxUpdate);

            if (result.ExitCode == 0 && !result.HasWarning)
            {
                SetStatus("Muxing erfolgreich abgeschlossen", 100);
                _dialogService.ShowInfo("Erfolg", $"MKV erfolgreich erstellt:\n{_currentPlan.OutputFilePath}");
                await OfferSingleEpisodeCleanupAsync(_currentPlan);
            }
            else if ((result.ExitCode == 0 && result.HasWarning)
                || (result.ExitCode == 1 && File.Exists(_currentPlan.OutputFilePath)))
            {
                SetStatus("Muxing mit Warnungen abgeschlossen", 100);
                _dialogService.ShowWarning("Warnung", $"Die MKV wurde erstellt, aber mkvmerge hat Warnungen gemeldet.\n\n{_currentPlan.OutputFilePath}");
                await OfferSingleEpisodeCleanupAsync(_currentPlan);
            }
            else
            {
                SetStatus($"Muxing fehlgeschlagen (Exit-Code {result.ExitCode})", 0);
                _dialogService.ShowWarning("Hinweis", $"mkvmerge wurde mit Exit-Code {result.ExitCode} beendet.");
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(ex.Message);
            SetStatus("Fehler", 0);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<SeriesEpisodeMuxPlan> BuildPlanAsync()
    {
        RequireValue(MainVideoPath, "Bitte ein Hauptvideo auswählen.");
        RequireValue(OutputPath, "Bitte eine Ausgabedatei wählen.");
        RequireValue(Title.Trim(), "Bitte einen Dateititel eingeben.");
        return await _services.EpisodePlans.BuildPlanAsync(this);
    }

    private void RefreshOutputTargetStatusFromPlan(SeriesEpisodeMuxPlan plan)
    {
        if (plan.SkipMux)
        {
            OutputTargetStatusText = plan.SkipReason ?? "Die Zieldatei ist bereits vollständig.";
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

    private async Task<bool> PrepareWorkingCopyAsync(SeriesEpisodeMuxPlan plan)
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

        await _services.MuxWorkflow.PrepareWorkingCopyAsync(plan, HandleWorkingCopyPreparationUpdate);
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
            var statusText = update.ProgressPercent is int progressPercent
                ? $"Muxing läuft... {progressPercent}%"
                : "Muxing läuft...";

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

    private async Task OfferSingleEpisodeCleanupAsync(SeriesEpisodeMuxPlan plan)
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
            });

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
        _planSummaryRefreshCts?.Cancel();
        _planSummaryRefreshCts?.Dispose();

        var cancellationSource = new CancellationTokenSource();
        _planSummaryRefreshCts = cancellationSource;

        _ = RefreshPlanSummaryDebouncedAsync(cancellationSource.Token);
    }

    private async Task RefreshPlanSummaryDebouncedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(250, cancellationToken);
            await RefreshPlanSummaryAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshPlanSummaryAsync()
    {
        var version = Interlocked.Increment(ref _planSummaryVersion);

        if (string.IsNullOrWhiteSpace(MainVideoPath)
            || string.IsNullOrWhiteSpace(OutputPath)
            || string.IsNullOrWhiteSpace(Title))
        {
            PlanSummaryText = string.Empty;
            UsageSummary = null;
            return;
        }

        try
        {
            var plan = await BuildPlanAsync();
            if (version != _planSummaryVersion)
            {
                return;
            }

            _currentPlan = plan;
            RefreshOutputTargetStatusFromPlan(plan);
            PlanSummaryText = plan.BuildCompactSummaryText();
            UsageSummary = plan.BuildUsageSummary();
        }
        catch
        {
            if (version != _planSummaryVersion)
            {
                return;
            }

            PlanSummaryText = string.Empty;
            UsageSummary = null;
        }
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        SelectMainVideoCommand.RaiseCanExecuteChanged();
        SelectAudioDescriptionCommand.RaiseCanExecuteChanged();
        SelectSubtitlesCommand.RaiseCanExecuteChanged();
        SelectAttachmentCommand.RaiseCanExecuteChanged();
        SelectOutputCommand.RaiseCanExecuteChanged();
        RescanCommand.RaiseCanExecuteChanged();
        OpenTvdbLookupCommand.RaiseCanExecuteChanged();
        TestSelectedSourcesCommand.RaiseCanExecuteChanged();
        CreatePreviewCommand.RaiseCanExecuteChanged();
        ExecuteMuxCommand.RaiseCanExecuteChanged();
    }

    private void SetStatus(string text, int progressValue)
    {
        StatusText = text;
        ProgressValue = Math.Clamp(progressValue, 0, 100);
    }

    private void ResetPreviewOutputBuffer(string initialText)
    {
        _previewOutputBuffer.Reset(initialText);
    }

    protected override void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        switch (propertyName)
        {
            case nameof(MainVideoPath):
                base.OnPropertyChanged(nameof(AudioDescriptionButtonText));
                break;
            case nameof(SubtitlePaths):
                base.OnPropertyChanged(nameof(SubtitleDisplayText));
                break;
            case nameof(AttachmentPaths):
                base.OnPropertyChanged(nameof(AttachmentDisplayText));
                break;
            case nameof(OutputPath):
                base.OnPropertyChanged(nameof(OutputTargetBadgeText));
                break;
            case nameof(RequiresManualCheck):
            case nameof(ManualCheckText):
                base.OnPropertyChanged(nameof(ManualCheckButtonText));
                base.OnPropertyChanged(nameof(ManualCheckBadgeText));
                break;
            case nameof(MetadataStatusText):
                base.OnPropertyChanged(nameof(HasMetadataStatus));
                base.OnPropertyChanged(nameof(MetadataBadgeText));
                break;
            case nameof(RequiresMetadataReview):
            case nameof(IsMetadataReviewApproved):
                base.OnPropertyChanged(nameof(MetadataActionButtonText));
                base.OnPropertyChanged(nameof(MetadataBadgeText));
                break;
            case nameof(PlanSummaryText):
                base.OnPropertyChanged(nameof(HasPlanSummary));
                break;
        }

        if (_isApplyingSharedState)
        {
            return;
        }

        if (propertyName is nameof(SeriesName) or nameof(SeasonNumber) or nameof(EpisodeNumber) or nameof(Title))
        {
            _currentPlan = null;
            HandleManualMetadataOverride();
            UpdateSuggestedOutputPathIfAutomatic();
            SchedulePlanSummaryRefresh();
        }
    }
}



