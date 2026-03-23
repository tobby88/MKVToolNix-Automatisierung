using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

internal sealed class BatchEpisodeCollectionController : IDisposable
{
    private readonly ObservableCollection<BatchEpisodeItemViewModel> _items = [];
    private readonly ICollectionView _view;
    private bool _suppressCollectionChanged;
    private bool _viewRefreshPending;
    private string _selectedFilterMode = "Alle";
    private string _selectedSortMode = "Dateiname";

    public BatchEpisodeCollectionController()
    {
        _items.CollectionChanged += Items_CollectionChanged;
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = FilterEpisodeItem;
        ApplyViewConfiguration();
    }

    public event Action? CommandsChanged;
    public event Action? OverviewChanged;
    public event Action<BatchEpisodeItemViewModel>? TitleForMuxChanged;
    public event Action? SelectedItemPlanInputsChanged;

    public ObservableCollection<BatchEpisodeItemViewModel> Items => _items;

    public ICollectionView View => _view;

    public IReadOnlyList<string> FilterModes { get; } =
    [
        "Alle",
        "Nur offen",
        "Nur neu",
        "Nur vorhanden",
        "Nur Fehler"
    ];

    public IReadOnlyList<string> SortModes { get; } =
    [
        "Dateiname",
        "Prüfung zuerst",
        "Status zuerst",
        "Neu zuerst"
    ];

    public string SelectedFilterMode => _selectedFilterMode;

    public string SelectedSortMode => _selectedSortMode;

    public BatchEpisodeItemViewModel? SelectedItem { get; set; }

    public int EpisodeCount => _items.Count;

    public int SelectedEpisodeCount => _items.Count(item => item.IsSelected);

    public int ExistingArchiveCount => _items.Count(item => item.ArchiveStateText == "vorhanden");

    public int PendingCheckCount => _items.Count(item => item.IsSelected && item.HasPendingChecks);

    public bool SetFilterMode(string value)
    {
        if (_selectedFilterMode == value)
        {
            return false;
        }

        _selectedFilterMode = value;
        RequestViewRefresh();
        return true;
    }

    public bool SetSortMode(string value)
    {
        if (_selectedSortMode == value)
        {
            return false;
        }

        _selectedSortMode = value;
        ApplyViewConfiguration();
        return true;
    }

    public void Reset(IEnumerable<BatchEpisodeItemViewModel> items)
    {
        _suppressCollectionChanged = true;
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
            _suppressCollectionChanged = false;
        }

        CommandsChanged?.Invoke();
        OverviewChanged?.Invoke();
    }

    public void Clear()
    {
        Reset([]);
        SelectedItem = null;
    }

    public void SelectAll()
    {
        foreach (var item in _items)
        {
            item.IsSelected = true;
        }
    }

    public void DeselectAll()
    {
        foreach (var item in _items)
        {
            item.IsSelected = false;
        }
    }

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

        return _selectedFilterMode switch
        {
            "Nur offen" => episode.HasPendingChecks,
            "Nur neu" => episode.ArchiveStateText == "neu",
            "Nur vorhanden" => episode.ArchiveStateText == "vorhanden",
            "Nur Fehler" => episode.Status.StartsWith("Fehler", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private void ApplyViewConfiguration()
    {
        using (_view.DeferRefresh())
        {
            _view.SortDescriptions.Clear();

            switch (_selectedSortMode)
            {
                case "Prüfung zuerst":
                    _view.SortDescriptions.Add(new SortDescription(nameof(BatchEpisodeItemViewModel.HasPendingChecks), ListSortDirection.Descending));
                    _view.SortDescriptions.Add(new SortDescription(nameof(BatchEpisodeItemViewModel.MainVideoFileName), ListSortDirection.Ascending));
                    break;
                case "Status zuerst":
                    _view.SortDescriptions.Add(new SortDescription(nameof(BatchEpisodeItemViewModel.StatusSortKey), ListSortDirection.Ascending));
                    _view.SortDescriptions.Add(new SortDescription(nameof(BatchEpisodeItemViewModel.MainVideoFileName), ListSortDirection.Ascending));
                    break;
                case "Neu zuerst":
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
        if (_suppressCollectionChanged)
        {
            return;
        }

        CommandsChanged?.Invoke();
        OverviewChanged?.Invoke();
    }

    private void EpisodeItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        CommandsChanged?.Invoke();

        if (e.PropertyName is nameof(BatchEpisodeItemViewModel.TitleForMux)
            && sender is BatchEpisodeItemViewModel titleItem)
        {
            TitleForMuxChanged?.Invoke(titleItem);
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
            nameof(BatchEpisodeItemViewModel.HasPendingChecks) => _selectedFilterMode == "Nur offen" || _selectedSortMode == "Prüfung zuerst",
            nameof(BatchEpisodeItemViewModel.Status)
                or nameof(BatchEpisodeItemViewModel.StatusSortKey) => _selectedFilterMode == "Nur Fehler" || _selectedSortMode == "Status zuerst",
            nameof(BatchEpisodeItemViewModel.OutputPath)
                or nameof(BatchEpisodeItemViewModel.ArchiveStateText)
                or nameof(BatchEpisodeItemViewModel.ArchiveSortKey) => _selectedFilterMode is "Nur neu" or "Nur vorhanden" || _selectedSortMode == "Neu zuerst",
            nameof(BatchEpisodeItemViewModel.MainVideoPath)
                or nameof(BatchEpisodeItemViewModel.MainVideoFileName) => _selectedSortMode is "Dateiname" or "Prüfung zuerst" or "Status zuerst" or "Neu zuerst",
            _ => false
        };
    }

    private static bool ShouldRefreshOverview(string? propertyName)
    {
        return propertyName is null
            or nameof(BatchEpisodeItemViewModel.IsSelected)
            or nameof(BatchEpisodeItemViewModel.Status)
            or nameof(BatchEpisodeItemViewModel.RequiresManualCheck)
            or nameof(BatchEpisodeItemViewModel.IsMetadataReviewApproved)
            or nameof(BatchEpisodeItemViewModel.RequiresMetadataReview)
            or nameof(BatchEpisodeItemViewModel.OutputPath);
    }

    private static bool ShouldRefreshSelectedItemPlanSummary(string? propertyName)
    {
        return propertyName is null
            or nameof(BatchEpisodeItemViewModel.TitleForMux)
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

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            _view.Refresh();
            return;
        }

        _viewRefreshPending = true;
        _ = dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _viewRefreshPending = false;
            _view.Refresh();
        }));
    }
}
