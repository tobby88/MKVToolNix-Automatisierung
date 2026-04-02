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

/// <summary>
/// Zentrales ViewModel des Einzelmodus; die Partial-Dateien trennen Auswahl/Erkennung und Ausführung.
/// </summary>
public sealed partial class SingleEpisodeMuxViewModel : EpisodeEditModel, IArchiveConfigurationAwareModule
{
    private static readonly string[] PreferredDownloadsSubPath = ["MediathekView-latest-win", "Downloads"];

    private readonly AppServices _services;
    private readonly IUserDialogService _dialogService;
    private readonly BufferedTextStore _previewOutputBuffer;
    private readonly IEpisodeReviewWorkflow _reviewWorkflow;
    private readonly EpisodePlanCache _planCache = new();
    private CancellationTokenSource? _planSummaryRefreshCts;

    private string _previewText = string.Empty;
    private string _statusText = "Bereit";
    private int _progressValue;
    private bool _isBusy;
    private string _lastSuggestedTitle = string.Empty;
    private bool _isApplyingSharedState;
    private string _outputTargetStatusText = string.Empty;
    private string _planRefreshProblemText = string.Empty;
    private SeriesEpisodeMuxPlan? _currentPlan;
    private int _planSummaryVersion;

    public SingleEpisodeMuxViewModel(
        AppServices services,
        IUserDialogService dialogService,
        IEpisodeReviewWorkflow? reviewWorkflow = null)
    {
        _services = services;
        _dialogService = dialogService;
        _reviewWorkflow = reviewWorkflow ?? new EpisodeReviewWorkflow(dialogService, services.EpisodeMetadata);
        _previewOutputBuffer = new BufferedTextStore(
            flush => _ = Application.Current.Dispatcher.BeginInvoke(flush),
            text => PreviewText = text,
            text => PreviewText += text);

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

    public ManualCheckBadgeState ManualCheckBadgeState => HasPendingManualCheck
        ? ManualCheckBadgeState.Pending
        : ManualCheckBadgeState.Approved;

    public string ManualCheckBadgeText => EpisodeEditTextBuilder.BuildManualCheckBadgeText(ManualCheckBadgeState);

    public string ManualCheckBadgeBackground => EpisodeUiStyleBuilder.BuildManualCheckBadgeBackground(ManualCheckBadgeState);

    public string ManualCheckBadgeBorderBrush => EpisodeUiStyleBuilder.BuildManualCheckBadgeBorderBrush(ManualCheckBadgeState);

    public string ManualCheckBadgeTooltip => EpisodeEditTextBuilder.BuildManualCheckBadgeTooltip(ManualCheckBadgeState);

    public string ManualCheckButtonTooltip => "Prüft die aktuell ausgewählte Quelle und erlaubt bei Bedarf eine alternative Datei.";

    public bool HasMetadataStatus => !string.IsNullOrWhiteSpace(MetadataStatusText);

    public string MetadataActionButtonText => MetadataBadgeState switch
    {
        MetadataBadgeState.Pending => "TVDB prüfen",
        MetadataBadgeState.Approved => "TVDB anpassen",
        _ => "TVDB öffnen"
    };

    public MetadataBadgeState MetadataBadgeState => EpisodeUiStateResolver.ResolveMetadataBadgeState(
        HasPendingMetadataReview,
        IsMetadataReviewApproved);

    public string MetadataBadgeText => EpisodeEditTextBuilder.BuildMetadataBadgeText(MetadataBadgeState);

    public string MetadataBadgeBackground => EpisodeUiStyleBuilder.BuildMetadataBadgeBackground(MetadataBadgeState);

    public string MetadataBadgeBorderBrush => EpisodeUiStyleBuilder.BuildMetadataBadgeBorderBrush(MetadataBadgeState);

    public string MetadataBadgeTooltip => EpisodeEditTextBuilder.BuildMetadataBadgeTooltip(MetadataBadgeState);

    public string MetadataActionButtonTooltip => MetadataBadgeState switch
    {
        MetadataBadgeState.Pending => "Öffnet den TVDB-Dialog, um Serie und Episode zu prüfen und freizugeben.",
        MetadataBadgeState.Approved => "Öffnet den TVDB-Dialog, um die Zuordnung bei Bedarf manuell anzupassen.",
        _ => "Öffnet den TVDB-Dialog, um Zugangsdaten zu speichern oder eine Zuordnung manuell festzulegen."
    };

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

    public OutputTargetBadgeState OutputTargetBadgeState => string.IsNullOrWhiteSpace(OutputPath)
        ? OutputTargetBadgeState.Open
        : _services.OutputPaths.IsArchivePath(OutputPath)
            ? ArchiveState == EpisodeArchiveState.Existing
                ? OutputTargetBadgeState.InLibrary
                : OutputTargetBadgeState.NewForLibrary
            : OutputTargetBadgeState.CustomTarget;

    public string OutputTargetBadgeText => EpisodeEditTextBuilder.BuildOutputTargetBadgeText(OutputTargetBadgeState);

    public string OutputTargetBadgeBackground => EpisodeUiStyleBuilder.BuildOutputTargetBadgeBackground(OutputTargetBadgeState);

    public string OutputTargetBadgeBorderBrush => EpisodeUiStyleBuilder.BuildOutputTargetBadgeBorderBrush(OutputTargetBadgeState);

    public string OutputTargetBadgeTooltip => EpisodeEditTextBuilder.BuildOutputTargetBadgeTooltip(OutputTargetBadgeState);

    public bool HasPlanSummary => !string.IsNullOrWhiteSpace(PlanSummaryText);

    public string PlanRefreshProblemText
    {
        get => _planRefreshProblemText;
        private set
        {
            if (_planRefreshProblemText == value)
            {
                return;
            }

            _planRefreshProblemText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPlanRefreshProblem));
        }
    }

    public bool HasPlanRefreshProblem => !string.IsNullOrWhiteSpace(PlanRefreshProblemText);

    public string RescanButtonTooltip => "Erkennt Quellen, Begleitdateien und Ausgabeziel erneut ausgehend vom aktuellen Hauptvideo.";

    public string CreatePreviewButtonTooltip => "Erstellt den geplanten mkvmerge-Aufruf und zeigt die Details an, ohne eine MKV zu schreiben.";

    public string ExecuteMuxButtonTooltip => "Startet das eigentliche Muxing mit der aktuellen Planung.";

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
            case nameof(ArchiveState):
                base.OnPropertyChanged(nameof(OutputTargetBadgeState));
                base.OnPropertyChanged(nameof(OutputTargetBadgeText));
                base.OnPropertyChanged(nameof(OutputTargetBadgeBackground));
                base.OnPropertyChanged(nameof(OutputTargetBadgeBorderBrush));
                base.OnPropertyChanged(nameof(OutputTargetBadgeTooltip));
                break;
            case nameof(RequiresManualCheck):
            case nameof(IsManualCheckApproved):
            case nameof(ManualCheckText):
                base.OnPropertyChanged(nameof(ManualCheckButtonText));
                base.OnPropertyChanged(nameof(ManualCheckBadgeState));
                base.OnPropertyChanged(nameof(ManualCheckBadgeText));
                base.OnPropertyChanged(nameof(ManualCheckBadgeBackground));
                base.OnPropertyChanged(nameof(ManualCheckBadgeBorderBrush));
                base.OnPropertyChanged(nameof(ManualCheckBadgeTooltip));
                break;
            case nameof(MetadataStatusText):
                base.OnPropertyChanged(nameof(HasMetadataStatus));
                base.OnPropertyChanged(nameof(MetadataBadgeState));
                base.OnPropertyChanged(nameof(MetadataBadgeText));
                base.OnPropertyChanged(nameof(MetadataBadgeBackground));
                base.OnPropertyChanged(nameof(MetadataBadgeBorderBrush));
                base.OnPropertyChanged(nameof(MetadataBadgeTooltip));
                break;
            case nameof(RequiresMetadataReview):
            case nameof(IsMetadataReviewApproved):
                base.OnPropertyChanged(nameof(MetadataActionButtonText));
                base.OnPropertyChanged(nameof(MetadataActionButtonTooltip));
                base.OnPropertyChanged(nameof(MetadataBadgeState));
                base.OnPropertyChanged(nameof(MetadataBadgeText));
                base.OnPropertyChanged(nameof(MetadataBadgeBackground));
                base.OnPropertyChanged(nameof(MetadataBadgeBorderBrush));
                base.OnPropertyChanged(nameof(MetadataBadgeTooltip));
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
            InvalidateCurrentPlan();
            HandleManualMetadataOverride();
            UpdateSuggestedOutputPathIfAutomatic();
            SchedulePlanSummaryRefresh();
        }
    }
}
