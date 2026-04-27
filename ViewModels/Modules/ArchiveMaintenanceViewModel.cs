using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.ViewModels.Commands;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// ViewModel für die Archivpflege: rekursiver Scan vorhandener Archiv-MKVs, Review der geplanten
/// Header-/Dateinamenskorrekturen und explizites Anwenden ausgewählter Änderungen.
/// </summary>
internal sealed class ArchiveMaintenanceViewModel : INotifyPropertyChanged, IGlobalSettingsAwareModule
{
    private readonly ArchiveMaintenanceModuleServices _services;
    private readonly IUserDialogService _dialogService;
    private bool _isBusy;
    private string _rootDirectory;
    private string _statusText = "Archivordner wählen und Scan starten.";
    private string _logText = string.Empty;
    private int _progressValue;
    private ArchiveMaintenanceItemViewModel? _selectedItem;

    public ArchiveMaintenanceViewModel(
        ArchiveMaintenanceModuleServices services,
        IUserDialogService dialogService)
    {
        _services = services;
        _dialogService = dialogService;
        _rootDirectory = ResolveInitialRootDirectory();
        SelectRootDirectoryCommand = new RelayCommand(SelectRootDirectory, () => !_isBusy);
        ScanCommand = new AsyncRelayCommand(ScanAsync, CanScan, HandleUnexpectedCommandError);
        ToggleSelectedItemSelectionCommand = new RelayCommand(ToggleSelectedItemSelection, () => IsInteractive && SelectedItem is { CanSelect: true });
        SelectAllWritableCommand = new RelayCommand(SelectAllWritable, () => IsInteractive && Items.Any(item => item.CanSelect && !item.IsSelected));
        DeselectAllCommand = new RelayCommand(DeselectAll, () => IsInteractive && Items.Any(item => item.IsSelected));
        ApplySelectedCommand = new AsyncRelayCommand(ApplySelectedAsync, CanApplySelected, HandleUnexpectedCommandError);
        OpenSelectedFileCommand = new RelayCommand(OpenSelectedFile, () => SelectedItem is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ArchiveMaintenanceItemViewModel> Items { get; } = [];

    public RelayCommand SelectRootDirectoryCommand { get; }

    public AsyncRelayCommand ScanCommand { get; }

    public RelayCommand ToggleSelectedItemSelectionCommand { get; }

    public RelayCommand SelectAllWritableCommand { get; }

    public RelayCommand DeselectAllCommand { get; }

    public AsyncRelayCommand ApplySelectedCommand { get; }

    public RelayCommand OpenSelectedFileCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
            {
                return;
            }

            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsInteractive));
            RefreshCommandStates();
        }
    }

    public bool IsInteractive => !IsBusy;

    public string RootDirectory
    {
        get => _rootDirectory;
        set
        {
            var normalizedValue = value?.Trim() ?? string.Empty;
            if (_rootDirectory == normalizedValue)
            {
                return;
            }

            _rootDirectory = normalizedValue;
            OnPropertyChanged();
            ScanCommand.RaiseCanExecuteChanged();
        }
    }

    public ArchiveMaintenanceItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (ReferenceEquals(_selectedItem, value))
            {
                return;
            }

            _selectedItem = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedDetailText));
            ToggleSelectedItemSelectionCommand.RaiseCanExecuteChanged();
            OpenSelectedFileCommand.RaiseCanExecuteChanged();
        }
    }

    public string SelectedDetailText => SelectedItem?.DetailText ?? "Keine Archivdatei ausgewählt.";

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
            var normalizedValue = Math.Clamp(value, 0, 100);
            if (_progressValue == normalizedValue)
            {
                return;
            }

            _progressValue = normalizedValue;
            OnPropertyChanged();
        }
    }

    public int TotalCount => Items.Count;

    public int WritableCount => Items.Count(item => item.HasWritableChanges);

    public int RemuxIssueCount => Items.Count(item => item.StatusText.Contains("Remux", StringComparison.OrdinalIgnoreCase));

    public int SelectedCount => Items.Count(item => item.IsSelected);

    public string SummaryText
    {
        get
        {
            if (Items.Count == 0)
            {
                return "Noch kein Scan durchgeführt.";
            }

            return $"{TotalCount} Datei(en), {WritableCount} mit direkt schreibbaren Änderungen, {RemuxIssueCount} mit Remux-Hinweis, {SelectedCount} ausgewählt.";
        }
    }

    public void HandleGlobalSettingsChanged()
    {
        RootDirectory = ResolveInitialRootDirectory();
    }

    private void SelectRootDirectory()
    {
        var selectedDirectory = _dialogService.SelectFolder(
            "Archivordner für die Pflege auswählen",
            Directory.Exists(RootDirectory) ? RootDirectory : ResolveInitialRootDirectory());
        if (!string.IsNullOrWhiteSpace(selectedDirectory))
        {
            RootDirectory = selectedDirectory;
        }
    }

    private async Task ScanAsync()
    {
        IsBusy = true;
        Items.Clear();
        SelectedItem = null;
        LogText = string.Empty;
        RefreshCounts();
        try
        {
            var progress = new Progress<ArchiveMaintenanceProgress>(update =>
            {
                StatusText = update.StatusText;
                ProgressValue = update.ProgressPercent;
            });
            var result = await _services.ArchiveMaintenance.ScanAsync(RootDirectory, progress);
            foreach (var item in result.Items.Select(analysis => new ArchiveMaintenanceItemViewModel(analysis)))
            {
                item.PropertyChanged += Item_OnPropertyChanged;
                Items.Add(item);
            }

            SelectedItem = Items.FirstOrDefault();
            StatusText = $"Scan abgeschlossen: {Items.Count} Datei(en).";
            AppendLog($"SCAN: {StatusText}");
            RefreshCounts();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplySelectedAsync()
    {
        var selectedItems = Items.Where(item => item.IsSelected && item.CanSelect).ToList();
        if (selectedItems.Count == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            for (var index = 0; index < selectedItems.Count; index++)
            {
                var item = selectedItems[index];
                StatusText = $"Wende Änderungen an {index + 1}/{selectedItems.Count}: {item.FileName}";
                ProgressValue = index * 100 / selectedItems.Count;
                var result = await _services.ArchiveMaintenance.ApplyAsync(
                    item.CreateApplyRequest(),
                    new Progress<string>(AppendLog));
                if (!result.Success)
                {
                    AppendLog($"FEHLER: {item.FileName}: {result.Message}");
                    continue;
                }

                item.MarkApplied(result.CurrentFilePath);
                AppendLog($"OK: {Path.GetFileName(result.CurrentFilePath)}");
            }

            ProgressValue = 100;
            StatusText = "Archivpflege-Anwendung abgeschlossen. Für Remux-Hinweise bleibt ein manueller Mux-Lauf nötig.";
            RefreshCounts();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ToggleSelectedItemSelection()
    {
        if (SelectedItem is not { CanSelect: true } selectedItem)
        {
            return;
        }

        selectedItem.IsSelected = !selectedItem.IsSelected;
    }

    private void SelectAllWritable()
    {
        foreach (var item in Items.Where(item => item.CanSelect))
        {
            item.IsSelected = true;
        }
    }

    private void DeselectAll()
    {
        foreach (var item in Items)
        {
            item.IsSelected = false;
        }
    }

    private void OpenSelectedFile()
    {
        if (SelectedItem is not null)
        {
            _dialogService.OpenPathWithDefaultApp(SelectedItem.FilePath);
        }
    }

    private bool CanScan()
    {
        return !_isBusy && Directory.Exists(RootDirectory);
    }

    private bool CanApplySelected()
    {
        return !_isBusy && Items.Any(item => item.IsSelected && item.CanSelect);
    }

    private string ResolveInitialRootDirectory()
    {
        var configuredPath = _services.ArchiveSettings.Load().DefaultSeriesArchiveRootPath;
        return string.IsNullOrWhiteSpace(configuredPath)
            ? SeriesArchiveService.DefaultArchiveRootDirectory
            : configuredPath;
    }

    private void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        LogText = string.IsNullOrWhiteSpace(LogText)
            ? line
            : LogText + Environment.NewLine + line;
    }

    private void Item_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ArchiveMaintenanceItemViewModel.IsSelected) or nameof(ArchiveMaintenanceItemViewModel.StatusText))
        {
            RefreshCounts();
            RefreshCommandStates();
        }
    }

    private void RefreshCounts()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(WritableCount));
        OnPropertyChanged(nameof(RemuxIssueCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SummaryText));
    }

    private void RefreshCommandStates()
    {
        SelectRootDirectoryCommand.RaiseCanExecuteChanged();
        ScanCommand.RaiseCanExecuteChanged();
        ToggleSelectedItemSelectionCommand.RaiseCanExecuteChanged();
        SelectAllWritableCommand.RaiseCanExecuteChanged();
        DeselectAllCommand.RaiseCanExecuteChanged();
        ApplySelectedCommand.RaiseCanExecuteChanged();
        OpenSelectedFileCommand.RaiseCanExecuteChanged();
    }

    private void HandleUnexpectedCommandError(Exception ex)
    {
        StatusText = "Fehler: " + ex.Message;
        AppendLog("FEHLER: " + ex.Message);
        _dialogService.ShowError(ex.Message);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
