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

public sealed class SingleEpisodeMuxViewModel : INotifyPropertyChanged
{
    private const string DefaultMainVideoDirectory = @"C:\Users\tobby\Downloads\MediathekView-latest-win\Downloads";

    private readonly AppServices _services;
    private readonly UserDialogService _dialogService;
    private CancellationTokenSource? _planSummaryRefreshCts;

    private string? _mainVideoPath;
    private string? _audioDescriptionPath;
    private List<string> _subtitlePaths = [];
    private List<string> _attachmentPaths = [];
    private List<string> _relatedEpisodeFilePaths = [];
    private string? _outputPath;
    private string _seriesName = string.Empty;
    private string _seasonNumber = "xx";
    private string _episodeNumber = "xx";
    private string _title = string.Empty;
    private string _previewText = string.Empty;
    private string _statusText = "Bereit";
    private int _progressValue;
    private bool _isBusy;
    private bool _outputPathWasManuallyChanged;
    private string _lastSuggestedTitle = string.Empty;
    private EpisodeMetadataGuess? _detectedMetadataGuess;
    private string _metadataStatusText = string.Empty;
    private bool _requiresMetadataReview;
    private bool _isMetadataReviewApproved = true;
    private bool _isApplyingMetadataState;
    private string _outputTargetStatusText = string.Empty;
    private string _planSummaryText = string.Empty;
    private EpisodeUsageSummary? _usageSummary;
    private bool _requiresManualCheck;
    private string _manualCheckText = string.Empty;
    private List<string> _manualCheckFilePaths = [];
    private string? _detectionSeedPath;
    private readonly HashSet<string> _excludedSourcePaths = new(StringComparer.OrdinalIgnoreCase);
    private string? _approvedReviewPath;
    private SeriesEpisodeMuxPlan? _currentPlan;
    private int _planSummaryVersion;

    public SingleEpisodeMuxViewModel(AppServices services, UserDialogService dialogService)
    {
        _services = services;
        _dialogService = dialogService;

        SelectMainVideoCommand = new AsyncRelayCommand(SelectMainVideoAsync, () => !_isBusy);
        SelectAudioDescriptionCommand = new AsyncRelayCommand(SelectAudioDescriptionAsync, () => !_isBusy);
        SelectSubtitlesCommand = new RelayCommand(SelectSubtitles, () => !_isBusy && !string.IsNullOrWhiteSpace(_mainVideoPath));
        SelectAttachmentCommand = new RelayCommand(SelectAttachments, () => !_isBusy && !string.IsNullOrWhiteSpace(_mainVideoPath));
        SelectOutputCommand = new RelayCommand(SelectOutput, () => !_isBusy);
        RescanCommand = new AsyncRelayCommand(RescanFromMainVideoAsync, () => !_isBusy && !string.IsNullOrWhiteSpace(_mainVideoPath));
        OpenTvdbLookupCommand = new AsyncRelayCommand(OpenTvdbLookupAsync, () => !_isBusy && !string.IsNullOrWhiteSpace(_mainVideoPath));
        TestSelectedSourcesCommand = new RelayCommand(TestSelectedSources, () => !_isBusy && _manualCheckFilePaths.Count > 0);
        CreatePreviewCommand = new AsyncRelayCommand(CreatePreviewAsync, () => !_isBusy);
        ExecuteMuxCommand = new AsyncRelayCommand(ExecuteMuxAsync, () => !_isBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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

    public string AudioDescriptionButtonText => string.IsNullOrWhiteSpace(_mainVideoPath)
        ? "AD-Datei waehlen"
        : "AD korrigieren";

    public string MainVideoPath
    {
        get => _mainVideoPath ?? string.Empty;
        private set
        {
            _mainVideoPath = string.IsNullOrWhiteSpace(value) ? null : value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AudioDescriptionButtonText));
        }
    }

