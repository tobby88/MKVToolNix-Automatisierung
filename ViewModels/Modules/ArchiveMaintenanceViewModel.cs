using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels.Commands;
using MkvToolnixAutomatisierung.Windows;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// ViewModel für die Archivpflege: rekursiver Scan vorhandener Archiv-MKVs, Review der geplanten
/// Header-/Dateinamenskorrekturen und explizites Anwenden ausgewählter Änderungen.
/// </summary>
internal sealed class ArchiveMaintenanceViewModel : INotifyPropertyChanged, IGlobalSettingsAwareModule
{
    private readonly ArchiveMaintenanceModuleServices _services;
    private readonly IUserDialogService _dialogService;
    private readonly IModuleLogService? _moduleLogs;
    private bool _isBusy;
    private bool _isScanning;
    private string _rootDirectory;
    private string _statusText = "Archivordner wählen und Scan starten.";
    private string _logText = string.Empty;
    private int _progressValue;
    private ArchiveMaintenanceItemViewModel? _selectedItem;
    private CancellationTokenSource? _scanCancellationSource;

    public ArchiveMaintenanceViewModel(
        ArchiveMaintenanceModuleServices services,
        IUserDialogService dialogService,
        IModuleLogService? moduleLogs = null)
    {
        _services = services;
        _dialogService = dialogService;
        _moduleLogs = moduleLogs;
        _rootDirectory = ResolveInitialRootDirectory();
        SelectRootDirectoryCommand = new AsyncRelayCommand(SelectRootDirectoryAsync, () => !_isBusy, HandleUnexpectedCommandError);
        ScanCommand = new AsyncRelayCommand(ScanAsync, CanScan, HandleUnexpectedCommandError);
        CancelScanCommand = new RelayCommand(CancelScan, () => CanCancelScan);
        ToggleSelectedItemSelectionCommand = new RelayCommand(ToggleSelectedItemSelection, () => IsInteractive && SelectedItem is { CanSelect: true });
        SelectAllWritableCommand = new RelayCommand(SelectAllWritable, () => IsInteractive && Items.Any(item => item.CanSelect && !item.IsSelected));
        DeselectAllCommand = new RelayCommand(DeselectAll, () => IsInteractive && Items.Any(item => item.IsSelected));
        ApplySelectedCommand = new AsyncRelayCommand(ApplySelectedAsync, CanApplySelected, HandleUnexpectedCommandError);
        OpenSelectedFileCommand = new RelayCommand(OpenSelectedFile, () => SelectedItem is not null);
        ReviewSelectedTvdbCommand = new RelayCommand(ReviewSelectedTvdb, () => IsInteractive && SelectedItem?.CanReviewTvdb == true);
        ReviewSelectedImdbCommand = new RelayCommand(ReviewSelectedImdb, () => IsInteractive && SelectedItem?.CanReviewImdb == true);
        ResetSelectedFileNameCommand = new RelayCommand(() => SelectedItem?.ResetTargetFileNameToCurrent(), () => IsInteractive && SelectedItem is not null);
        ResetSelectedContainerTitleCommand = new RelayCommand(() => SelectedItem?.ResetTargetContainerTitleToCurrent(), () => IsInteractive && SelectedItem is not null);
        ResetSelectedNfoTitleCommand = new RelayCommand(() => SelectedItem?.ResetTargetNfoTitleToCurrent(), () => IsInteractive && SelectedItem?.HasNfoTextSync == true);
        ResetSelectedNfoSortTitleCommand = new RelayCommand(() => SelectedItem?.ResetTargetNfoSortTitleToCurrent(), () => IsInteractive && SelectedItem?.HasNfoTextSync == true);
        ToggleSelectedNfoTitleLockCommand = new RelayCommand(() => SelectedItem?.ToggleTargetNfoTitleLock(), () => IsInteractive && SelectedItem?.HasNfoTextSync == true);
        ToggleSelectedNfoSortTitleLockCommand = new RelayCommand(() => SelectedItem?.ToggleTargetNfoSortTitleLock(), () => IsInteractive && SelectedItem?.HasNfoTextSync == true);
        SuppressSelectedFileNameChangeCommand = new RelayCommand(SuppressSelectedFileNameChange, () => IsInteractive && SelectedItem?.CanSuppressFileNameChange == true);
        RestoreSelectedFileNameSuggestionCommand = new RelayCommand(RestoreSelectedFileNameSuggestion, () => IsInteractive && SelectedItem?.CanRestoreFileNameSuggestion == true);
        SuppressSelectedContainerTitleChangeCommand = new RelayCommand(SuppressSelectedContainerTitleChange, () => IsInteractive && SelectedItem?.CanSuppressContainerTitleChange == true);
        RestoreSelectedContainerTitleSuggestionCommand = new RelayCommand(RestoreSelectedContainerTitleSuggestion, () => IsInteractive && SelectedItem?.CanRestoreContainerTitleSuggestion == true);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ArchiveMaintenanceItemViewModel> Items { get; } = [];

    public AsyncRelayCommand SelectRootDirectoryCommand { get; }

    public AsyncRelayCommand ScanCommand { get; }

    public RelayCommand CancelScanCommand { get; }

    public RelayCommand ToggleSelectedItemSelectionCommand { get; }

    public RelayCommand SelectAllWritableCommand { get; }

    public RelayCommand DeselectAllCommand { get; }

    public AsyncRelayCommand ApplySelectedCommand { get; }

    public RelayCommand OpenSelectedFileCommand { get; }

    public RelayCommand ReviewSelectedTvdbCommand { get; }

    public RelayCommand ReviewSelectedImdbCommand { get; }

    public RelayCommand ResetSelectedFileNameCommand { get; }

    public RelayCommand ResetSelectedContainerTitleCommand { get; }

    public RelayCommand ResetSelectedNfoTitleCommand { get; }

    public RelayCommand ResetSelectedNfoSortTitleCommand { get; }

    public RelayCommand ToggleSelectedNfoTitleLockCommand { get; }

    public RelayCommand ToggleSelectedNfoSortTitleLockCommand { get; }

    public RelayCommand SuppressSelectedFileNameChangeCommand { get; }

    public RelayCommand RestoreSelectedFileNameSuggestionCommand { get; }

    public RelayCommand SuppressSelectedContainerTitleChangeCommand { get; }

    public RelayCommand RestoreSelectedContainerTitleSuggestionCommand { get; }

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

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (_isScanning == value)
            {
                return;
            }

            _isScanning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanCancelScan));
            OnPropertyChanged(nameof(CancelScanTooltip));
            RefreshCommandStates();
        }
    }

    public bool CanCancelScan => IsScanning && _scanCancellationSource is { IsCancellationRequested: false };

    public string CancelScanTooltip => CanCancelScan
        ? "Bricht den laufenden Archivpflege-Scan inklusive aktueller Dateianalyse ab."
        : "Derzeit läuft kein abbrechbarer Archivpflege-Scan.";

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
            ReviewSelectedTvdbCommand.RaiseCanExecuteChanged();
            ReviewSelectedImdbCommand.RaiseCanExecuteChanged();
            ResetSelectedFileNameCommand.RaiseCanExecuteChanged();
            ResetSelectedContainerTitleCommand.RaiseCanExecuteChanged();
            ResetSelectedNfoTitleCommand.RaiseCanExecuteChanged();
            ResetSelectedNfoSortTitleCommand.RaiseCanExecuteChanged();
            ToggleSelectedNfoTitleLockCommand.RaiseCanExecuteChanged();
            ToggleSelectedNfoSortTitleLockCommand.RaiseCanExecuteChanged();
            SuppressSelectedFileNameChangeCommand.RaiseCanExecuteChanged();
            RestoreSelectedFileNameSuggestionCommand.RaiseCanExecuteChanged();
            SuppressSelectedContainerTitleChangeCommand.RaiseCanExecuteChanged();
            RestoreSelectedContainerTitleSuggestionCommand.RaiseCanExecuteChanged();
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

    private async Task SelectRootDirectoryAsync()
    {
        var selectedDirectory = _dialogService.SelectFolder(
            "Archivordner für die Pflege auswählen",
            Directory.Exists(RootDirectory) ? RootDirectory : ResolveInitialRootDirectory());
        if (!string.IsNullOrWhiteSpace(selectedDirectory))
        {
            RootDirectory = selectedDirectory;
            await ScanCommand.ExecuteAsync();
        }
    }

    private async Task ScanAsync()
    {
        using var scanCancellationSource = new CancellationTokenSource();
        _scanCancellationSource = scanCancellationSource;
        IsScanning = true;
        IsBusy = true;
        Items.Clear();
        SelectedItem = null;
        LogText = string.Empty;
        RefreshCounts();
        try
        {
            var progress = new InlineProgress<ArchiveMaintenanceProgress>(update =>
            {
                if (scanCancellationSource.IsCancellationRequested)
                {
                    return;
                }

                StatusText = update.StatusText;
                ProgressValue = update.ProgressPercent;
            });
            var result = await _services.ArchiveMaintenance.ScanAsync(
                RootDirectory,
                progress,
                scanCancellationSource.Token);
            var suppressedChanges = _services.ArchiveSettings.Load().SuppressedMaintenanceChanges;
            foreach (var item in result.Items
                         .OrderBy(analysis => Path.GetFileName(analysis.FilePath), StringComparer.OrdinalIgnoreCase)
                         .Select(analysis => new ArchiveMaintenanceItemViewModel(analysis)))
            {
                item.ApplySuppressedChanges(suppressedChanges);
                item.PropertyChanged += Item_OnPropertyChanged;
                Items.Add(item);
            }

            SelectedItem = Items.FirstOrDefault();
            StatusText = $"Scan abgeschlossen: {Items.Count} Datei(en).";
            AppendLog($"SCAN: {StatusText}");
            RefreshCounts();
            SaveVisibleLog("Scan");
        }
        catch (OperationCanceledException) when (scanCancellationSource.IsCancellationRequested)
        {
            ProgressValue = 0;
            StatusText = "Scan abgebrochen.";
            AppendLog("SCAN: Scan abgebrochen.");
            RefreshCounts();
            SaveVisibleLog("Scan abgebrochen");
        }
        finally
        {
            if (ReferenceEquals(_scanCancellationSource, scanCancellationSource))
            {
                _scanCancellationSource = null;
            }

            IsScanning = false;
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
                SelectedItem = item;
                StatusText = $"Wende Änderungen an {index + 1}/{selectedItems.Count}: {item.FileName}";
                ProgressValue = index * 100 / selectedItems.Count;
                AppendLog(StatusText);
                var reportedOutputLines = new List<string>();
                var result = await _services.ArchiveMaintenance.ApplyAsync(
                    item.CreateApplyRequest(),
                    new Progress<string>(line =>
                    {
                        reportedOutputLines.Add(line);
                        AppendLog(line);
                    }));
                AppendResultOutputLines(result.OutputLines, reportedOutputLines);
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
            AppendLog(StatusText);
            RefreshCounts();
            SaveVisibleLog("Änderungen anwenden");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AppendResultOutputLines(IReadOnlyList<string> resultOutputLines, IReadOnlyList<string> alreadyReportedLines)
    {
        var reportedIndex = 0;
        foreach (var outputLine in resultOutputLines)
        {
            if (reportedIndex < alreadyReportedLines.Count
                && string.Equals(outputLine, alreadyReportedLines[reportedIndex], StringComparison.Ordinal))
            {
                reportedIndex++;
                continue;
            }

            AppendLog(outputLine);
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

    private void SuppressSelectedFileNameChange()
    {
        if (SelectedItem?.SuppressFileNameChange() is { } suppressedChange)
        {
            SaveSuppressedChange(suppressedChange);
            AppendLog($"Archivpflege-Ablehnung gespeichert: {SelectedItem.FileName} Dateiname.");
            SaveVisibleLog("Manuelle Korrektur");
        }
    }

    private void RestoreSelectedFileNameSuggestion()
    {
        if (SelectedItem is null)
        {
            return;
        }

        RemoveSuppressedChange(SelectedItem.FilePath, ArchiveMaintenanceItemViewModel.FileNameChangeKind);
        SelectedItem.RestoreFileNameSuggestion();
        AppendLog($"Archivpflege-Ablehnung aufgehoben: {SelectedItem.FileName} Dateiname.");
        SaveVisibleLog("Manuelle Korrektur");
    }

    private void SuppressSelectedContainerTitleChange()
    {
        if (SelectedItem?.SuppressContainerTitleChange() is { } suppressedChange)
        {
            SaveSuppressedChange(suppressedChange);
            AppendLog($"Archivpflege-Ablehnung gespeichert: {SelectedItem.FileName} MKV-Titel.");
            SaveVisibleLog("Manuelle Korrektur");
        }
    }

    private void RestoreSelectedContainerTitleSuggestion()
    {
        if (SelectedItem is null)
        {
            return;
        }

        RemoveSuppressedChange(SelectedItem.FilePath, ArchiveMaintenanceItemViewModel.ContainerTitleChangeKind);
        SelectedItem.RestoreContainerTitleSuggestion();
        AppendLog($"Archivpflege-Ablehnung aufgehoben: {SelectedItem.FileName} MKV-Titel.");
        SaveVisibleLog("Manuelle Korrektur");
    }

    private void SaveSuppressedChange(ArchiveMaintenanceSuppressedChange suppressedChange)
    {
        var settings = _services.ArchiveSettings.Load();
        settings.SuppressedMaintenanceChanges.RemoveAll(change =>
            PathComparisonHelper.AreSamePath(change.MediaFilePath, suppressedChange.MediaFilePath)
            && string.Equals(change.ChangeKind, suppressedChange.ChangeKind, StringComparison.Ordinal));
        settings.SuppressedMaintenanceChanges.Add(suppressedChange);
        _services.ArchiveSettings.Save(settings);
        RefreshCounts();
        RefreshCommandStates();
    }

    private void RemoveSuppressedChange(string mediaFilePath, string changeKind)
    {
        var settings = _services.ArchiveSettings.Load();
        settings.SuppressedMaintenanceChanges.RemoveAll(change =>
            PathComparisonHelper.AreSamePath(change.MediaFilePath, mediaFilePath)
            && string.Equals(change.ChangeKind, changeKind, StringComparison.Ordinal));
        _services.ArchiveSettings.Save(settings);
        RefreshCounts();
        RefreshCommandStates();
    }

    private void CancelScan()
    {
        if (!CanCancelScan)
        {
            return;
        }

        StatusText = "Scan wird abgebrochen...";
        AppendLog("SCAN: Abbruch angefordert.");
        _scanCancellationSource?.Cancel();
        OnPropertyChanged(nameof(CanCancelScan));
        OnPropertyChanged(nameof(CancelScanTooltip));
        RefreshCommandStates();
    }

    private void OpenSelectedFile()
    {
        if (SelectedItem is not null)
        {
            _dialogService.OpenPathWithDefaultApp(SelectedItem.FilePath);
        }
    }

    private void ReviewSelectedTvdb()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        if (!item.TryBuildMetadataGuess(out var guess) || guess is null)
        {
            _dialogService.ShowWarning("TVDB-Prüfung", "Aus dem Ziel-Dateinamen kann kein TVDB-Suchvorschlag gebaut werden.");
            return;
        }

        var dialog = new TvdbLookupWindow(_services.EpisodeMetadata, guess, _services.SettingsDialog)
        {
            Owner = ResolveOwner()
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (dialog.KeepLocalDetection)
        {
            AppendLog($"TVDB beibehalten: {item.FileName}");
            SaveVisibleLog("TVDB prüfen");
            return;
        }

        if (dialog.SelectedEpisodeSelection is not { } selection)
        {
            return;
        }

        item.ApplyTvdbSelection(selection);
        AppendLog($"TVDB gewählt: {item.FileName} -> {selection.TvdbEpisodeId}");
        RefreshCounts();
        RefreshCommandStates();
        SaveVisibleLog("TVDB prüfen");
    }

    private void ReviewSelectedImdb()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        item.TryBuildMetadataGuess(out var guess);
        var lookupMode = _services.EpisodeMetadata.LoadSettings().ImdbLookupMode;
        var dialog = new ImdbLookupWindow(_services.ImdbLookup, lookupMode, guess, item.TargetImdbId)
        {
            Owner = ResolveOwner()
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (dialog.ImdbExplicitlyUnavailable)
        {
            item.MarkImdbUnavailable();
            AppendLog($"IMDb geprüft: keine IMDb-ID für {item.FileName}");
            SaveVisibleLog("IMDb prüfen");
        }
        else if (!string.IsNullOrWhiteSpace(dialog.SelectedImdbId))
        {
            item.ApplyImdbSelection(dialog.SelectedImdbId!);
            AppendLog($"IMDb gewählt: {item.FileName} -> {dialog.SelectedImdbId}");
            SaveVisibleLog("IMDb prüfen");
        }

        RefreshCounts();
        RefreshCommandStates();
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

    /// <summary>
    /// Persistiert das sichtbare Archivpflege-Protokoll nach Scan- und Schreibaktionen.
    /// </summary>
    private void SaveVisibleLog(string operationLabel)
    {
        if (_moduleLogs is null || string.IsNullOrWhiteSpace(LogText))
        {
            return;
        }

        try
        {
            _moduleLogs.SaveModuleLog("Archivpflege", operationLabel, RootDirectory, LogText);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogService.ShowWarning("Protokoll", $"Das Archivpflege-Protokoll konnte nicht gespeichert werden.\n\n{ex.Message}");
        }
    }

    private void Item_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, SelectedItem)
            && e.PropertyName is nameof(ArchiveMaintenanceItemViewModel.DetailText)
                or nameof(ArchiveMaintenanceItemViewModel.ChangeSummary)
                or nameof(ArchiveMaintenanceItemViewModel.ManualValidationMessage))
        {
            OnPropertyChanged(nameof(SelectedDetailText));
        }

        if (e.PropertyName is nameof(ArchiveMaintenanceItemViewModel.IsSelected)
            or nameof(ArchiveMaintenanceItemViewModel.StatusText)
            or nameof(ArchiveMaintenanceItemViewModel.HasWritableChanges)
            or nameof(ArchiveMaintenanceItemViewModel.CanSelect)
            or nameof(ArchiveMaintenanceItemViewModel.ManualValidationMessage)
            or nameof(ArchiveMaintenanceItemViewModel.CanSuppressFileNameChange)
            or nameof(ArchiveMaintenanceItemViewModel.CanRestoreFileNameSuggestion)
            or nameof(ArchiveMaintenanceItemViewModel.CanSuppressContainerTitleChange)
            or nameof(ArchiveMaintenanceItemViewModel.CanRestoreContainerTitleSuggestion))
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
        CancelScanCommand.RaiseCanExecuteChanged();
        ToggleSelectedItemSelectionCommand.RaiseCanExecuteChanged();
        SelectAllWritableCommand.RaiseCanExecuteChanged();
        DeselectAllCommand.RaiseCanExecuteChanged();
        ApplySelectedCommand.RaiseCanExecuteChanged();
        OpenSelectedFileCommand.RaiseCanExecuteChanged();
        ReviewSelectedTvdbCommand.RaiseCanExecuteChanged();
        ReviewSelectedImdbCommand.RaiseCanExecuteChanged();
        ResetSelectedFileNameCommand.RaiseCanExecuteChanged();
        ResetSelectedContainerTitleCommand.RaiseCanExecuteChanged();
        ResetSelectedNfoTitleCommand.RaiseCanExecuteChanged();
        ResetSelectedNfoSortTitleCommand.RaiseCanExecuteChanged();
        ToggleSelectedNfoTitleLockCommand.RaiseCanExecuteChanged();
        ToggleSelectedNfoSortTitleLockCommand.RaiseCanExecuteChanged();
        SuppressSelectedFileNameChangeCommand.RaiseCanExecuteChanged();
        RestoreSelectedFileNameSuggestionCommand.RaiseCanExecuteChanged();
        SuppressSelectedContainerTitleChangeCommand.RaiseCanExecuteChanged();
        RestoreSelectedContainerTitleSuggestionCommand.RaiseCanExecuteChanged();
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

    private static Window? ResolveOwner()
    {
        return Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
               ?? Application.Current?.MainWindow;
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public InlineProgress(Action<T> handler)
        {
            _handler = handler;
        }

        public void Report(T value) => _handler(value);
    }
}
