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
internal sealed partial class BatchMuxViewModel : INotifyPropertyChanged, IArchiveConfigurationAwareModule
{
    private const string DoneFolderName = "done";
    private const int AutomaticCompareProgressStart = 80;

    private readonly BatchModuleServices _services;
    private readonly IUserDialogService _dialogService;
    private readonly BufferedTextStore _logBuffer;
    private readonly IEpisodeReviewWorkflow _reviewWorkflow;
    private readonly BatchEpisodeCollectionController _episodeCollection;
    private readonly BatchExecutionRunner _executionRunner;
    private readonly BatchOperationController _operationController = new();
    private readonly EpisodePlanCache _planCache = new();
    private readonly DebouncedRefreshController _selectedPlanSummaryRefresh = new(TimeSpan.FromMilliseconds(200));
    private int _batchProgressGeneration;

    private string _sourceDirectory = string.Empty;
    private string _outputDirectory = string.Empty;
    private string _statusText = "Bereit";
    private string _logText = string.Empty;
    private int _progressValue;
    private bool _isBusy;
    private bool _isSelectedItemPlanSummaryFrozen;
    private BatchEpisodeItemViewModel? _selectedEpisodeItem;

    /// <summary>
    /// Initialisiert das Batch-Modul samt Hilfsdiensten, Review-Workflow und Batch-Kommandos.
    /// </summary>
    public BatchMuxViewModel(
        BatchModuleServices services,
        IUserDialogService dialogService,
        IAppSettingsDialogService? settingsDialog = null,
        IEpisodeReviewWorkflow? reviewWorkflow = null)
    {
        _services = services;
        _dialogService = dialogService;
        _reviewWorkflow = reviewWorkflow ?? new EpisodeReviewWorkflow(dialogService, services.EpisodeMetadata, settingsDialog);
        _episodeCollection = new BatchEpisodeCollectionController();
        _executionRunner = new BatchExecutionRunner(services.FileCopy, services.MuxWorkflow, services.Cleanup);
        _logBuffer = new BufferedTextStore(
            flush => _ = Application.Current.Dispatcher.BeginInvoke(flush),
            text => LogText = text,
            text => LogText += text);
        Action<Exception> unexpectedCommandErrorHandler = ex => _dialogService.ShowError($"Unerwarteter Fehler:\n\n{ex.Message}");

        _episodeCollection.CommandsChanged += RefreshCommands;
        _episodeCollection.SelectionStateChanged += RefreshSelectionCommands;
        _episodeCollection.OverviewChanged += RefreshOverview;
        _episodeCollection.AutomaticOutputInputsChanged += RefreshAutomaticOutputPath;
        _episodeCollection.SelectedItemPlanInputsChanged += ScheduleSelectedItemPlanSummaryRefresh;

        SelectSourceDirectoryCommand = new AsyncRelayCommand(SelectSourceDirectoryAsync, () => !_isBusy, unexpectedCommandErrorHandler);
        SelectOutputDirectoryCommand = new RelayCommand(SelectOutputDirectory, () => !_isBusy && !string.IsNullOrWhiteSpace(SourceDirectory));
        ScanDirectoryCommand = new AsyncRelayCommand(ScanDirectoryAsync, () => !_isBusy && !string.IsNullOrWhiteSpace(SourceDirectory), unexpectedCommandErrorHandler);
        SelectAllEpisodesCommand = new RelayCommand(SelectAllEpisodes, CanSelectAllEpisodes);
        DeselectAllEpisodesCommand = new RelayCommand(DeselectAllEpisodes, CanDeselectAllEpisodes);
        ToggleSelectedEpisodeSelectionCommand = new RelayCommand(ToggleSelectedEpisodeSelection, () => !_isBusy && SelectedEpisodeItem is not null);
        ReviewPendingSourcesCommand = new AsyncRelayCommand(ReviewPendingSourcesAsync, CanReviewPendingSources, unexpectedCommandErrorHandler);
        OpenSelectedSourcesCommand = new AsyncRelayCommand(OpenSelectedSourcesAsync, () => !_isBusy && HasSelectedVideoFiles(), unexpectedCommandErrorHandler);
        OpenSelectedAudioDescriptionCommand = new RelayCommand(OpenSelectedAudioDescription, () => !_isBusy && !string.IsNullOrWhiteSpace(SelectedEpisodeItem?.AudioDescriptionPath));
        OpenSelectedSubtitlesCommand = new RelayCommand(OpenSelectedSubtitles, () => !_isBusy && SelectedEpisodeItem?.SubtitlePaths.Count > 0);
        OpenSelectedAttachmentsCommand = new RelayCommand(OpenSelectedAttachments, () => !_isBusy && SelectedEpisodeItem?.AttachmentPaths.Count > 0);
        OpenSelectedOutputCommand = new RelayCommand(OpenSelectedOutput, () => !_isBusy && File.Exists(SelectedEpisodeItem?.OutputPath));
        ReviewSelectedMetadataCommand = new AsyncRelayCommand(ReviewSelectedMetadataAsync, () => !_isBusy && SelectedEpisodeItem is not null, unexpectedCommandErrorHandler);
        RefreshAllComparisonsCommand = new AsyncRelayCommand(RefreshAllComparisonsAsync, () => !_isBusy && EpisodeItems.Any(), unexpectedCommandErrorHandler);
        RedetectSelectedEpisodeCommand = new AsyncRelayCommand(RedetectSelectedEpisodeAsync, () => !_isBusy && SelectedEpisodeItem is not null, unexpectedCommandErrorHandler);
        EditSelectedAudioDescriptionCommand = new RelayCommand(EditSelectedAudioDescription, () => !_isBusy && SelectedEpisodeItem is not null);
        EditSelectedSubtitlesCommand = new RelayCommand(EditSelectedSubtitles, () => !_isBusy && SelectedEpisodeItem is not null);
        EditSelectedAttachmentsCommand = new RelayCommand(EditSelectedAttachments, () => !_isBusy && SelectedEpisodeItem is not null);
        EditSelectedOutputCommand = new RelayCommand(EditSelectedOutput, () => !_isBusy && SelectedEpisodeItem is not null);
        ApproveSelectedPlanReviewCommand = new RelayCommand(ApproveSelectedPlanReview, () => !_isBusy && SelectedEpisodeItem?.HasPendingPlanReview == true);
        RunBatchCommand = new AsyncRelayCommand(RunBatchAsync, () => !_isBusy && EpisodeItems.Any(item => item.IsSelected), unexpectedCommandErrorHandler);
        CancelBatchOperationCommand = new RelayCommand(CancelCurrentBatchOperation, () => CanCancelBatchOperation);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncRelayCommand SelectSourceDirectoryCommand { get; }
    public RelayCommand SelectOutputDirectoryCommand { get; }
    public AsyncRelayCommand ScanDirectoryCommand { get; }
    public RelayCommand SelectAllEpisodesCommand { get; }
    public RelayCommand DeselectAllEpisodesCommand { get; }
    public RelayCommand ToggleSelectedEpisodeSelectionCommand { get; }
    public AsyncRelayCommand ReviewPendingSourcesCommand { get; }
    public AsyncRelayCommand OpenSelectedSourcesCommand { get; }
    public RelayCommand OpenSelectedAudioDescriptionCommand { get; }
    public RelayCommand OpenSelectedSubtitlesCommand { get; }
    public RelayCommand OpenSelectedAttachmentsCommand { get; }
    public RelayCommand OpenSelectedOutputCommand { get; }
    public AsyncRelayCommand ReviewSelectedMetadataCommand { get; }
    public AsyncRelayCommand RefreshAllComparisonsCommand { get; }
    public AsyncRelayCommand RedetectSelectedEpisodeCommand { get; }
    public RelayCommand EditSelectedAudioDescriptionCommand { get; }
    public RelayCommand EditSelectedSubtitlesCommand { get; }
    public RelayCommand EditSelectedAttachmentsCommand { get; }
    public RelayCommand EditSelectedOutputCommand { get; }
    public RelayCommand ApproveSelectedPlanReviewCommand { get; }
    public AsyncRelayCommand RunBatchCommand { get; }
    public RelayCommand CancelBatchOperationCommand { get; }

    /// <summary>
    /// Hält die aktuell laufende, entkoppelte Aktualisierung der Detail-/Planansicht des ausgewählten Eintrags.
    /// Nach Abschluss oder Abbruch liefert die Eigenschaft wieder <see langword="null"/>.
    /// </summary>
    internal Task? SelectedItemPlanSummaryRefreshTask => _selectedPlanSummaryRefresh.CurrentTask;

    /// <summary>
    /// Rohsammlung aller Batch-Zeilen.
    /// </summary>
    public ObservableCollection<BatchEpisodeItemViewModel> EpisodeItems => _episodeCollection.Items;

    /// <summary>
    /// Gefilterte und sortierte Sicht auf <see cref="EpisodeItems"/> für die DataGrid-Bindung.
    /// </summary>
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

    public string ReviewPendingSourcesTooltip => "Öffnet nacheinander alle noch offenen Quellen-, TVDB- und Hinweisprüfungen der ausgewählten Episoden.";

    public string OpenSelectedSourcesTooltip => "Öffnet die aktiven Videoquellen des markierten Eintrags mit der Standardanwendung.";

    public string ReviewSelectedMetadataTooltip => "Öffnet den TVDB-Dialog für den aktuell ausgewählten Batch-Eintrag.";

    public string RefreshAllComparisonsTooltip => "Berechnet die Vergleiche für bereits vorhandene Bibliotheksziele erneut.";

    public string RedetectSelectedEpisodeTooltip => "Lässt die Dateierkennung für den markierten Eintrag erneut laufen.";

    public string RunBatchTooltip => "Startet das Muxing für alle ausgewählten Episoden. Offene Pflichtprüfungen werden vorher noch abgearbeitet.";

    public string CancelBatchOperationTooltip => _operationController.CurrentOperationKind switch
    {
        BatchOperationKind.Scan => "Bricht den laufenden Batch-Scan und die anschliessenden automatischen Vergleiche ab.",
        BatchOperationKind.Comparison => "Bricht den laufenden Archivvergleich nach dem Batch-Scan ab.",
        BatchOperationKind.Execution => "Bricht den laufenden Batch inklusive Arbeitskopien, Mux und Done-Cleanup kontrolliert ab.",
        _ => "Bricht den aktuell laufenden Batch-Vorgang ab."
    };

    public string BatchLogInfoText => "Das sichtbare Protokoll zeigt Scan und Batch-Lauf dieser Sitzung. Die gespeicherte Logdatei enthält jeweils nur den aktuellen Batch-Lauf.";

    public string OutputDirectoryHintText => _services.OutputPaths.BuildOutputRootOverrideHint(OutputDirectory) ?? string.Empty;

    public bool HasOutputDirectoryHint => !string.IsNullOrWhiteSpace(OutputDirectoryHintText);

    public bool CanCancelBatchOperation => _operationController.CanCancelCurrentOperation;

    public string CancelBatchOperationText => _operationController.CancelButtonText;

    /// <summary>
    /// Repräsentiert den aktuell im Grid markierten Eintrag und stößt bei Wechseln die
    /// abhängigen Command- und Detailaktualisierungen an.
    /// </summary>
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

    /// <summary>
    /// Reagiert auf einen globalen Wechsel des Archivwurzelpfads und richtet automatische Ziele,
    /// Archivstatus und Detailansicht der bereits geladenen Batch-Zeilen neu aus.
    /// </summary>
    public void HandleArchiveConfigurationChanged()
    {
        _planCache.Clear();

        using (EpisodeItemsView.DeferRefresh())
        {
            foreach (var item in EpisodeItems)
            {
                if (item.UsesAutomaticOutputPath)
                {
                    RefreshAutomaticOutputPath(item, refreshOutputTargetCollisions: false);
                }
                else
                {
                    item.RefreshOutputPathArchiveContext(_services.OutputPaths.IsArchivePath(item.OutputPath));
                }

                item.RefreshArchivePresence();
            }
        }

        RefreshOutputTargetCollisions(EpisodeItems);

        OnPropertyChanged(nameof(OutputDirectoryHintText));
        OnPropertyChanged(nameof(HasOutputDirectoryHint));

        if (SelectedEpisodeItem is not null)
        {
            ScheduleSelectedItemPlanSummaryRefresh();
        }

        RefreshCommands();
    }

    /// <summary>
    /// Kapselt den Busy-Zustand des Batch-Moduls und aktualisiert die davon abhängige UI.
    /// </summary>
    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        OnPropertyChanged(nameof(IsInteractive));
        RefreshCommands();
    }