    public string AudioDescriptionPath
    {
        get => _audioDescriptionPath ?? string.Empty;
        private set
        {
            _audioDescriptionPath = string.IsNullOrWhiteSpace(value) ? null : value;
            OnPropertyChanged();
        }
    }

    public string AttachmentDisplayText => _attachmentPaths.Count == 0
        ? string.Empty
        : string.Join(Environment.NewLine, _attachmentPaths.Select(Path.GetFileName));

    public string OutputPath
    {
        get => _outputPath ?? string.Empty;
        private set
        {
            _outputPath = string.IsNullOrWhiteSpace(value) ? null : value;
            OnPropertyChanged();
        }
    }

    public string SubtitleDisplayText => _subtitlePaths.Count == 0
        ? string.Empty
        : string.Join(Environment.NewLine, _subtitlePaths.Select(Path.GetFileName));

    public string SeriesName
    {
        get => _seriesName;
        set
        {
            var normalized = value.Trim();
            if (_seriesName == normalized)
            {
                return;
            }

            _seriesName = normalized;
            _currentPlan = null;
            OnPropertyChanged();
            HandleManualMetadataOverride();
            UpdateSuggestedOutputPathIfAutomatic();
            SchedulePlanSummaryRefresh();
        }
    }

    public string SeasonNumber
    {
        get => _seasonNumber;
        set
        {
            var normalized = EpisodeMetadataMergeHelper.NormalizeEpisodeNumber(value);
            if (_seasonNumber == normalized)
            {
                return;
            }

            _seasonNumber = normalized;
            _currentPlan = null;
            OnPropertyChanged();
            HandleManualMetadataOverride();
            UpdateSuggestedOutputPathIfAutomatic();
            SchedulePlanSummaryRefresh();
        }
    }

    public string EpisodeNumber
    {
        get => _episodeNumber;
        set
        {
            var normalized = EpisodeMetadataMergeHelper.NormalizeEpisodeNumber(value);
            if (_episodeNumber == normalized)
            {
                return;
            }

            _episodeNumber = normalized;
            _currentPlan = null;
            OnPropertyChanged();
            HandleManualMetadataOverride();
            UpdateSuggestedOutputPathIfAutomatic();
            SchedulePlanSummaryRefresh();
        }
    }

    public string Title
    {
        get => _title;
        set
        {
            var normalized = value.Trim();
            if (_title == normalized)
            {
                return;
            }

            _title = normalized;
            _currentPlan = null;
            OnPropertyChanged();
            HandleManualMetadataOverride();
            UpdateSuggestedOutputPathIfAutomatic();
            SchedulePlanSummaryRefresh();
        }
    }

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

    public bool RequiresManualCheck
    {
        get => _requiresManualCheck;
        private set
        {
            _requiresManualCheck = value;
            OnPropertyChanged();
        }
    }

    public string ManualCheckText
    {
        get => _manualCheckText;
        private set
        {
            _manualCheckText = value;
            OnPropertyChanged();
        }
    }

    public string ManualCheckButtonText => IsManualCheckApproved
        ? "Quelle erneut pruefen"
        : "Quelle pruefen / freigeben";

