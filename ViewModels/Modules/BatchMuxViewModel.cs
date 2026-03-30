using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels.Commands;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Zentrales ViewModel des Batch-Moduls; die Teil-Dateien trennen Scan, Planung, Review und Ausführung.
/// </summary>
public sealed partial class BatchMuxViewModel : INotifyPropertyChanged, IArchiveConfigurationAwareModule
{
    private const string DoneFolderName = "done";
    private const int AutomaticCompareProgressStart = 80;
    private static readonly string[] PreferredDownloadsSubPath = ["MediathekView-latest-win", "Downloads"];

    private readonly AppServices _services;
    private readonly IUserDialogService _dialogService;
    private readonly BufferedTextStore _logBuffer;
    private readonly EpisodeReviewWorkflow _reviewWorkflow;
    private readonly BatchEpisodeCollectionController _episodeCollection;
    private readonly BatchExecutionRunner _executionRunner;
    private readonly BatchOperationController _operationController = new();
    private readonly EpisodePlanCache _planCache = new();

    private string _sourceDirectory = string.Empty;
    private string _outputDirectory = string.Empty;
    private string _statusText = "Bereit";
    private string _logText = string.Empty;
    private int _progressValue;
    private bool _isBusy;
    private BatchEpisodeItemViewModel? _selectedEpisodeItem;
    private int _selectedPlanSummaryVersion;
    private CancellationTokenSource? _selectedPlanSummaryRefreshCts;

