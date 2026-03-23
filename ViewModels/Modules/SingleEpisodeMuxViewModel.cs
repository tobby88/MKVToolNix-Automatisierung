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

public sealed partial class SingleEpisodeMuxViewModel : EpisodeEditModel
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


