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
internal sealed partial class SingleEpisodeMuxViewModel : EpisodeEditModel, IArchiveConfigurationAwareModule
{
    private const int DetectionProgressStageEnd = 80;
    private const int MetadataProgressValue = 88;
    private const int InitialPlanProgressValue = 94;

    private readonly SingleEpisodeModuleServices _services;
    private readonly IUserDialogService _dialogService;
    private readonly BufferedTextStore _previewOutputBuffer;
    private readonly IEpisodeReviewWorkflow _reviewWorkflow;
    private readonly EpisodePlanCache _planCache = new();
    private readonly DebouncedRefreshController _planSummaryRefresh = new(TimeSpan.FromMilliseconds(250));
    private CancellationTokenSource? _currentOperationCts;

    private string _previewText = string.Empty;
    private string _statusText = "Bereit";
    private int _progressValue;
    private bool _isBusy;
    private string _lastSuggestedTitle = string.Empty;
    private bool _isApplyingSharedState;
    private string _outputTargetStatusText = string.Empty;
    private string _planRefreshProblemText = string.Empty;
    private SingleEpisodeExecutionStatusKind _executionStatusKind = SingleEpisodeExecutionStatusKind.Ready;
    private SeriesEpisodeMuxPlan? _currentPlan;
    private int _detectionProgressVersion;

    public SingleEpisodeMuxViewModel(
        SingleEpisodeModuleServices services,
        IUserDialogService dialogService,
        IAppSettingsDialogService? settingsDialog = null,
        IEpisodeReviewWorkflow? reviewWorkflow = null)
    {
        _services = services;
        _dialogService = dialogService;
        _reviewWorkflow = reviewWorkflow ?? new EpisodeReviewWorkflow(dialogService, services.EpisodeMetadata, settingsDialog);
        _previewOutputBuffer = new BufferedTextStore(
            flush => _ = Application.Current.Dispatcher.BeginInvoke(flush),
            text => PreviewText = text,
            text => PreviewText += text);
        Action<Exception> unexpectedCommandErrorHandler = ex => _dialogService.ShowError($"Unerwarteter Fehler:\n\n{ex.Message}");

        SelectMainVideoCommand = new AsyncRelayCommand(SelectMainVideoAsync, () => !_isBusy, unexpectedCommandErrorHandler);
        SelectAudioDescriptionCommand = new AsyncRelayCommand(SelectAudioDescriptionAsync, () => !_isBusy, unexpectedCommandErrorHandler);
        SelectSubtitlesCommand = new RelayCommand(SelectSubtitles, () => !_isBusy && !string.IsNullOrWhiteSpace(MainVideoPath));
        SelectAttachmentCommand = new RelayCommand(SelectAttachments, () => !_isBusy && !string.IsNullOrWhiteSpace(MainVideoPath));
        OpenMainVideoCommand = new RelayCommand(OpenMainVideo, () => !_isBusy && !string.IsNullOrWhiteSpace(MainVideoPath));
        OpenAudioDescriptionCommand = new RelayCommand(OpenAudioDescription, () => !_isBusy && !string.IsNullOrWhiteSpace(AudioDescriptionPath));
        OpenSubtitlesCommand = new RelayCommand(OpenSubtitles, () => !_isBusy && SubtitlePaths.Count > 0);
        OpenAttachmentsCommand = new RelayCommand(OpenAttachments, () => !_isBusy && AttachmentPaths.Count > 0);
        OpenOutputCommand = new RelayCommand(OpenOutput, () => !_isBusy && File.Exists(OutputPath));
        SelectOutputCommand = new RelayCommand(SelectOutput, () => !_isBusy);
        RescanCommand = new AsyncRelayCommand(RescanFromMainVideoAsync, () => !_isBusy && !string.IsNullOrWhiteSpace(MainVideoPath), unexpectedCommandErrorHandler);
        OpenTvdbLookupCommand = new AsyncRelayCommand(OpenTvdbLookupAsync, () => !_isBusy && !string.IsNullOrWhiteSpace(MainVideoPath), unexpectedCommandErrorHandler);
        TestSelectedSourcesCommand = new AsyncRelayCommand(ReviewSourcesAsync, () => !_isBusy && ManualCheckFilePaths.Count > 0, unexpectedCommandErrorHandler);
        ApprovePlanReviewCommand = new RelayCommand(ApprovePendingPlanReview, () => !_isBusy && HasPendingPlanReview);
        CreatePreviewCommand = new AsyncRelayCommand(CreatePreviewAsync, () => !_isBusy, unexpectedCommandErrorHandler);
        ExecuteMuxCommand = new AsyncRelayCommand(ExecuteMuxAsync, () => !_isBusy, unexpectedCommandErrorHandler);
        CancelCurrentOperationCommand = new RelayCommand(CancelCurrentOperation, () => CanCancelCurrentOperation);
    }

