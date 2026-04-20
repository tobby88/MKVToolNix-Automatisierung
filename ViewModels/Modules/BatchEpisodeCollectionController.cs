using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Filteroptionen für die Batch-Liste.
/// </summary>
internal enum BatchEpisodeFilterMode
{
    All,
    PendingChecks,
    NewOnly,
    ExistingOnly,
    ErrorsOnly
}

/// <summary>
/// Sortieroptionen für die Batch-Liste.
/// </summary>
internal enum BatchEpisodeSortMode
{
    FileName,
    PendingChecksFirst,
    StatusFirst,
    NewFirst
}

/// <summary>
/// UI-Objekt für einen auswählbaren Filtereintrag.
/// </summary>
internal sealed record BatchEpisodeFilterOption(BatchEpisodeFilterMode Key, string DisplayName);

/// <summary>
/// UI-Objekt für einen auswählbaren Sortiereintrag.
/// </summary>
internal sealed record BatchEpisodeSortOption(BatchEpisodeSortMode Key, string DisplayName);

/// <summary>
/// Kümmert sich ausschließlich um Filterung, Sortierung und Selection-Synchronisation der Batch-Liste.
/// </summary>
internal sealed class BatchEpisodeCollectionController : IDisposable
{
    private readonly ObservableCollection<BatchEpisodeItemViewModel> _items = [];
    private readonly ICollectionView _view;
    private bool _deferCollectionNotifications;
    private bool _viewRefreshPending;
    private BatchEpisodeFilterOption _selectedFilterMode;
    private BatchEpisodeSortOption _selectedSortMode;

    public BatchEpisodeCollectionController()
    {
        _items.CollectionChanged += Items_CollectionChanged;
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = FilterEpisodeItem;

        FilterModes =
        [
            new(BatchEpisodeFilterMode.All, "Alle"),
            new(BatchEpisodeFilterMode.PendingChecks, "Nur offen"),
            new(BatchEpisodeFilterMode.NewOnly, "Nur neu"),
            new(BatchEpisodeFilterMode.ExistingOnly, "Nur vorhanden"),
            new(BatchEpisodeFilterMode.ErrorsOnly, "Nur Fehler")
        ];

        SortModes =
        [
            new(BatchEpisodeSortMode.FileName, "Dateiname"),
            new(BatchEpisodeSortMode.PendingChecksFirst, "Prüfung zuerst"),
            new(BatchEpisodeSortMode.StatusFirst, "Status zuerst"),
            new(BatchEpisodeSortMode.NewFirst, "Neu zuerst")
        ];

        _selectedFilterMode = FilterModes[0];
        _selectedSortMode = SortModes[0];
        ApplyViewConfiguration();
    }

    public event Action? CommandsChanged;
    public event Action? SelectionStateChanged;
    public event Action? OverviewChanged;
    public event Action<BatchEpisodeItemViewModel>? AutomaticOutputInputsChanged;
    public event Action? SelectedItemPlanInputsChanged;

    public ObservableCollection<BatchEpisodeItemViewModel> Items => _items;

    public ICollectionView View => _view;

    public IReadOnlyList<BatchEpisodeFilterOption> FilterModes { get; }

    public IReadOnlyList<BatchEpisodeSortOption> SortModes { get; }

    public BatchEpisodeFilterOption SelectedFilterMode => _selectedFilterMode;

    public BatchEpisodeSortOption SelectedSortMode => _selectedSortMode;

    public BatchEpisodeItemViewModel? SelectedItem { get; set; }

    public int EpisodeCount => _items.Count;

    public int SelectedEpisodeCount => _items.Count(item => item.IsSelected);

    public int ExistingArchiveCount => _items.Count(item => item.ArchiveState == EpisodeArchiveState.Existing);

    public int PendingCheckCount => _items.Count(item => item.IsSelected && item.HasPendingChecks);

    public bool HasUnselectedVisibleItems => VisibleItems.Any(item => !item.IsSelected);

