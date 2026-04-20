using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.ViewModels.Commands;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Verwaltet Scan, Vorschau und Ausfuehrung des Download-Sortiermoduls.
/// </summary>
internal sealed class DownloadSortViewModel : INotifyPropertyChanged
{
    private readonly DownloadSortModuleServices _services;
    private readonly IUserDialogService _dialogService;
    private readonly ObservableCollection<DownloadSortItemViewModel> _items = [];
    private readonly ObservableCollection<string> _targetFolderOptions = [];

    private IReadOnlyList<DownloadSortFolderRenamePlan> _currentFolderRenames = [];
    private DownloadSortItemViewModel? _selectedItem;
    private string _sourceDirectory;
    private string _statusText = "Bereit";
    private string _logText = string.Empty;
    private int _progressValue;
    private bool _isBusy;

    public DownloadSortViewModel(
        DownloadSortModuleServices services,
        IUserDialogService dialogService)
    {
        _services = services;
        _dialogService = dialogService;
        _sourceDirectory = PreferredDownloadDirectoryHelper.GetPreferredMediathekDownloadsDirectory();
        Action<Exception> unexpectedCommandErrorHandler = ex => _dialogService.ShowError($"Unerwarteter Fehler:\n\n{ex.Message}");

        SelectSourceDirectoryCommand = new AsyncRelayCommand(SelectSourceDirectoryAsync, () => !_isBusy, unexpectedCommandErrorHandler);
        ScanCommand = new AsyncRelayCommand(ScanAsync, CanScan, unexpectedCommandErrorHandler);
        SelectAllSortableCommand = new RelayCommand(SelectAllSortable, () => !_isBusy && Items.Any(item => DownloadSortItemStates.IsSortable(item.State) && !item.IsSelected));
        DeselectAllCommand = new RelayCommand(DeselectAll, () => !_isBusy && Items.Any(item => item.IsSelected));
        ToggleSelectedItemSelectionCommand = new RelayCommand(ToggleSelectedItemSelection, () => !_isBusy && SelectedItem is not null);
        ApplyTargetFolderToMatchingItemsCommand = new RelayCommand(ApplySelectedTargetFolderToMatchingItems, CanApplySelectedTargetFolderToMatchingItems);
        RunSortCommand = new AsyncRelayCommand(RunSortAsync, CanRunSort, unexpectedCommandErrorHandler);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncRelayCommand SelectSourceDirectoryCommand { get; }

    public AsyncRelayCommand ScanCommand { get; }

    public RelayCommand SelectAllSortableCommand { get; }

    public RelayCommand DeselectAllCommand { get; }

    public RelayCommand ToggleSelectedItemSelectionCommand { get; }

    public RelayCommand ApplyTargetFolderToMatchingItemsCommand { get; }

    public AsyncRelayCommand RunSortCommand { get; }

    public ObservableCollection<DownloadSortItemViewModel> Items => _items;

    public ObservableCollection<string> TargetFolderOptions => _targetFolderOptions;

    public DownloadSortItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (_selectedItem == value)
            {
                return;
            }

            _selectedItem = value;
            OnPropertyChanged();
            ToggleSelectedItemSelectionCommand.RaiseCanExecuteChanged();
            ApplyTargetFolderToMatchingItemsCommand.RaiseCanExecuteChanged();
        }
    }

    public string SourceDirectory
    {
        get => _sourceDirectory;
        private set
        {
            if (_sourceDirectory == value)
            {
                return;
            }

            _sourceDirectory = value;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged();
        }
    }

    public string LogText
    {
        get => _logText;
        private set
        {
            if (_logText == value)
            {
                return;
            }

            _logText = value;
            OnPropertyChanged();
        }
    }

    public int ProgressValue
    {
        get => _progressValue;
        private set
        {
            if (_progressValue == value)
            {
                return;
            }

            _progressValue = value;
            OnPropertyChanged();
        }
    }

    public bool IsInteractive => !_isBusy;

    public int ItemCount => Items.Count;

    public int SelectedCount => Items.Count(item => item.IsSelected);

    public int ReadyCount => Items.Count(item => item.State == DownloadSortItemState.Ready);

    public int ReplacementCount => Items.Count(item => item.State == DownloadSortItemState.ReadyWithReplacement);

    public int ReviewCount => Items.Count(item => item.State == DownloadSortItemState.NeedsReview);

    public int ConflictCount => Items.Count(item => item.State == DownloadSortItemState.Conflict);

    public int DefectiveCount => Items.Count(item => item.State == DownloadSortItemState.Defective);

    public int RenameCount => _currentFolderRenames.Count;

    public string SummaryText => ItemCount == 0
        ? "Keine losen Download-Dateien erkannt."
        : $"{ItemCount} Paket(e), {ReadyCount} bereit{BuildReplacementSummarySegment()}{BuildDefectiveSummarySegment()}, {ReviewCount} pruefen, {ConflictCount} Konflikt(e), {RenameCount} sichere Ordner-Umbenennung(en).";

    public string ScanTooltip => "Analysiert lose Dateien in der Wurzel des MediathekView-Downloadordners und schlaegt Serienordner vor.";

    public string RunSortTooltip => "Fuehrt die ausgewaehlten Ordnerumbenennungen und Dateiverschiebungen aus und scannt danach erneut.";

    private async Task SelectSourceDirectoryAsync()
    {
        var initialDirectory = Directory.Exists(SourceDirectory)
            ? SourceDirectory
            : PreferredDownloadDirectoryHelper.GetPreferredMediathekDownloadsDirectory();
        var selectedDirectory = _dialogService.SelectFolder(
            "MediathekView-Downloadordner auswählen",
            initialDirectory);
        if (string.IsNullOrWhiteSpace(selectedDirectory))
        {
            return;
        }

        SourceDirectory = selectedDirectory;
        await ScanAsync();
    }

    private async Task ScanAsync()
    {
        if (!CanScan())
        {
            return;
        }

        await ScanCoreAsync(resetLog: true);
    }

    private async Task ScanCoreAsync(bool resetLog)
    {
        try
        {
            SetBusy(true);
            await ScanCoreWithoutBusyAsync(resetLog);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyScanResult(DownloadSortScanResult scanResult)
    {
        foreach (var existingItem in Items)
        {
            existingItem.PropertyChanged -= ItemOnPropertyChanged;
        }

        _currentFolderRenames = scanResult.FolderRenames;
        Items.Clear();
        RefreshTargetFolderOptions(scanResult);

        foreach (var candidate in scanResult.Items)
        {
            var item = new DownloadSortItemViewModel(candidate);
            item.PropertyChanged += ItemOnPropertyChanged;
            Items.Add(item);
        }

        SelectedItem = Items.FirstOrDefault();

        var logLines = new List<string>();
        if (_currentFolderRenames.Count > 0)
        {
            logLines.AddRange(_currentFolderRenames.Select(plan =>
                $"ORDNER-HINWEIS: '{plan.CurrentFolderName}' wird bei Bedarf zu '{plan.TargetFolderName}' vereinheitlicht."));
        }

        if (Items.Count == 0)
        {
            logLines.Add("Keine losen Download-Dateien in der Wurzel gefunden.");
        }

        if (logLines.Count > 0)
        {
            AppendLog(logLines);
        }

        RefreshSummaryAndCommands();
    }

    private async Task RunSortAsync()
    {
        var selectedSortableItems = Items
            .Where(item => item.IsSelected && DownloadSortItemStates.IsSortable(item.State))
            .ToList();
        if (selectedSortableItems.Count == 0)
        {
            _dialogService.ShowWarning("Einsortieren", "Es sind keine einsortierbaren Einträge ausgewählt.");
            return;
        }

        try
        {
            SetBusy(true);
            StatusText = "Sortiere Dateien ein...";
            ProgressValue = 30;

            var requests = selectedSortableItems
                .Select(item => new DownloadSortMoveRequest(item.DisplayName, item.FilePaths, item.TargetFolderName))
                .ToList();

            var applyResult = await Task.Run(() => _services.DownloadSort.Apply(SourceDirectory, requests, _currentFolderRenames));
            AppendLog(applyResult.LogLines);
            if (applyResult.LogLines.Any(line => line.StartsWith("FEHLER:", StringComparison.OrdinalIgnoreCase)))
            {
                _dialogService.ShowWarning(
                    "Einsortieren",
                    "Einige Dateien oder Ordner konnten nicht verschoben werden. Der Lauf wurde fortgesetzt; Details stehen im Protokoll.");
            }

            ProgressValue = 85;
            StatusText = $"Sortieren abgeschlossen: {applyResult.MovedGroupCount} Paket(e), {applyResult.MovedFileCount} Datei(en), {applyResult.RenamedFolderCount} Ordner.";

            await ScanCoreWithoutBusyAsync(resetLog: false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SelectAllSortable()
    {
        foreach (var item in Items.Where(item => DownloadSortItemStates.IsSortable(item.State)))
        {
            item.IsSelected = true;
        }

        RefreshSummaryAndCommands();
    }

    private void DeselectAll()
    {
        foreach (var item in Items.Where(item => item.IsSelected))
        {
            item.IsSelected = false;
        }

        RefreshSummaryAndCommands();
    }

    private void ToggleSelectedItemSelection()
    {
        if (_isBusy || SelectedItem is not DownloadSortItemViewModel item)
        {
            return;
        }

        item.IsSelected = !item.IsSelected;
    }

    private void ApplySelectedTargetFolderToMatchingItems()
    {
        var selectedItem = SelectedItem;
        if (selectedItem is null || string.IsNullOrWhiteSpace(selectedItem.InitialTargetFolderName))
        {
            return;
        }

        var appliedCount = 0;
        foreach (var item in Items.Where(item => ShouldApplyTargetFolderFromSelectedItem(selectedItem, item)))
        {
            item.TargetFolderName = selectedItem.TargetFolderName;
            appliedCount++;
        }

        if (appliedCount > 0)
        {
            StatusText = $"Zielordner auf {appliedCount} weitere Paket(e) übernommen.";
        }

        RefreshSummaryAndCommands();
    }

    private void ItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DownloadSortItemViewModel item)
        {
            return;
        }

        if (e.PropertyName is nameof(DownloadSortItemViewModel.TargetFolderName))
        {
            EnsureTargetFolderOption(item.TargetFolderName);
            if (item.State != DownloadSortItemState.Defective)
            {
                var evaluation = _services.DownloadSort.EvaluateTarget(
                    SourceDirectory,
                    item.FilePaths,
                    item.TargetFolderName,
                    _currentFolderRenames);
                item.ApplyEvaluation(evaluation);
            }
        }

        if (e.PropertyName is nameof(DownloadSortItemViewModel.TargetFolderName)
            or nameof(DownloadSortItemViewModel.IsSelected))
        {
            RefreshSummaryAndCommands();
        }
    }

    private bool CanApplySelectedTargetFolderToMatchingItems()
    {
        var selectedItem = SelectedItem;
        return !_isBusy
            && selectedItem is not null
            && !string.IsNullOrWhiteSpace(selectedItem.InitialTargetFolderName)
            && !string.IsNullOrWhiteSpace(selectedItem.TargetFolderName)
            && Items.Any(item => ShouldApplyTargetFolderFromSelectedItem(selectedItem, item));
    }

    private static bool ShouldApplyTargetFolderFromSelectedItem(
        DownloadSortItemViewModel selectedItem,
        DownloadSortItemViewModel candidate)
    {
        // Gruppiert absichtlich über den ursprünglichen Zielvorschlag, nicht über den aktuellen
        // Zielordner. Dadurch kann eine manuelle Korrektur von "Serie A" auf "Serie B" gesammelt
        // auf weitere falsch erkannte Einträge angewendet werden.
        return !ReferenceEquals(selectedItem, candidate)
            && string.Equals(
                candidate.InitialTargetFolderName,
                selectedItem.InitialTargetFolderName,
                StringComparison.OrdinalIgnoreCase)
            && !string.Equals(candidate.TargetFolderName, selectedItem.TargetFolderName, StringComparison.OrdinalIgnoreCase);
    }

    private bool CanScan()
    {
        return !_isBusy && Directory.Exists(SourceDirectory);
    }

    private bool CanRunSort()
    {
        return !_isBusy && Items.Any(item => item.IsSelected && DownloadSortItemStates.IsSortable(item.State));
    }

    private void SetBusy(bool isBusy)
    {
        if (_isBusy == isBusy)
        {
            return;
        }

        _isBusy = isBusy;
        OnPropertyChanged(nameof(IsInteractive));
        RefreshSummaryAndCommands();
    }

    private void RefreshSummaryAndCommands()
    {
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(ReadyCount));
        OnPropertyChanged(nameof(ReplacementCount));
        OnPropertyChanged(nameof(ReviewCount));
        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(DefectiveCount));
        OnPropertyChanged(nameof(RenameCount));
        OnPropertyChanged(nameof(SummaryText));

        SelectSourceDirectoryCommand.RaiseCanExecuteChanged();
        ScanCommand.RaiseCanExecuteChanged();
        SelectAllSortableCommand.RaiseCanExecuteChanged();
        DeselectAllCommand.RaiseCanExecuteChanged();
        ToggleSelectedItemSelectionCommand.RaiseCanExecuteChanged();
        ApplyTargetFolderToMatchingItemsCommand.RaiseCanExecuteChanged();
        RunSortCommand.RaiseCanExecuteChanged();
    }

    private void RefreshTargetFolderOptions(DownloadSortScanResult scanResult)
    {
        var options = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(SourceDirectory))
        {
            foreach (var folderName in Directory.GetDirectories(SourceDirectory)
                         .Select(Path.GetFileName)
                         .Where(folderName => !string.IsNullOrWhiteSpace(folderName))
                         .Cast<string>())
            {
                options.Add(folderName);
            }
        }

        foreach (var folderName in scanResult.Items.Select(item => item.SuggestedFolderName))
        {
            AddTargetFolderOption(options, folderName);
        }

        foreach (var renamePlan in scanResult.FolderRenames)
        {
            AddTargetFolderOption(options, renamePlan.CurrentFolderName);
            AddTargetFolderOption(options, renamePlan.TargetFolderName);
        }

        _targetFolderOptions.Clear();
        foreach (var option in options)
        {
            _targetFolderOptions.Add(option);
        }
    }

    private void EnsureTargetFolderOption(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)
            || _targetFolderOptions.Contains(folderName, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _targetFolderOptions.Add(folderName);
    }

    private static void AddTargetFolderOption(ISet<string> options, string? folderName)
    {
        if (!string.IsNullOrWhiteSpace(folderName))
        {
            options.Add(folderName);
        }
    }

    private void AppendLog(IEnumerable<string> lines)
    {
        var materialized = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
        if (materialized.Count == 0)
        {
            return;
        }

        LogText = string.IsNullOrWhiteSpace(LogText)
            ? string.Join(Environment.NewLine, materialized)
            : LogText + Environment.NewLine + string.Join(Environment.NewLine, materialized);
    }

    private async Task ScanCoreWithoutBusyAsync(bool resetLog)
    {
        if (resetLog)
        {
            LogText = string.Empty;
        }

        StatusText = "Analysiere lose Mediathek-Dateien...";
        ProgressValue = 15;

        IProgress<DownloadSortScanProgress> scanProgress = new Progress<DownloadSortScanProgress>(HandleDownloadSortScanProgress);
        var scanResult = await Task.Run(() => _services.DownloadSort.Scan(SourceDirectory, scanProgress.Report));

        ApplyScanResult(scanResult);
        ProgressValue = 100;
        StatusText = ItemCount == 0
            ? "Keine losen Download-Dateien gefunden"
            : $"Scan abgeschlossen: {ReadyCount} bereit{BuildReplacementSummarySegment()}{BuildDefectiveSummarySegment()}, {ReviewCount} pruefen, {ConflictCount} Konflikt(e)";
    }

    private void HandleDownloadSortScanProgress(DownloadSortScanProgress progress)
    {
        StatusText = progress.StatusText;
        ProgressValue = progress.ProgressPercent;
    }

    private string BuildReplacementSummarySegment()
    {
        return ReplacementCount == 0
            ? string.Empty
            : $", {ReplacementCount} ersetzen";
    }

    private string BuildDefectiveSummarySegment()
    {
        return DefectiveCount == 0
            ? string.Empty
            : $", {DefectiveCount} defekt";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