    public BatchMuxViewModel(AppServices services, IUserDialogService dialogService)
    {
        _services = services;
        _dialogService = dialogService;
        _reviewWorkflow = new EpisodeReviewWorkflow(dialogService, services.EpisodeMetadata);
        _episodeCollection = new BatchEpisodeCollectionController();
        _executionRunner = new BatchExecutionRunner(services.FileCopy, services.MuxWorkflow, services.Cleanup);
        _logBuffer = new BufferedTextStore(
            flush => _ = Application.Current.Dispatcher.BeginInvoke(flush),
            text => LogText = text,
            text => LogText += text);

        _episodeCollection.CommandsChanged += RefreshCommands;
        _episodeCollection.OverviewChanged += RefreshOverview;
        _episodeCollection.AutomaticOutputInputsChanged += RefreshAutomaticOutputPath;
        _episodeCollection.SelectedItemPlanInputsChanged += ScheduleSelectedItemPlanSummaryRefresh;

        SelectSourceDirectoryCommand = new AsyncRelayCommand(SelectSourceDirectoryAsync, () => !_isBusy);
        SelectOutputDirectoryCommand = new RelayCommand(SelectOutputDirectory, () => !_isBusy && !string.IsNullOrWhiteSpace(SourceDirectory));
        ScanDirectoryCommand = new AsyncRelayCommand(ScanDirectoryAsync, () => !_isBusy && !string.IsNullOrWhiteSpace(SourceDirectory));
        SelectAllEpisodesCommand = new RelayCommand(SelectAllEpisodes, () => !_isBusy && EpisodeItems.Any(item => !item.IsSelected));
        DeselectAllEpisodesCommand = new RelayCommand(DeselectAllEpisodes, () => !_isBusy && EpisodeItems.Any(item => item.IsSelected));
        ReviewPendingSourcesCommand = new AsyncRelayCommand(ReviewPendingSourcesAsync, CanReviewPendingSources);
        OpenSelectedSourcesCommand = new RelayCommand(OpenSelectedSources, () => !_isBusy && SelectedEpisodeItem?.SourceFilePaths.Count > 0);
        ReviewSelectedMetadataCommand = new AsyncRelayCommand(ReviewSelectedMetadataAsync, () => !_isBusy && SelectedEpisodeItem is not null);
        RefreshAllComparisonsCommand = new AsyncRelayCommand(RefreshAllComparisonsAsync, () => !_isBusy && EpisodeItems.Any());
        RedetectSelectedEpisodeCommand = new AsyncRelayCommand(RedetectSelectedEpisodeAsync, () => !_isBusy && SelectedEpisodeItem is not null);
        EditSelectedAudioDescriptionCommand = new RelayCommand(EditSelectedAudioDescription, () => !_isBusy && SelectedEpisodeItem is not null);
        EditSelectedSubtitlesCommand = new RelayCommand(EditSelectedSubtitles, () => !_isBusy && SelectedEpisodeItem is not null);
        EditSelectedAttachmentsCommand = new RelayCommand(EditSelectedAttachments, () => !_isBusy && SelectedEpisodeItem is not null);
        EditSelectedOutputCommand = new RelayCommand(EditSelectedOutput, () => !_isBusy && SelectedEpisodeItem is not null);
        RunBatchCommand = new AsyncRelayCommand(RunBatchAsync, () => !_isBusy && EpisodeItems.Any(item => item.IsSelected));
        CancelBatchOperationCommand = new RelayCommand(CancelCurrentBatchOperation, () => CanCancelBatchOperation);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncRelayCommand SelectSourceDirectoryCommand { get; }
    public RelayCommand SelectOutputDirectoryCommand { get; }
    public AsyncRelayCommand ScanDirectoryCommand { get; }
    public RelayCommand SelectAllEpisodesCommand { get; }
    public RelayCommand DeselectAllEpisodesCommand { get; }
    public AsyncRelayCommand ReviewPendingSourcesCommand { get; }
    public RelayCommand OpenSelectedSourcesCommand { get; }
    public AsyncRelayCommand ReviewSelectedMetadataCommand { get; }
    public AsyncRelayCommand RefreshAllComparisonsCommand { get; }
    public AsyncRelayCommand RedetectSelectedEpisodeCommand { get; }
    public RelayCommand EditSelectedAudioDescriptionCommand { get; }
    public RelayCommand EditSelectedSubtitlesCommand { get; }
    public RelayCommand EditSelectedAttachmentsCommand { get; }
    public RelayCommand EditSelectedOutputCommand { get; }
    public AsyncRelayCommand RunBatchCommand { get; }
    public RelayCommand CancelBatchOperationCommand { get; }

    public ObservableCollection<BatchEpisodeItemViewModel> EpisodeItems => _episodeCollection.Items;
    public ICollectionView EpisodeItemsView => _episodeCollection.View;
    public IReadOnlyList<BatchEpisodeFilterOption> FilterModes => _episodeCollection.FilterModes;
    public IReadOnlyList<BatchEpisodeSortOption> SortModes => _episodeCollection.SortModes;

    public string SourceDirectory
    {
        get => _sourceDirectory;
        private set
        {
            _sourceDirectory = value;
            OnPropertyChanged();
        }
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        private set
        {
            _outputDirectory = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutputDirectoryHintText));
            OnPropertyChanged(nameof(HasOutputDirectoryHint));
        }
    }

    public BatchEpisodeFilterOption SelectedFilterMode
    {
        get => _episodeCollection.SelectedFilterMode;
        set
        {
            if (!_episodeCollection.SetFilterMode(value))
            {
                return;
            }

            OnPropertyChanged();
        }
    }

    public BatchEpisodeSortOption SelectedSortMode
    {
        get => _episodeCollection.SelectedSortMode;
        set
        {
            if (!_episodeCollection.SetSortMode(value))
            {
                return;
            }

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

    public string LogText
    {
        get => _logText;
        private set
        {
            _logText = value;
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

    public int EpisodeCount => _episodeCollection.EpisodeCount;

    public int SelectedEpisodeCount => _episodeCollection.SelectedEpisodeCount;

    public int ExistingArchiveCount => _episodeCollection.ExistingArchiveCount;

    public int PendingCheckCount => _episodeCollection.PendingCheckCount;

    public bool IsInteractive => !_isBusy;

    public string ScanDirectoryTooltip => "Scannt den Quellordner nach Episoden und erstellt Vorschläge für Quellen, Titel und Zielpfade.";

    public string ReviewPendingSourcesTooltip => "Öffnet nacheinander alle noch offenen Quellen- und TVDB-Prüfungen der ausgewählten Episoden.";

    public string OpenSelectedSourcesTooltip => "Prüft die Quelldateien des markierten Eintrags und erlaubt bei Bedarf eine alternative Quelle.";

    public string ReviewSelectedMetadataTooltip => "Öffnet den TVDB-Dialog für den aktuell ausgewählten Batch-Eintrag.";

    public string RefreshAllComparisonsTooltip => "Berechnet die Vergleiche für bereits vorhandene Bibliotheksziele erneut.";

    public string RedetectSelectedEpisodeTooltip => "Lässt die Dateierkennung für den markierten Eintrag erneut laufen.";

    public string RunBatchTooltip => "Startet das Muxing für alle ausgewählten Episoden. Offene Pflichtprüfungen werden vorher noch abgearbeitet.";

    public string CancelBatchOperationTooltip => _operationController.CurrentOperationKind switch
    {
        BatchOperationKind.Scan => "Bricht den laufenden Batch-Scan und die anschliessenden automatischen Vergleiche ab.",
        BatchOperationKind.Execution => "Bricht den laufenden Batch inklusive Arbeitskopien, Mux und Done-Cleanup kontrolliert ab.",
        _ => "Bricht den aktuell laufenden Batch-Vorgang ab."
    };

    public string BatchLogInfoText => "Das sichtbare Protokoll zeigt Scan und Batch-Lauf dieser Sitzung. Die gespeicherte Logdatei enthält jeweils nur den aktuellen Batch-Lauf.";

    public string OutputDirectoryHintText => _services.OutputPaths.BuildOutputRootOverrideHint(OutputDirectory) ?? string.Empty;

    public bool HasOutputDirectoryHint => !string.IsNullOrWhiteSpace(OutputDirectoryHintText);

    public bool CanCancelBatchOperation => _operationController.CanCancelCurrentOperation;

    public string CancelBatchOperationText => _operationController.CancelButtonText;

    public BatchEpisodeItemViewModel? SelectedEpisodeItem
    {
        get => _selectedEpisodeItem;
        set
        {
            if (_selectedEpisodeItem == value)
            {
                return;
            }

            _selectedEpisodeItem = value;
            _episodeCollection.SelectedItem = value;
            OnPropertyChanged();
            RefreshCommands();
            ScheduleSelectedItemPlanSummaryRefresh();
        }
    }

    public void HandleArchiveConfigurationChanged()
    {
        _planCache.Clear();

        using (EpisodeItemsView.DeferRefresh())
        {
            foreach (var item in EpisodeItems)
            {
                if (item.UsesAutomaticOutputPath)
                {
                    item.SetAutomaticOutputPath(BuildOutputPath(item));
                }

                item.RefreshArchivePresence();
            }
        }

        OnPropertyChanged(nameof(OutputDirectoryHintText));
        OnPropertyChanged(nameof(HasOutputDirectoryHint));

        if (SelectedEpisodeItem is not null)
        {
            ScheduleSelectedItemPlanSummaryRefresh();
        }

        RefreshCommands();
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        OnPropertyChanged(nameof(IsInteractive));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        SelectSourceDirectoryCommand.RaiseCanExecuteChanged();
        SelectOutputDirectoryCommand.RaiseCanExecuteChanged();
        ScanDirectoryCommand.RaiseCanExecuteChanged();
        SelectAllEpisodesCommand.RaiseCanExecuteChanged();
        DeselectAllEpisodesCommand.RaiseCanExecuteChanged();
        ReviewPendingSourcesCommand.RaiseCanExecuteChanged();
        OpenSelectedSourcesCommand.RaiseCanExecuteChanged();
        ReviewSelectedMetadataCommand.RaiseCanExecuteChanged();
        RefreshAllComparisonsCommand.RaiseCanExecuteChanged();
        RedetectSelectedEpisodeCommand.RaiseCanExecuteChanged();
        EditSelectedAudioDescriptionCommand.RaiseCanExecuteChanged();
        EditSelectedSubtitlesCommand.RaiseCanExecuteChanged();
        EditSelectedAttachmentsCommand.RaiseCanExecuteChanged();
        EditSelectedOutputCommand.RaiseCanExecuteChanged();
        RunBatchCommand.RaiseCanExecuteChanged();
        CancelBatchOperationCommand.RaiseCanExecuteChanged();
    }

    private void ClearEpisodeItems()
    {
        _planCache.Clear();
        _episodeCollection.Clear();
        SelectedEpisodeItem = null;
    }

    private void SetStatus(string text, int progress)
    {
        StatusText = text;
        ProgressValue = Math.Clamp(progress, 0, 100);
    }

    private void RefreshOverview()
    {
        OnPropertyChanged(nameof(EpisodeCount));
        OnPropertyChanged(nameof(SelectedEpisodeCount));
        OnPropertyChanged(nameof(ExistingArchiveCount));
        OnPropertyChanged(nameof(PendingCheckCount));
    }

    private CancellationToken BeginBatchOperation(BatchOperationKind operationKind)
    {
        var token = _operationController.Begin(operationKind);
        NotifyBatchOperationStateChanged();
        return token;
    }

    private void CompleteBatchOperation(BatchOperationKind operationKind)
    {
        _operationController.Complete(operationKind);
        NotifyBatchOperationStateChanged();
    }

    private void CancelCurrentBatchOperation()
    {
        if (!_operationController.CancelCurrentOperation())
        {
            return;
        }

        StatusText = _operationController.CurrentOperationKind switch
        {
            BatchOperationKind.Scan => "Batch-Scan wird abgebrochen...",
            BatchOperationKind.Execution => "Batch-Lauf wird abgebrochen...",
            _ => "Vorgang wird abgebrochen..."
        };

        NotifyBatchOperationStateChanged();
    }

    private void NotifyBatchOperationStateChanged()
    {
        OnPropertyChanged(nameof(CancelBatchOperationText));
        OnPropertyChanged(nameof(CanCancelBatchOperation));
        OnPropertyChanged(nameof(CancelBatchOperationTooltip));
        RefreshCommands();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}