    /// <summary>
    /// Hebt alle Kommandos neu aus, deren Ausführbarkeit sich durch Busy-Zustand,
    /// Auswahl, Scanergebnis oder Reviewstatus verändert haben könnte.
    /// </summary>
    private void RefreshCommands()
    {
        SelectSourceDirectoryCommand.RaiseCanExecuteChanged();
        SelectOutputDirectoryCommand.RaiseCanExecuteChanged();
        ScanDirectoryCommand.RaiseCanExecuteChanged();
        SelectAllEpisodesCommand.RaiseCanExecuteChanged();
        DeselectAllEpisodesCommand.RaiseCanExecuteChanged();
        ToggleSelectedEpisodeSelectionCommand.RaiseCanExecuteChanged();
        ReviewPendingSourcesCommand.RaiseCanExecuteChanged();
        OpenSelectedSourcesCommand.RaiseCanExecuteChanged();
        OpenSelectedAudioDescriptionCommand.RaiseCanExecuteChanged();
        OpenSelectedSubtitlesCommand.RaiseCanExecuteChanged();
        OpenSelectedAttachmentsCommand.RaiseCanExecuteChanged();
        OpenSelectedOutputCommand.RaiseCanExecuteChanged();
        ReviewSelectedMetadataCommand.RaiseCanExecuteChanged();
        RefreshAllComparisonsCommand.RaiseCanExecuteChanged();
        RedetectSelectedEpisodeCommand.RaiseCanExecuteChanged();
        EditSelectedAudioDescriptionCommand.RaiseCanExecuteChanged();
        EditSelectedSubtitlesCommand.RaiseCanExecuteChanged();
        EditSelectedAttachmentsCommand.RaiseCanExecuteChanged();
        EditSelectedOutputCommand.RaiseCanExecuteChanged();
        ApproveSelectedPlanReviewCommand.RaiseCanExecuteChanged();
        RunBatchCommand.RaiseCanExecuteChanged();
        CancelBatchOperationCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Aktualisiert nur die Batch-Kommandos, die direkt von der Menge ausgewählter Folgen abhängen.
    /// Diese abgespeckte Variante vermeidet unnötige WPF-Neuroutings beim Space-Toggle im Grid.
    /// </summary>
    private void RefreshSelectionCommands()
    {
        // Die Zeilenauswahl beeinflusst nur Batch-Aktionen, die über die Menge der aktivierten Folgen
        // entscheiden. Der DataGrid-Space-Command hängt dagegen an markierter Zeile und Busy-Zustand;
        // er darf sich nicht während seiner eigenen Ausführung per CanExecuteChanged neu routen.
        SelectAllEpisodesCommand.RaiseCanExecuteChanged();
        DeselectAllEpisodesCommand.RaiseCanExecuteChanged();
        ReviewPendingSourcesCommand.RaiseCanExecuteChanged();
        RunBatchCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Aktiviert die Sammelaktion auch dann, wenn unter einem Filter nur noch ausgeblendete
    /// Batch-Einträge ausgewählt werden könnten und deshalb erst der "auch versteckte Elemente"
    ///-Dialog den restlichen Aktionspfad freischaltet.
    /// </summary>
    private bool CanSelectAllEpisodes()
    {
        return !_isBusy
            && (_episodeCollection.HasUnselectedVisibleItems
                || (SelectedFilterMode.Key != BatchEpisodeFilterMode.All && _episodeCollection.HasUnselectedItems));
    }

    /// <summary>
    /// Aktiviert die Sammelaktion spiegelbildlich auch bei ausschließlich versteckt ausgewählten
    /// Einträgen, damit der Benutzer die globale Aktion unter aktivem Filter weiterhin erreicht.
    /// </summary>
    private bool CanDeselectAllEpisodes()
    {
        return !_isBusy
            && (_episodeCollection.HasSelectedVisibleItems
                || (SelectedFilterMode.Key != BatchEpisodeFilterMode.All && _episodeCollection.HasSelectedItems));
    }

    /// <summary>
    /// Entfernt alle geladenen Batch-Episoden und leert auch abhängige Hilfsstrukturen wie den Plan-Cache.
    /// </summary>
    private void ClearEpisodeItems()
    {
        _planCache.Clear();
        _episodeCollection.Clear();
        SelectedEpisodeItem = null;
    }

    /// <summary>
    /// Leert nach einem abgeschlossenen Batch bewusst die Episodenliste.
    /// So kann derselbe Lauf nicht versehentlich noch einmal gestartet werden, während
    /// Status- und Logzusammenfassung der gerade beendeten Sitzung sichtbar bleiben.
    /// </summary>
    internal void ResetCompletedBatchSession()
    {
        ClearEpisodeItems();
    }

    /// <summary>
    /// Setzt sichtbaren Statustext und Prozentfortschritt konsistent.
    /// </summary>
    private void SetStatus(string text, int progress)
    {
        StatusText = text;
        ProgressValue = Math.Clamp(progress, 0, 100);
    }

    /// <summary>
    /// Aktualisiert den Batch-Fortschritt auch dann sicher, wenn ein Worker-Callback nicht vom UI-Thread kommt.
    /// </summary>
    private void SetStatusFromAnyThread(string text, int progress)
    {
        DispatchBatchProgress(() => SetStatus(text, progress));
    }

    /// <summary>
    /// Führt verzögerte Fortschrittsupdates nur aus, solange sie noch zum aktuellen Batch-Vorgang gehören.
    /// </summary>
    private void DispatchBatchProgress(Action applyProgress)
    {
        var generation = Volatile.Read(ref _batchProgressGeneration);
        var dispatcher = Application.Current?.Dispatcher;
        void ApplyIfCurrent()
        {
            if (_operationController.CanCancelCurrentOperation
                && Volatile.Read(ref _batchProgressGeneration) == generation)
            {
                applyProgress();
            }
        }

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyIfCurrent();
            return;
        }

        _ = dispatcher.BeginInvoke(ApplyIfCurrent);
    }

    private void BeginBatchProgressScope()
    {
        Interlocked.Increment(ref _batchProgressGeneration);
    }

    private void InvalidateBatchProgressCallbacks()
    {
        Interlocked.Increment(ref _batchProgressGeneration);
    }

    /// <summary>
    /// Aktualisiert die aggregierten Zähler für Kopfbereich und Statusübersichten.
    /// </summary>
    private void RefreshOverview()
    {
        OnPropertyChanged(nameof(EpisodeCount));
        OnPropertyChanged(nameof(SelectedEpisodeCount));
        OnPropertyChanged(nameof(ExistingArchiveCount));
        OnPropertyChanged(nameof(PendingCheckCount));
    }

    /// <summary>
    /// Startet einen explizit abbrechbaren Batch-Vorgang und liefert dessen CancellationToken zurück.
    /// </summary>
    private CancellationToken BeginBatchOperation(BatchOperationKind operationKind)
    {
        BeginBatchProgressScope();
        var token = _operationController.Begin(operationKind);
        NotifyBatchOperationStateChanged();
        return token;
    }

    /// <summary>
    /// Markiert einen zuvor gestarteten Batch-Vorgang als abgeschlossen.
    /// </summary>
    private void CompleteBatchOperation(BatchOperationKind operationKind)
    {
        InvalidateBatchProgressCallbacks();
        _operationController.Complete(operationKind);
        NotifyBatchOperationStateChanged();
    }

    /// <summary>
    /// Wechselt die sichtbare Phase eines laufenden Batch-Vorgangs, ohne den CancellationToken
    /// zu ersetzen. So bleibt "Scan plus automatischer Archivvergleich" ein zusammenhängender
    /// abbrechbarer Ablauf.
    /// </summary>
    private void ChangeCurrentBatchOperationKind(BatchOperationKind operationKind)
    {
        _operationController.ChangeCurrentOperationKind(operationKind);
        NotifyBatchOperationStateChanged();
    }

    /// <summary>
    /// Schließt die aktuell laufende Batch-Operation unabhängig von ihrer sichtbaren Zwischenphase ab.
    /// </summary>
    private void CompleteCurrentBatchOperation()
    {
        InvalidateBatchProgressCallbacks();
        _operationController.CompleteCurrent();
        NotifyBatchOperationStateChanged();
    }

    /// <summary>
    /// Löst den Benutzerabbruch des aktuell laufenden Batch-Scans oder Batch-Laufs aus.
    /// </summary>
    private void CancelCurrentBatchOperation()
    {
        if (!_operationController.CancelCurrentOperation())
        {
            return;
        }

        InvalidateBatchProgressCallbacks();
        StatusText = _operationController.CurrentOperationKind switch
        {
            BatchOperationKind.Scan => "Batch-Scan wird abgebrochen...",
            BatchOperationKind.Comparison => "Archivvergleich wird abgebrochen...",
            BatchOperationKind.Execution => "Batch-Lauf wird abgebrochen...",
            _ => "Vorgang wird abgebrochen..."
        };

        NotifyBatchOperationStateChanged();
    }

    /// <summary>
    /// Aktualisiert alle UI-Eigenschaften und Kommandos, die vom Status des OperationControllers abhängen.
    /// </summary>
    private void NotifyBatchOperationStateChanged()
    {
        OnPropertyChanged(nameof(CancelBatchOperationText));
        OnPropertyChanged(nameof(CanCancelBatchOperation));
        OnPropertyChanged(nameof(CancelBatchOperationTooltip));
        RefreshCommands();
    }

    /// <summary>
    /// Friert die sichtbare Plan- und Nutzungsübersicht des aktuell ausgewählten Batch-Eintrags ein.
    /// Während des Batch-Laufs wird die Zieldatei aktiv geschrieben; automatische Neuplanungen
    /// gegen diesen Zwischenstand würden die Anzeige mit falschen Warnungen und Schein-Vergleichen
    /// überschreiben. Deshalb bleibt ab hier bewusst die letzte bestätigte Darstellung sichtbar.
    /// </summary>
    internal void FreezeSelectedItemPlanSummaryForExecution()
    {
        _isSelectedItemPlanSummaryFrozen = true;
        CancelSelectedItemPlanSummaryRefresh(invalidateInFlightRefreshes: true);
    }

    /// <summary>
    /// Hebt das Einfrieren der Detailübersicht nach dem Batch-Lauf wieder auf.
    /// </summary>
    internal void UnfreezeSelectedItemPlanSummaryAfterExecution()
    {
        _isSelectedItemPlanSummaryFrozen = false;
        if (SelectedEpisodeItem is not null)
        {
            ScheduleSelectedItemPlanSummaryRefresh();
        }
    }

    /// <summary>
    /// Standard-PropertyChanged-Helfer des ViewModels.
    /// </summary>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