    public string MetadataStatusText
    {
        get => _metadataStatusText;
        private set
        {
            if (_metadataStatusText == value)
            {
                return;
            }

            _metadataStatusText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMetadataStatus));
        }
    }

    public bool HasMetadataStatus => !string.IsNullOrWhiteSpace(MetadataStatusText);

    public bool RequiresMetadataReview
    {
        get => _requiresMetadataReview;
        private set
        {
            if (_requiresMetadataReview == value)
            {
                return;
            }

            _requiresMetadataReview = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MetadataActionButtonText));
        }
    }

    public bool IsMetadataReviewApproved
    {
        get => _isMetadataReviewApproved;
        private set
        {
            if (_isMetadataReviewApproved == value)
            {
                return;
            }

            _isMetadataReviewApproved = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MetadataActionButtonText));
        }
    }

    public string MetadataActionButtonText => RequiresMetadataReview && !IsMetadataReviewApproved
        ? "TVDB pruefen"
        : "TVDB anpassen";

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
        }
    }

    public bool HasOutputTargetStatus => !string.IsNullOrWhiteSpace(OutputTargetStatusText);

    public string PlanSummaryText
    {
        get => _planSummaryText;
        private set
        {
            if (_planSummaryText == value)
            {
                return;
            }

            _planSummaryText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPlanSummary));
        }
    }

    public bool HasPlanSummary => !string.IsNullOrWhiteSpace(PlanSummaryText);

    public EpisodeUsageSummary? UsageSummary
    {
        get => _usageSummary;
        private set
        {
            if (_usageSummary == value)
            {
                return;
            }

            _usageSummary = value;
            OnPropertyChanged();
        }
    }

    private string ResolveMainVideoInitialDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_mainVideoPath))
        {
            var existingDirectory = Path.GetDirectoryName(_mainVideoPath);
            if (!string.IsNullOrWhiteSpace(existingDirectory) && Directory.Exists(existingDirectory))
            {
                return existingDirectory;
            }
        }

        return Directory.Exists(DefaultMainVideoDirectory)
            ? DefaultMainVideoDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private string ResolveComponentInitialDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_mainVideoPath))
        {
            var directory = Path.GetDirectoryName(_mainVideoPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                return directory;
            }
        }

        return ResolveMainVideoInitialDirectory();
    }

    private string ResolveOutputDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_outputPath))
        {
            var outputDirectory = Path.GetDirectoryName(_outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                return outputDirectory;
            }
        }

        if (!string.IsNullOrWhiteSpace(_mainVideoPath))
        {
            var sourceDirectory = Path.GetDirectoryName(_mainVideoPath);
            if (!string.IsNullOrWhiteSpace(sourceDirectory))
            {
                return sourceDirectory;
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private string BuildSuggestedOutputPath()
    {
        return EpisodeMetadataMergeHelper.BuildSuggestedOutputFilePath(
            ResolveOutputDirectory(),
            SeriesName,
            SeasonNumber,
            EpisodeNumber,
            Title);
    }

    private string BuildFallbackOutputName()
    {
        if (!string.IsNullOrWhiteSpace(_outputPath))
        {
            return Path.GetFileName(_outputPath);
        }

        return Path.GetFileName(BuildSuggestedOutputPath());
    }

    private string? CurrentReviewTargetPath => _manualCheckFilePaths.FirstOrDefault();

    private bool IsManualCheckApproved => !RequiresManualCheck
        || string.Equals(_approvedReviewPath, CurrentReviewTargetPath, StringComparison.OrdinalIgnoreCase);

    private async Task<bool> ApplyAutoDetectedFilesAsync(
        string selectedVideoPath,
        IReadOnlyCollection<string>? excludedSourcePaths = null)
    {
        try
        {
            SetBusy(true);
            SetStatus("Dateien werden erkannt...", 0);
            PreviewText = "Erkennung laeuft...";

            var detected = await _services.SeriesEpisodeMux.DetectFromSelectedVideoAsync(
                selectedVideoPath,
                HandleDetectionUpdate,
                excludedSourcePaths);
            _detectedMetadataGuess = new EpisodeMetadataGuess(
                detected.SeriesName,
                detected.SuggestedTitle,
                detected.SeasonNumber,
                detected.EpisodeNumber);
            SetStatus("TVDB-Metadaten werden abgeglichen...", 88);
            detected = await ApplyAutomaticMetadataAsync(detected);

            _detectionSeedPath = selectedVideoPath;
            MainVideoPath = detected.MainVideoPath;
            AudioDescriptionPath = detected.AudioDescriptionPath ?? string.Empty;
            _subtitlePaths = detected.SubtitlePaths.ToList();
            _attachmentPaths = detected.AttachmentPaths.ToList();
            _relatedEpisodeFilePaths = detected.RelatedFilePaths.ToList();
            _currentPlan = null;

            ApplyMetadataFields(() =>
            {
                SeriesName = detected.SeriesName;
                SeasonNumber = detected.SeasonNumber;
                EpisodeNumber = detected.EpisodeNumber;

                if (string.IsNullOrWhiteSpace(Title) || Title == _lastSuggestedTitle)
                {
                    Title = detected.SuggestedTitle;
                }
            });

            _lastSuggestedTitle = detected.SuggestedTitle;
            SetSuggestedOutputPath(detected.SuggestedOutputFilePath);
            ApplyManualCheckState(detected);
            OnPropertyChanged(nameof(SubtitleDisplayText));
            OnPropertyChanged(nameof(AttachmentDisplayText));

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
        Application.Current.Dispatcher.Invoke(() =>
        {
            SetStatus(update.StatusText, update.ProgressPercent);
            PreviewText = $"{update.StatusText}{Environment.NewLine}{Environment.NewLine}Bitte warten...";
        });
    }

    private static string BuildDetectionPreview(AutoDetectedEpisodeFiles detected)
    {
        var lines = new List<string>
        {
            "Dateien wurden automatisch erkannt. Mit 'Vorschau erzeugen' kannst du den mkvmerge-Aufruf pruefen.",
            $"Hauptquelle: {Path.GetFileName(detected.MainVideoPath)}",
            $"Erkannte Episode: {detected.SeriesName} - S{detected.SeasonNumber}E{detected.EpisodeNumber} - {detected.SuggestedTitle}"
        };

        if (detected.AdditionalVideoPaths.Count > 0)
        {
            lines.Add("Weitere Videospuren: " + string.Join(", ", detected.AdditionalVideoPaths.Select(Path.GetFileName)));
        }

        if (detected.AttachmentPaths.Count > 0)
        {
            lines.Add("Anhaenge: " + string.Join(", ", detected.AttachmentPaths.Select(Path.GetFileName)));
        }

        if (detected.Notes.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Hinweise:");
            lines.AddRange(detected.Notes.Select(note => "- " + note));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void ApplyManualCheckState(AutoDetectedEpisodeFiles detected)
    {
        _manualCheckFilePaths = detected.ManualCheckFilePaths.ToList();
        RequiresManualCheck = detected.RequiresManualCheck;
        if (!RequiresManualCheck)
        {
            _approvedReviewPath = null;
        }
        else if (!string.Equals(_approvedReviewPath, CurrentReviewTargetPath, StringComparison.OrdinalIgnoreCase))
        {
            _approvedReviewPath = null;
        }

        UpdateManualCheckText();
        OnPropertyChanged(nameof(ManualCheckButtonText));
    }

    private async Task RescanFromMainVideoAsync()
    {
        if (string.IsNullOrWhiteSpace(_mainVideoPath))
        {
            _dialogService.ShowError("Bitte zuerst ein Hauptvideo auswaehlen.");
            return;
        }

        await ApplyAutoDetectedFilesAsync(_mainVideoPath, _excludedSourcePaths);
    }

    private void SetAudioDescription(string path)
    {
        AudioDescriptionPath = path;
        _currentPlan = null;
        _approvedReviewPath = null;
        SchedulePlanSummaryRefresh();
    }

    private void ClearAudioDescription()
    {
        AudioDescriptionPath = string.Empty;
        _currentPlan = null;
        _approvedReviewPath = null;
        SchedulePlanSummaryRefresh();
    }

    private void SetSubtitles(IEnumerable<string> paths)
    {
        _subtitlePaths = paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        _currentPlan = null;
        OnPropertyChanged(nameof(SubtitleDisplayText));
        SchedulePlanSummaryRefresh();
    }

    private void ClearSubtitles()
    {
        _subtitlePaths = [];
        _currentPlan = null;
        OnPropertyChanged(nameof(SubtitleDisplayText));
        SchedulePlanSummaryRefresh();
    }

    private void SetAttachments(IEnumerable<string> paths)
    {
        _attachmentPaths = paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        _currentPlan = null;
        OnPropertyChanged(nameof(AttachmentDisplayText));
        SchedulePlanSummaryRefresh();
    }

    private void ClearAttachments()
    {
        _attachmentPaths = [];
        _currentPlan = null;
        OnPropertyChanged(nameof(AttachmentDisplayText));
        SchedulePlanSummaryRefresh();
    }

    private void SetOutputPath(string path)
    {
        OutputPath = path;
        _currentPlan = null;
        RefreshOutputTargetStatus();
        SchedulePlanSummaryRefresh();
    }

    private void SetSuggestedOutputPath(string path)
    {
        _outputPathWasManuallyChanged = false;
        SetOutputPath(path);
    }

    private void UpdateSuggestedOutputPathIfAutomatic()
    {
        if (_outputPathWasManuallyChanged)
        {
            return;
        }

        SetSuggestedOutputPath(BuildSuggestedOutputPath());
    }

    private void RefreshOutputTargetStatus()
    {
        if (string.IsNullOrWhiteSpace(_outputPath))
        {
            OutputTargetStatusText = string.Empty;
            return;
        }

        if (File.Exists(_outputPath))
        {
            OutputTargetStatusText = _outputPath.StartsWith(SeriesArchiveService.ArchiveRootDirectory, StringComparison.OrdinalIgnoreCase)
                ? "Am Ziel liegt bereits eine Archiv-MKV. Bei Vorschau oder Mux wird geprueft, ob etwas fehlt oder ersetzt werden muss."
                : "Die Zieldatei existiert bereits und wuerde beim Mux ueberschrieben.";
            return;
        }

        OutputTargetStatusText = _outputPath.StartsWith(SeriesArchiveService.ArchiveRootDirectory, StringComparison.OrdinalIgnoreCase)
            ? "Das Archivziel ist noch frei. Die Episode kann direkt dort einsortiert werden."
            : "Die Zieldatei existiert noch nicht.";
    }

    private async Task SelectMainVideoAsync()
    {
        var path = _dialogService.SelectMainVideo(ResolveMainVideoInitialDirectory());
        if (!string.IsNullOrWhiteSpace(path))
        {
            _excludedSourcePaths.Clear();
            _approvedReviewPath = null;
            await ApplyAutoDetectedFilesAsync(path, _excludedSourcePaths);
        }
    }

    private async Task SelectAudioDescriptionAsync()
    {
        if (string.IsNullOrWhiteSpace(_mainVideoPath))
        {
            var selectedAudioDescriptionPath = _dialogService.SelectAudioDescription(ResolveMainVideoInitialDirectory());
            if (!string.IsNullOrWhiteSpace(selectedAudioDescriptionPath))
            {
                _excludedSourcePaths.Clear();
                _approvedReviewPath = null;
                await ApplyAutoDetectedFilesAsync(selectedAudioDescriptionPath, _excludedSourcePaths);
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
        if (string.IsNullOrWhiteSpace(_mainVideoPath))
        {
            _dialogService.ShowError("Bitte zuerst ein Hauptvideo auswaehlen.");
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
        if (string.IsNullOrWhiteSpace(_mainVideoPath))
        {
            _dialogService.ShowError("Bitte zuerst ein Hauptvideo auswaehlen.");
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
            _outputPathWasManuallyChanged = true;
            SetOutputPath(path);
        }
    }

    private async Task OpenTvdbLookupAsync()
    {
        var guess = _detectedMetadataGuess ?? new EpisodeMetadataGuess(
            string.IsNullOrWhiteSpace(SeriesName) ? "Unbekannte Serie" : SeriesName,
            string.IsNullOrWhiteSpace(Title) ? "Unbekannter Titel" : Title,
            SeasonNumber,
            EpisodeNumber);

        var dialog = new TvdbLookupWindow(_services.EpisodeMetadata, guess)
        {
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
                ?? Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (dialog.KeepLocalDetection)
        {
            ApplyLocalMetadataGuess();
            MarkMetadataAsReviewed("Lokale Erkennung wurde bewusst beibehalten.");
            SetStatus("Lokale Erkennung beibehalten", 100);
            PreviewText = "Lokale Metadaten beibehalten. Bitte bei Bedarf 'Vorschau erzeugen' erneut ausfuehren.";
            RefreshCommands();
            return;
        }

        if (dialog.SelectedEpisodeSelection is null)
        {
            return;
        }

        ApplyTvdbSelection(dialog.SelectedEpisodeSelection);
        MarkMetadataAsReviewed(
            $"TVDB manuell uebernommen: S{dialog.SelectedEpisodeSelection.SeasonNumber}E{dialog.SelectedEpisodeSelection.EpisodeNumber} - {dialog.SelectedEpisodeSelection.EpisodeTitle}");
        SetStatus("TVDB-Zuordnung uebernommen", 100);
        PreviewText = "TVDB-Metadaten uebernommen. Bitte bei Bedarf 'Vorschau erzeugen' erneut ausfuehren.";
        RefreshCommands();
        await Task.CompletedTask;
    }

    private void TestSelectedSources()
    {
        _ = ReviewSourcesAsync();
    }

    private async Task ReviewSourcesAsync()
    {
        while (RequiresManualCheck && !string.IsNullOrWhiteSpace(CurrentReviewTargetPath))
        {
            var reviewTargetPath = CurrentReviewTargetPath!;
            SetStatus("Pruefe Quelle...", ProgressValue);
            _dialogService.OpenFilesWithDefaultApp([reviewTargetPath]);

            var result = _dialogService.AskSourceReviewResult(
                Path.GetFileName(reviewTargetPath),
                canTryAlternative: true);

            if (result == MessageBoxResult.Cancel)
            {
                SetStatus("Quellenpruefung abgebrochen", ProgressValue);
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                _approvedReviewPath = reviewTargetPath;
                UpdateManualCheckText();
                OnPropertyChanged(nameof(ManualCheckButtonText));
                SetStatus("Quelle freigegeben", 100);
                return;
            }

            var detectionSeedPath = _detectionSeedPath ?? _mainVideoPath;
            if (string.IsNullOrWhiteSpace(detectionSeedPath))
            {
                _dialogService.ShowWarning("Hinweis", "Es konnte keine alternative Quelle ermittelt werden.");
                return;
            }

            var tentativeExclusions = new HashSet<string>(_excludedSourcePaths, StringComparer.OrdinalIgnoreCase)
            {
                reviewTargetPath
            };

            _approvedReviewPath = null;
            var updated = await ApplyAutoDetectedFilesAsync(detectionSeedPath, tentativeExclusions);
            if (!updated)
            {
                return;
            }

            _excludedSourcePaths.Clear();
            foreach (var excludedPath in tentativeExclusions)
            {
                _excludedSourcePaths.Add(excludedPath);
            }

            if (!RequiresManualCheck)
            {
                SetStatus("Auf alternative Quelle umgestellt", 100);
                return;
            }
        }
    }

    private void UpdateManualCheckText()
    {
        if (!RequiresManualCheck)
        {
            ManualCheckText = string.Empty;
            return;
        }

        ManualCheckText = IsManualCheckApproved
            ? "Die aktuell ausgewaehlte Quelle wurde bereits geprueft und freigegeben."
            : "Die aktuell ausgewaehlte Quelle ist pruefpflichtig. Bitte vor dem Muxen kurz pruefen und freigeben.";
    }

    private void ApplyTvdbSelection(TvdbEpisodeSelection selection)
    {
        ApplyMetadataFields(() =>
        {
            SeriesName = selection.TvdbSeriesName;
            SeasonNumber = selection.SeasonNumber;
            EpisodeNumber = selection.EpisodeNumber;
            Title = selection.EpisodeTitle;
        });

        _lastSuggestedTitle = selection.EpisodeTitle;
        UpdateSuggestedOutputPathIfAutomatic();
        RefreshOutputTargetStatus();
        SchedulePlanSummaryRefresh();
    }

    private void ApplyLocalMetadataGuess()
    {
        if (_detectedMetadataGuess is null)
        {
            return;
        }

        ApplyMetadataFields(() =>
        {
            SeriesName = _detectedMetadataGuess.SeriesName;
            SeasonNumber = _detectedMetadataGuess.SeasonNumber;
            EpisodeNumber = _detectedMetadataGuess.EpisodeNumber;
            Title = _detectedMetadataGuess.EpisodeTitle;
        });

        _lastSuggestedTitle = _detectedMetadataGuess.EpisodeTitle;
        UpdateSuggestedOutputPathIfAutomatic();
        RefreshOutputTargetStatus();
        SchedulePlanSummaryRefresh();
    }

    private async Task<AutoDetectedEpisodeFiles> ApplyAutomaticMetadataAsync(AutoDetectedEpisodeFiles detected)
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

        ApplyMetadataResolutionState(resolution);
        return detected;
    }

    private void ApplyMetadataResolutionState(EpisodeMetadataResolutionResult resolution)
    {
        MetadataStatusText = resolution.StatusText;
        RequiresMetadataReview = resolution.RequiresReview;
        IsMetadataReviewApproved = !resolution.RequiresReview;
        RefreshOutputTargetStatus();
        SchedulePlanSummaryRefresh();
    }

    private void MarkMetadataAsReviewed(string statusText)
    {
        MetadataStatusText = statusText;
        RequiresMetadataReview = false;
        IsMetadataReviewApproved = true;
        RefreshOutputTargetStatus();
        SchedulePlanSummaryRefresh();
    }

    private void HandleManualMetadataOverride()
    {
        if (_isApplyingMetadataState)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(MetadataStatusText) || RequiresMetadataReview)
        {
            MarkMetadataAsReviewed("Metadaten manuell angepasst.");
        }
    }

    private void ApplyMetadataFields(Action applyAction)
    {
        _isApplyingMetadataState = true;
        try
        {
            applyAction();
        }
        finally
        {
            _isApplyingMetadataState = false;
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

            if (_currentPlan.SkipMux)
            {
                SetStatus("Archiv bereits aktuell", 100);
                _dialogService.ShowInfo("Hinweis", _currentPlan.SkipReason ?? "Die Archivdatei ist bereits vollstaendig.");
                return;
            }

            if (RequiresManualCheck && !IsManualCheckApproved)
            {
                _dialogService.ShowWarning("Hinweis", "Diese Episode nutzt eine pruefpflichtige Quelle. Bitte zuerst 'Quelle pruefen' ausfuehren und die Quelle freigeben.");
                SetStatus("Freigabe der Quelle fehlt", 0);
                return;
            }

            if (RequiresMetadataReview && !IsMetadataReviewApproved)
            {
                _dialogService.ShowWarning("Hinweis", "Die TVDB-Zuordnung ist noch nicht freigegeben. Bitte zuerst 'TVDB pruefen' ausfuehren oder die Metadaten manuell korrigieren.");
                SetStatus("Freigabe der TVDB-Metadaten fehlt", 0);
                return;
            }

            if (!_dialogService.ConfirmMuxStart())
            {
                SetStatus("Abgebrochen", 0);
                return;
            }

            if (_currentPlan.WorkingCopy is not null)
            {
                if (_services.FileCopy.NeedsCopy(_currentPlan.WorkingCopy))
                {
                    if (!_dialogService.ConfirmArchiveCopy(_currentPlan.WorkingCopy))
                    {
                        SetStatus("Abgebrochen", 0);
                        return;
                    }

                    SetStatus("Kopiere Archivdatei...", 0);
                    await _services.FileCopy.CopyAsync(
                        _currentPlan.WorkingCopy,
                        (copiedBytes, totalBytes) =>
                        {
                            var progress = totalBytes <= 0
                                ? 0
                                : (int)Math.Round(copiedBytes * 100d / totalBytes);

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                SetStatus($"Kopiere Archivdatei... {progress}%", progress);
                            });
                        });
                }
                else
                {
                    SetStatus("Arbeitskopie bereits aktuell - uebernehme vorhandene Kopie...", 100);
                }
            }

            SetStatus("Muxing laeuft...", 0);
            var result = await _services.SeriesEpisodeMux.ExecuteAsync(_currentPlan, HandleMuxOutput, HandleMuxUpdate);

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
            _services.Cleanup.DeleteTemporaryFile(_currentPlan?.WorkingCopy?.DestinationFilePath);
            SetBusy(false);
        }
    }

    private async Task<SeriesEpisodeMuxPlan> BuildPlanAsync()
    {
        var request = new SeriesEpisodeMuxRequest(
            RequireValue(_mainVideoPath, "Bitte ein Hauptvideo auswaehlen."),
            _audioDescriptionPath,
            _subtitlePaths,
            _attachmentPaths,
            RequireValue(_outputPath, "Bitte eine Ausgabedatei waehlen."),
            RequireValue(_title.Trim(), "Bitte einen Dateititel eingeben."));

        return await _services.SeriesEpisodeMux.CreatePlanAsync(request);
    }

    private void RefreshOutputTargetStatusFromPlan(SeriesEpisodeMuxPlan plan)
    {
        if (plan.SkipMux)
        {
            OutputTargetStatusText = plan.SkipReason ?? "Die Archivdatei ist bereits vollstaendig.";
            return;
        }

        if (plan.WorkingCopy is not null)
        {
            OutputTargetStatusText = plan.WorkingCopy.IsReusable
                ? "Am Ziel liegt bereits eine Archiv-MKV. Eine aktuelle Arbeitskopie ist schon vorhanden und wird direkt weiterverwendet."
                : "Am Ziel liegt bereits eine Archiv-MKV. Vor dem Mux wird eine lokale Arbeitskopie erstellt und die fehlenden oder besseren Spuren werden eingearbeitet.";
            return;
        }

        RefreshOutputTargetStatus();
    }

    private void HandleMuxOutput(string line)
    {
        Application.Current.Dispatcher.Invoke(() => PreviewText += line + Environment.NewLine);
    }

    private void HandleMuxUpdate(MuxExecutionUpdate update)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var progressValue = update.ProgressPercent ?? ProgressValue;
            var statusText = update.ProgressPercent is int progressPercent
                ? $"Muxing laeuft... {progressPercent}%"
                : "Muxing laeuft...";

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
        var relatedFiles = BuildCleanupFileList(_relatedEpisodeFilePaths, plan);
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
                Application.Current.Dispatcher.Invoke(() =>
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
        return sourceFilePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(File.Exists)
            .Where(path => !string.Equals(path, plan.OutputFilePath, StringComparison.OrdinalIgnoreCase))
            .Where(path => plan.WorkingCopy is null
                || !string.Equals(path, plan.WorkingCopy.DestinationFilePath, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.StartsWith(SeriesArchiveService.ArchiveRootDirectory, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
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

        if (string.IsNullOrWhiteSpace(_mainVideoPath)
            || string.IsNullOrWhiteSpace(_outputPath)
            || string.IsNullOrWhiteSpace(_title))
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