    public bool HasSelectedVisibleItems => VisibleItems.Any(item => item.IsSelected);

    public bool SetFilterMode(BatchEpisodeFilterOption? value)
    {
        if (value is null || EqualityComparer<BatchEpisodeFilterOption>.Default.Equals(_selectedFilterMode, value))
        {
            return false;
        }

        _selectedFilterMode = value;
        RequestViewRefresh();
        CommandsChanged?.Invoke();
        return true;
    }

    public bool SetSortMode(BatchEpisodeSortOption? value)
    {
        if (value is null || EqualityComparer<BatchEpisodeSortOption>.Default.Equals(_selectedSortMode, value))
        {
            return false;
        }

        _selectedSortMode = value;
        ApplyViewConfiguration();
        return true;
    }

    public void Reset(IEnumerable<BatchEpisodeItemViewModel> items)
    {
        _deferCollectionNotifications = true;
        try
        {
            foreach (var item in _items)
            {
                item.PropertyChanged -= EpisodeItem_PropertyChanged;
            }

            _items.Clear();

            foreach (var item in items)
            {
                item.PropertyChanged += EpisodeItem_PropertyChanged;
                _items.Add(item);
            }
        }
        finally
        {
            _deferCollectionNotifications = false;
        }

        CommandsChanged?.Invoke();
        OverviewChanged?.Invoke();
    }

    public void Clear()
    {
        Reset([]);
        SelectedItem = null;
    }

    public int SelectAllVisible()
    {
        var changedCount = 0;
        foreach (var item in VisibleItems.Where(item => !item.IsSelected))
        {
            item.IsSelected = true;
            changedCount++;
        }

        return changedCount;
    }

    public int SelectAllItems()
    {
        var changedCount = 0;
        foreach (var item in _items.Where(item => !item.IsSelected))
        {
            item.IsSelected = true;
            changedCount++;
        }

        return changedCount;
    }

    public int DeselectAllVisible()
    {
        var changedCount = 0;
        foreach (var item in VisibleItems.Where(item => item.IsSelected))
        {
            item.IsSelected = false;
            changedCount++;
        }

        return changedCount;
    }

    public int DeselectAllItems()
    {
        var changedCount = 0;
        foreach (var item in _items.Where(item => item.IsSelected))
        {
            item.IsSelected = false;
            changedCount++;
        }

        return changedCount;
    }

    // Auswahlaktionen muessen gegen die aktuell gesetzte Filterregel laufen, nicht gegen
    // den eventuell noch nicht neu gerenderten ICollectionView-Zustand. Sonst kann ein
    // direkt nach dem Filterwechsel geklicktes "Alle wählen" kurz wieder alle Einträge treffen.
    private IEnumerable<BatchEpisodeItemViewModel> VisibleItems => _items.Where(item => FilterEpisodeItem(item));

    public void Dispose()
    {
        _items.CollectionChanged -= Items_CollectionChanged;
        foreach (var item in _items)
        {
            item.PropertyChanged -= EpisodeItem_PropertyChanged;
        }
    }

    private bool FilterEpisodeItem(object item)
    {
        if (item is not BatchEpisodeItemViewModel episode)
        {
            return false;
        }

        return _selectedFilterMode.Key switch
        {
            BatchEpisodeFilterMode.PendingChecks => episode.HasPendingChecks,
            BatchEpisodeFilterMode.NewOnly => episode.ArchiveState == EpisodeArchiveState.New,
            BatchEpisodeFilterMode.ExistingOnly => episode.ArchiveState == EpisodeArchiveState.Existing,
            BatchEpisodeFilterMode.ErrorsOnly => episode.HasErrorStatus,
            _ => true
        };
    }