    public AsyncRelayCommand SelectMainVideoCommand { get; }
    public AsyncRelayCommand SelectAudioDescriptionCommand { get; }
    public RelayCommand SelectSubtitlesCommand { get; }
    public RelayCommand SelectAttachmentCommand { get; }
    public RelayCommand OpenMainVideoCommand { get; }
    public RelayCommand OpenAudioDescriptionCommand { get; }
    public RelayCommand OpenSubtitlesCommand { get; }
    public RelayCommand OpenAttachmentsCommand { get; }
    public RelayCommand OpenOutputCommand { get; }
    public RelayCommand SelectOutputCommand { get; }
    public AsyncRelayCommand RescanCommand { get; }
    public AsyncRelayCommand OpenTvdbLookupCommand { get; }
    public AsyncRelayCommand TestSelectedSourcesCommand { get; }
    public RelayCommand ApprovePlanReviewCommand { get; }
    public AsyncRelayCommand CreatePreviewCommand { get; }
    public AsyncRelayCommand ExecuteMuxCommand { get; }
    public RelayCommand CancelCurrentOperationCommand { get; }

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

    /// <summary>
    /// Kompakter Laufstatus für den Einzelmodus; entspricht funktional dem Status-Badge im Batch.
    /// </summary>
    public SingleEpisodeExecutionStatusKind ExecutionStatusKind
    {
        get => _executionStatusKind;
        private set
        {
            if (_executionStatusKind == value)
            {
                return;
            }

            _executionStatusKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExecutionStatusBadgeText));
            OnPropertyChanged(nameof(ExecutionStatusBadgeBackground));
            OnPropertyChanged(nameof(ExecutionStatusBadgeBorderBrush));
            OnPropertyChanged(nameof(ExecutionStatusTooltip));
            OnPropertyChanged(nameof(HasSingleEpisodeStatusBand));
        }
    }

    public string ExecutionStatusBadgeText => EpisodeEditTextBuilder.BuildSingleExecutionStatusText(ExecutionStatusKind);

    public string ExecutionStatusBadgeBackground => EpisodeUiStyleBuilder.BuildSingleExecutionStatusBadgeBackground(ExecutionStatusKind);

    public string ExecutionStatusBadgeBorderBrush => EpisodeUiStyleBuilder.BuildSingleExecutionStatusBadgeBorderBrush(ExecutionStatusKind);

    public string ExecutionStatusTooltip => EpisodeEditTextBuilder.BuildSingleExecutionStatusTooltip(ExecutionStatusKind, StatusText);

    public bool HasSingleEpisodeStatusBand => HasPlanSummary
        || ExecutionStatusKind != SingleEpisodeExecutionStatusKind.Ready
        || !string.IsNullOrWhiteSpace(MainVideoPath)
        || !string.IsNullOrWhiteSpace(OutputPath);

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

    public string CreatePreviewButtonTooltip => "Erstellt den geplanten MKVToolNix-Aufruf und zeigt die Details an, ohne eine MKV zu schreiben.";

    public string ExecuteMuxButtonTooltip => "Startet die aktuelle Planung mit dem passenden MKVToolNix-Werkzeug.";

    public bool CanCancelCurrentOperation => _currentOperationCts is { IsCancellationRequested: false };

    public string CancelCurrentOperationTooltip => CanCancelCurrentOperation
        ? "Bricht die laufende Vorschau-, Kopier- oder Mux-Aktion ab."
        : "Derzeit läuft keine abbrechbare Einzelaktion.";

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
        OpenMainVideoCommand.RaiseCanExecuteChanged();
        OpenAudioDescriptionCommand.RaiseCanExecuteChanged();
        OpenSubtitlesCommand.RaiseCanExecuteChanged();
        OpenAttachmentsCommand.RaiseCanExecuteChanged();
        OpenOutputCommand.RaiseCanExecuteChanged();
        SelectOutputCommand.RaiseCanExecuteChanged();
        RescanCommand.RaiseCanExecuteChanged();
        OpenTvdbLookupCommand.RaiseCanExecuteChanged();
        TestSelectedSourcesCommand.RaiseCanExecuteChanged();
        ApprovePlanReviewCommand.RaiseCanExecuteChanged();
        CreatePreviewCommand.RaiseCanExecuteChanged();
        ExecuteMuxCommand.RaiseCanExecuteChanged();
        CancelCurrentOperationCommand.RaiseCanExecuteChanged();
    }

    private CancellationTokenSource BeginCurrentOperation()
    {
        _currentOperationCts?.Cancel();
        _currentOperationCts?.Dispose();
        _currentOperationCts = new CancellationTokenSource();
        NotifyCurrentOperationChanged();
        return _currentOperationCts;
    }

    private void CompleteCurrentOperation(CancellationTokenSource operationSource)
    {
        if (!ReferenceEquals(_currentOperationCts, operationSource))
        {
            operationSource.Dispose();
            return;
        }

        _currentOperationCts.Dispose();
        _currentOperationCts = null;
        NotifyCurrentOperationChanged();
    }

    private void CancelCurrentOperation()
    {
        if (_currentOperationCts is null || _currentOperationCts.IsCancellationRequested)
        {
            return;
        }

        _currentOperationCts.Cancel();
        SetStatus("Abbruch angefordert...", ProgressValue);
        NotifyCurrentOperationChanged();
    }

    private void NotifyCurrentOperationChanged()
    {
        OnPropertyChanged(nameof(CanCancelCurrentOperation));
        OnPropertyChanged(nameof(CancelCurrentOperationTooltip));
        RefreshCommands();
    }

    private void SetStatus(string text, int progressValue)
    {
        StatusText = text;
        ProgressValue = Math.Clamp(progressValue, 0, 100);
        OnPropertyChanged(nameof(ExecutionStatusTooltip));
    }

    private void SetExecutionStatus(SingleEpisodeExecutionStatusKind statusKind)
    {
        ExecutionStatusKind = statusKind;
    }

    private int BeginDetectionProgressSession()
    {
        return Interlocked.Increment(ref _detectionProgressVersion);
    }

    private void CompleteDetectionProgressSession(int sessionVersion)
    {
        Interlocked.CompareExchange(ref _detectionProgressVersion, sessionVersion + 1, sessionVersion);
    }

    private bool IsDetectionProgressSessionCurrent(int sessionVersion)
    {
        return Volatile.Read(ref _detectionProgressVersion) == sessionVersion;
    }

    internal static int ScaleDetectionProgressForOverallProgress(int progressPercent)
    {
        var normalizedPercent = Math.Clamp(progressPercent, 0, 100);
        return (int)Math.Round(DetectionProgressStageEnd * (normalizedPercent / 100d));
    }

    private void ResetPreviewOutputBuffer(string initialText)
    {
        _previewOutputBuffer.Reset(initialText);
    }

    private void ApprovePendingPlanReview()
    {
        ApprovePlanReview();
        RefreshCommands();
    }

    protected override void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        switch (propertyName)
        {
            case nameof(MainVideoPath):
                base.OnPropertyChanged(nameof(AudioDescriptionButtonText));
                OpenMainVideoCommand.RaiseCanExecuteChanged();
                base.OnPropertyChanged(nameof(HasSingleEpisodeStatusBand));
                break;
            case nameof(AudioDescriptionPath):
                OpenAudioDescriptionCommand.RaiseCanExecuteChanged();
                break;
            case nameof(SubtitlePaths):
                base.OnPropertyChanged(nameof(SubtitleDisplayText));
                OpenSubtitlesCommand.RaiseCanExecuteChanged();
                break;
            case nameof(AttachmentPaths):
                base.OnPropertyChanged(nameof(AttachmentDisplayText));
                OpenAttachmentsCommand.RaiseCanExecuteChanged();
                break;
            case nameof(OutputPath):
            case nameof(ArchiveState):
                OpenOutputCommand.RaiseCanExecuteChanged();
                base.OnPropertyChanged(nameof(HasSingleEpisodeStatusBand));
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
                base.OnPropertyChanged(nameof(HasSingleEpisodeStatusBand));
                break;
            case nameof(HasPendingPlanReview):
            case nameof(IsPlanReviewApproved):
                ApprovePlanReviewCommand.RaiseCanExecuteChanged();
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
