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

    private IReadOnlyList<DownloadSortFolderRenamePlan> _currentFolderRenames = [];
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
        SelectAllReadyCommand = new RelayCommand(SelectAllReady, () => !_isBusy && Items.Any(item => item.State == DownloadSortItemState.Ready && !item.IsSelected));
        DeselectAllCommand = new RelayCommand(DeselectAll, () => !_isBusy && Items.Any(item => item.IsSelected));
        RunSortCommand = new AsyncRelayCommand(RunSortAsync, CanRunSort, unexpectedCommandErrorHandler);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncRelayCommand SelectSourceDirectoryCommand { get; }

    public AsyncRelayCommand ScanCommand { get; }

    public RelayCommand SelectAllReadyCommand { get; }

    public RelayCommand DeselectAllCommand { get; }

    public AsyncRelayCommand RunSortCommand { get; }

    public ObservableCollection<DownloadSortItemViewModel> Items => _items;

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

    public int ReviewCount => Items.Count(item => item.State == DownloadSortItemState.NeedsReview);

    public int ConflictCount => Items.Count(item => item.State == DownloadSortItemState.Conflict);

    public int RenameCount => _currentFolderRenames.Count;

    public string SummaryText => ItemCount == 0
        ? "Keine losen Download-Dateien erkannt."
        : $"{ItemCount} Paket(e), {ReadyCount} bereit, {ReviewCount} pruefen, {ConflictCount} Konflikt(e), {RenameCount} sichere Ordner-Umbenennung(en).";

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

        foreach (var candidate in scanResult.Items)
        {
            var item = new DownloadSortItemViewModel(candidate);
            item.PropertyChanged += ItemOnPropertyChanged;
            Items.Add(item);
        }

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
        var selectedReadyItems = Items
            .Where(item => item.IsSelected && item.State == DownloadSortItemState.Ready)
            .ToList();
        if (selectedReadyItems.Count == 0)
        {
            _dialogService.ShowWarning("Downloads", "Es sind keine einsortierbaren Einträge ausgewählt.");
            return;
        }

        try
        {
            SetBusy(true);
            StatusText = "Sortiere Downloads...";
            ProgressValue = 30;

            var requests = selectedReadyItems
                .Select(item => new DownloadSortMoveRequest(item.DisplayName, item.FilePaths, item.TargetFolderName))
                .ToList();

            var applyResult = await Task.Run(() => _services.DownloadSort.Apply(SourceDirectory, requests, _currentFolderRenames));
            AppendLog(applyResult.LogLines);
            ProgressValue = 85;
            StatusText = $"Sortieren abgeschlossen: {applyResult.MovedGroupCount} Paket(e), {applyResult.MovedFileCount} Datei(en), {applyResult.RenamedFolderCount} Ordner.";

            await ScanCoreWithoutBusyAsync(resetLog: false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SelectAllReady()
    {
        foreach (var item in Items.Where(item => item.State == DownloadSortItemState.Ready))
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

    private void ItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DownloadSortItemViewModel item)
        {
            return;
        }

        if (e.PropertyName is nameof(DownloadSortItemViewModel.TargetFolderName))
        {
            var evaluation = _services.DownloadSort.EvaluateTarget(
                SourceDirectory,
                item.FilePaths,
                item.TargetFolderName,
                _currentFolderRenames);
            item.ApplyEvaluation(evaluation);
        }

        if (e.PropertyName is nameof(DownloadSortItemViewModel.TargetFolderName)
            or nameof(DownloadSortItemViewModel.IsSelected))
        {
            RefreshSummaryAndCommands();
        }
    }

    private bool CanScan()
    {
        return !_isBusy && Directory.Exists(SourceDirectory);
    }

    private bool CanRunSort()
    {
        return !_isBusy && Items.Any(item => item.IsSelected && item.State == DownloadSortItemState.Ready);
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
        OnPropertyChanged(nameof(ReviewCount));
        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(RenameCount));
        OnPropertyChanged(nameof(SummaryText));

        SelectSourceDirectoryCommand.RaiseCanExecuteChanged();
        ScanCommand.RaiseCanExecuteChanged();
        SelectAllReadyCommand.RaiseCanExecuteChanged();
        DeselectAllCommand.RaiseCanExecuteChanged();
        RunSortCommand.RaiseCanExecuteChanged();
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

        StatusText = "Analysiere lose Downloads...";
        ProgressValue = 15;

        var scanResult = await Task.Run(() => _services.DownloadSort.Scan(SourceDirectory));

        ApplyScanResult(scanResult);
        ProgressValue = 100;
        StatusText = ItemCount == 0
            ? "Keine losen Download-Dateien gefunden"
            : $"Scan abgeschlossen: {ReadyCount} bereit, {ReviewCount} pruefen, {ConflictCount} Konflikt(e)";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