    private void ApplyViewConfiguration()
    {
        using (_view.DeferRefresh())
        {
            _view.SortDescriptions.Clear();

            switch (_selectedSortMode.Key)
            {
                case BatchEpisodeSortMode.PendingChecksFirst:
                    _view.SortDescriptions.Add(new SortDescription(nameof(BatchEpisodeItemViewModel.HasPendingChecks), ListSortDirection.Descending));
                    _view.SortDescriptions.Add(new SortDescription(nameof(BatchEpisodeItemViewModel.MainVideoFileName), ListSortDirection.Ascending));
                    break;
                case BatchEpisodeSortMode.StatusFirst:
                    _view.SortDescriptions.Add(new SortDescription(nameof(BatchEpisodeItemViewModel.StatusSortKey), ListSortDirection.Ascending));
                    _view.SortDescriptions.Add(new SortDescription(nameof(BatchEpisodeItemViewModel.MainVideoFileName), ListSortDirection.Ascending));
                    break;
                case BatchEpisodeSortMode.NewFirst:
                    _view.SortDescriptions.Add(new SortDescription(nameof(BatchEpisodeItemViewModel.ArchiveSortKey), ListSortDirection.Ascending));
                    _view.SortDescriptions.Add(new SortDescription(nameof(BatchEpisodeItemViewModel.MainVideoFileName), ListSortDirection.Ascending));
                    break;
                default:
                    _view.SortDescriptions.Add(new SortDescription(nameof(BatchEpisodeItemViewModel.MainVideoFileName), ListSortDirection.Ascending));
                    break;
            }
        }
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_deferCollectionNotifications)
        {
            return;
        }

        CommandsChanged?.Invoke();
        OverviewChanged?.Invoke();
    }

    private void EpisodeItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BatchEpisodeItemViewModel.IsSelected))
        {
            // Auswahländerungen sind extrem häufig und können direkt durch die DataGrid-Tastaturbedienung
            // ausgelöst werden. Sie aktualisieren nur die davon abhängigen Batch-Kommandos; ein vollständiger
            // Command-Refresh würde auch das gerade ausgeführte Space-Kommando neu bewerten und WPF kann
            // dadurch den Zellfokus nachgelagert verlieren.
            SelectionStateChanged?.Invoke();
        }
        else
        {
            CommandsChanged?.Invoke();
        }

        if (e.PropertyName is nameof(BatchEpisodeItemViewModel.SeriesName)
            or nameof(BatchEpisodeItemViewModel.SeasonNumber)
            or nameof(BatchEpisodeItemViewModel.EpisodeNumber)
            or nameof(BatchEpisodeItemViewModel.TitleForMux)
            && sender is BatchEpisodeItemViewModel titleItem)
        {
            AutomaticOutputInputsChanged?.Invoke(titleItem);
        }

        if (ShouldRefreshOverview(e.PropertyName))
        {
            OverviewChanged?.Invoke();
        }

        if (ShouldRefreshView(e.PropertyName))
        {
            RequestViewRefresh();
        }

        if (sender is BatchEpisodeItemViewModel item
            && ReferenceEquals(SelectedItem, item)
            && ShouldRefreshSelectedItemPlanSummary(e.PropertyName))
        {
            SelectedItemPlanInputsChanged?.Invoke();
        }
    }

    private bool ShouldRefreshView(string? propertyName)
    {
        if (propertyName is null)
        {
            return true;
        }

        return propertyName switch
        {
            nameof(BatchEpisodeItemViewModel.HasPendingChecks) => _selectedFilterMode.Key == BatchEpisodeFilterMode.PendingChecks || _selectedSortMode.Key == BatchEpisodeSortMode.PendingChecksFirst,
            nameof(BatchEpisodeItemViewModel.Status)
                or nameof(BatchEpisodeItemViewModel.StatusKind)
                or nameof(BatchEpisodeItemViewModel.HasErrorStatus)
                or nameof(BatchEpisodeItemViewModel.StatusSortKey) => _selectedFilterMode.Key == BatchEpisodeFilterMode.ErrorsOnly || _selectedSortMode.Key == BatchEpisodeSortMode.StatusFirst,
            nameof(BatchEpisodeItemViewModel.OutputPath)
                or nameof(BatchEpisodeItemViewModel.ArchiveState)
                or nameof(BatchEpisodeItemViewModel.ArchiveStateText)
                or nameof(BatchEpisodeItemViewModel.ArchiveSortKey) => _selectedFilterMode.Key is BatchEpisodeFilterMode.NewOnly or BatchEpisodeFilterMode.ExistingOnly || _selectedSortMode.Key == BatchEpisodeSortMode.NewFirst,
            nameof(BatchEpisodeItemViewModel.MainVideoPath)
                or nameof(BatchEpisodeItemViewModel.MainVideoFileName) => _selectedSortMode.Key is BatchEpisodeSortMode.FileName or BatchEpisodeSortMode.PendingChecksFirst or BatchEpisodeSortMode.StatusFirst or BatchEpisodeSortMode.NewFirst,
            _ => false
        };
    }

    private static bool ShouldRefreshOverview(string? propertyName)
    {
        return propertyName is null
            or nameof(BatchEpisodeItemViewModel.IsSelected)
            or nameof(BatchEpisodeItemViewModel.Status)
            or nameof(BatchEpisodeItemViewModel.StatusKind)
            or nameof(BatchEpisodeItemViewModel.HasErrorStatus)
            or nameof(BatchEpisodeItemViewModel.RequiresManualCheck)
            or nameof(BatchEpisodeItemViewModel.IsMetadataReviewApproved)
            or nameof(BatchEpisodeItemViewModel.RequiresMetadataReview)
            or nameof(BatchEpisodeItemViewModel.HasPendingChecks)
            or nameof(BatchEpisodeItemViewModel.ArchiveState)
            or nameof(BatchEpisodeItemViewModel.OutputPath);
    }

    private static bool ShouldRefreshSelectedItemPlanSummary(string? propertyName)
    {
        return propertyName is null
            or nameof(BatchEpisodeItemViewModel.SeriesName)
            or nameof(BatchEpisodeItemViewModel.SeasonNumber)
            or nameof(BatchEpisodeItemViewModel.EpisodeNumber)
            or nameof(BatchEpisodeItemViewModel.TitleForMux)
            or nameof(BatchEpisodeItemViewModel.HasPrimaryVideoSource)
            or nameof(BatchEpisodeItemViewModel.MainVideoPath)
            or nameof(BatchEpisodeItemViewModel.AudioDescriptionPath)
            or nameof(BatchEpisodeItemViewModel.SubtitlePaths)
            or nameof(BatchEpisodeItemViewModel.AttachmentPaths)
            or nameof(BatchEpisodeItemViewModel.OutputPath);
    }

    private void RequestViewRefresh()
    {
        if (_viewRefreshPending)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        if (dispatcher is null)
        {
            if (!HasOpenEditTransaction(_view))
            {
                _view.Refresh();
                CommandsChanged?.Invoke();
            }

            return;
        }

        _viewRefreshPending = true;
        _ = dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(ProcessPendingViewRefresh));
    }

    /// <summary>
    /// WPF-DataGrid hält Checkbox- und Zelländerungen kurz in einer Edit-Transaktion.
    /// Ein Refresh der CollectionView in genau diesem Moment löst die bekannte
    /// "Refresh ist während einer AddNew- oder EditItem-Transaktion nicht zulässig"-Exception aus.
    /// Deshalb verschieben wir den Refresh so lange, bis die laufende Bearbeitung sauber committed ist.
    /// </summary>
    internal static bool HasOpenEditTransaction(ICollectionView view)
    {
        return view is IEditableCollectionView editableCollectionView
            && (editableCollectionView.IsAddingNew || editableCollectionView.IsEditingItem);
    }

    private void ProcessPendingViewRefresh()
    {
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        if (dispatcher is not null && HasOpenEditTransaction(_view))
        {
            _ = dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(ProcessPendingViewRefresh));
            return;
        }

        _viewRefreshPending = false;
        _view.Refresh();
        CommandsChanged?.Invoke();
    }
}
