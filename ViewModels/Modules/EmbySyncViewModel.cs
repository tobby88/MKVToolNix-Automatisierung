using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Emby;
using MkvToolnixAutomatisierung.ViewModels.Commands;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Verwaltet den nachgelagerten Emby-Abgleich für neu erzeugte MKV-Dateien und deren NFO-Provider-IDs.
/// </summary>
internal sealed class EmbySyncViewModel : INotifyPropertyChanged
{
    private readonly EmbyModuleServices _services;
    private readonly IUserDialogService _dialogService;
    private readonly ObservableCollection<EmbySyncItemViewModel> _items = [];
    private readonly List<string> _reportPaths = [];

    private EmbySyncItemViewModel? _selectedItem;
    private string _serverUrl;
    private string _apiKey;
    private string _reportPath = string.Empty;
    private string _statusText = "Bereit";
    private string _logText = string.Empty;
    private int _progressValue;
    private bool _isBusy;

    public EmbySyncViewModel(EmbyModuleServices services, IUserDialogService dialogService)
    {
        _services = services;
        _dialogService = dialogService;
        var settings = _services.Settings.Load();
        _serverUrl = settings.ServerUrl;
        _apiKey = settings.ApiKey;
        Action<Exception> unexpectedCommandErrorHandler = ex => _dialogService.ShowError($"Unerwarteter Fehler:\n\n{ex.Message}");

        SelectReportCommand = new AsyncRelayCommand(SelectReportAsync, () => !_isBusy, unexpectedCommandErrorHandler);
        LoadReportCommand = new AsyncRelayCommand(LoadReportAsync, CanLoadReport, unexpectedCommandErrorHandler);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, CanUseEmbyApi, unexpectedCommandErrorHandler);
        AnalyzeItemsCommand = new AsyncRelayCommand(AnalyzeItemsAsync, () => !_isBusy && Items.Count > 0, unexpectedCommandErrorHandler);
        RunSyncCommand = new AsyncRelayCommand(RunSyncAsync, CanRunSync, unexpectedCommandErrorHandler);
        SaveSettingsCommand = new RelayCommand(SaveSettings, () => !_isBusy);
        SelectAllCommand = new RelayCommand(SelectAllRunnable, () => !_isBusy && Items.Any(item => !item.IsSelected));
        DeselectAllCommand = new RelayCommand(DeselectAll, () => !_isBusy && Items.Any(item => item.IsSelected));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncRelayCommand SelectReportCommand { get; }

    public AsyncRelayCommand LoadReportCommand { get; }

    public AsyncRelayCommand TestConnectionCommand { get; }

    public AsyncRelayCommand AnalyzeItemsCommand { get; }

    public AsyncRelayCommand RunSyncCommand { get; }

    public RelayCommand SaveSettingsCommand { get; }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand DeselectAllCommand { get; }

    public ObservableCollection<EmbySyncItemViewModel> Items => _items;

    public EmbySyncItemViewModel? SelectedItem
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
        }
    }

    public string ServerUrl
    {
        get => _serverUrl;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            if (_serverUrl == normalized)
            {
                return;
            }

            _serverUrl = normalized;
            OnPropertyChanged();
            RefreshCommands();
        }
    }

    public string ApiKey
    {
        get => _apiKey;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            if (_apiKey == normalized)
            {
                return;
            }

            _apiKey = normalized;
            OnPropertyChanged();
            RefreshCommands();
        }
    }

    public string ReportPath
    {
        get => _reportPath;
        private set
        {
            if (_reportPath == value)
            {
                return;
            }

            _reportPath = value;
            OnPropertyChanged();
            RefreshCommands();
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

    public int MissingIdCount => Items.Count(item => !item.HasProviderIds);

    public string SummaryText => ItemCount == 0
        ? "Noch kein Metadatenreport geladen."
        : $"{ItemCount} Datei(en), {SelectedCount} ausgewählt, {MissingIdCount} ohne TVDB-/IMDB-ID.";

    public string AnalyzeItemsTooltip => HasEmbyApiSettings()
        ? "Prüft lokale NFO-Dateien und liest zusätzlich die aktuell in Emby sichtbaren Episoden samt Provider-IDs ein."
        : "Prüft nur die lokalen NFO-Dateien. Für den zusätzlichen Emby-Abgleich bitte Server und API-Key eintragen.";

    public string RunSyncTooltip => "Startet bevorzugt nur den Scan der Serienbibliothek, wartet begrenzt auf neue Emby-Items und schreibt danach TVDB-/IMDB-IDs in die lokalen NFO-Dateien zurück. Der serverseitige Scan kann danach noch weiterlaufen.";

    /// <summary>
    /// Kurzer Ablaufhinweis für den manuellen Emby-Schritt.
    /// </summary>
    public string WorkflowInfoText =>
        "1. Reports laden importiert die JSON-Metadatenreports neu erzeugter MKV-Dateien. "
        + "2. NFO/Emby prüfen liest lokale NFO-Dateien und optional bereits sichtbare Emby-Provider-IDs ein. "
        + "3. Scan + NFO-Sync startet bevorzugt nur den Serienbibliotheksscan und schreibt danach die IDs in die NFO-Dateien zurück.";

    private async Task SelectReportAsync()
    {
        var selectedPaths = _dialogService.SelectFiles(
            "Metadatenreports neu erzeugter Ausgabedateien auswählen",
            "Metadatenreports (*.metadata.json;*.json)|*.metadata.json;*.json|Alle Dateien (*.*)|*.*",
            GetInitialReportDirectory());
        if (selectedPaths is null || selectedPaths.Length == 0)
        {
            return;
        }

        SetReportPaths(selectedPaths);
        await LoadReportAsync();
    }

    private Task LoadReportAsync()
    {
        if (!CanLoadReport())
        {
            return Task.CompletedTask;
        }

        var importEntries = _reportPaths
            .SelectMany(_services.Sync.LoadNewOutputReport)
            .GroupBy(entry => entry.MediaFilePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(entry => entry.MediaFilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ReplaceItems(importEntries);
        ProgressValue = importEntries.Count == 0 ? 0 : 20;
        StatusText = importEntries.Count == 0
            ? "Keine MKV-Pfade im Metadatenreport gefunden"
            : $"Metadatenreport geladen: {importEntries.Count} MKV-Datei(en) aus {_reportPaths.Count} Datei(en)";
        foreach (var reportPath in _reportPaths)
        {
            AppendLog($"Metadatenreport geladen: {reportPath}");
        }
        return AnalyzeItemsAsync(queryEmby: false);
    }

    private async Task TestConnectionAsync()
    {
        await RunBusyAsync(async () =>
        {
            var settings = BuildSettingsFromInput();
            SaveSettings(settings);
            StatusText = "Prüfe Emby-Verbindung...";
            ProgressValue = 20;
            var serverInfo = await _services.Sync.TestConnectionAsync(settings);
            ProgressValue = 100;
            StatusText = $"Emby verbunden: {serverInfo.ServerName} ({serverInfo.Version})";
            AppendLog(StatusText);
        });
    }

    private Task AnalyzeItemsAsync()
    {
        return AnalyzeItemsAsync(queryEmby: true);
    }

    private async Task AnalyzeItemsAsync(bool queryEmby)
    {
        if (Items.Count == 0)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var settings = BuildSettingsFromInput();
            var canQueryEmby = queryEmby && HasEmbyApiSettings();
            if (canQueryEmby)
            {
                SaveSettings(settings);
            }

            var items = Items.ToList();
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                StatusText = canQueryEmby
                    ? $"Prüfe NFO und Emby-Item {index + 1}/{items.Count}..."
                    : $"Prüfe lokale NFO {index + 1}/{items.Count}...";
                ProgressValue = ScaleProgress(index, items.Count, 10, 90);

                var analysis = await _services.Sync.AnalyzeFileAsync(
                    settings,
                    item.MediaFilePath,
                    canQueryEmby,
                    CancellationToken.None);
                item.ApplyAnalysis(analysis);
            }

            ProgressValue = 100;
            StatusText = $"Prüfung abgeschlossen: {items.Count} Datei(en), {MissingIdCount} ohne Provider-ID";
            AppendLog(StatusText);
            RefreshSummaryAndCommands();
        });
    }

    private async Task RunSyncAsync()
    {
        var selectedItems = Items.Where(item => item.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            _dialogService.ShowWarning("Emby-Abgleich", "Es sind keine Dateien für den Emby-Abgleich ausgewählt.");
            return;
        }

        await RunBusyAsync(async () =>
        {
            var settings = BuildSettingsFromInput();
            SaveSettings(settings);
            var archiveSettings = _services.ArchiveSettings.Load();

            StatusText = "Starte Emby-Scan...";
            ProgressValue = 5;
            var scanTrigger = await _services.Sync.TriggerSeriesLibraryScanAsync(
                settings,
                archiveSettings.DefaultSeriesArchiveRootPath);
            ProgressValue = 10;
            StatusText = $"{scanTrigger.Message} Warte auf neue Emby-Items für den lokalen NFO-Abgleich...";
            AppendLog(scanTrigger.Message);

            await ResolveEmbyItemsWithinBudgetAsync(settings, selectedItems);
            await RefreshSelectedAnalysesAfterLibraryScanAsync(settings, selectedItems);
            var updatedCount = 0;
            var skippedCount = 0;

            for (var index = 0; index < selectedItems.Count; index++)
            {
                var item = selectedItems[index];
                StatusText = $"Aktualisiere NFO {index + 1}/{selectedItems.Count}...";
                ProgressValue = ScaleProgress(index, selectedItems.Count, 55, 95);

                if (!item.ProviderIds.HasAny)
                {
                    item.SetStatus("Übersprungen", "Keine TVDB- oder IMDB-ID vorhanden. Bitte IDs manuell ergänzen oder Emby-Metadaten prüfen.");
                    skippedCount++;
                    continue;
                }

                var updateResult = _services.Sync.UpdateNfoProviderIds(item.MediaFilePath, item.ProviderIds);
                if (!updateResult.Success)
                {
                    item.SetStatus("NFO prüfen", updateResult.Message);
                    skippedCount++;
                    continue;
                }

                if (!updateResult.NfoChanged)
                {
                    item.SetStatus("NFO aktuell", updateResult.Message);
                    continue;
                }

                var refreshTriggered = false;
                if (!string.IsNullOrWhiteSpace(item.EmbyItemId))
                {
                    await _services.Sync.RefreshItemMetadataAsync(settings, item.EmbyItemId);
                    refreshTriggered = true;
                }

                item.MarkUpdated(refreshTriggered);
                updatedCount++;
            }

            ProgressValue = 100;
            StatusText =
                $"Lokaler NFO-Abgleich abgeschlossen: {updatedCount} aktualisiert, {skippedCount} übersprungen. "
                + (scanTrigger.UsedGlobalLibraryScan
                    ? "Der globale Server-Scan kann noch im Hintergrund weiterlaufen."
                    : "Der Serienbibliotheksscan kann serverseitig noch im Hintergrund weiterlaufen.");
            AppendLog(StatusText);
            RefreshSummaryAndCommands();
        });
    }

    private async Task ResolveEmbyItemsWithinBudgetAsync(AppEmbySettings settings, IReadOnlyList<EmbySyncItemViewModel> selectedItems)
    {
        var deadline = DateTime.UtcNow.AddSeconds(Math.Clamp(settings.ScanWaitTimeoutSeconds, 5, 600));
        var pendingItems = selectedItems
            .Where(item => string.IsNullOrWhiteSpace(item.EmbyItemId))
            .ToList();
        var attempt = 0;

        while (pendingItems.Count > 0 && DateTime.UtcNow <= deadline)
        {
            attempt++;
            StatusText = $"Suche neue Emby-Items ({selectedItems.Count - pendingItems.Count}/{selectedItems.Count})...";
            foreach (var item in pendingItems.ToList())
            {
                var embyItem = await _services.Sync.FindItemByPathAsync(settings, item.MediaFilePath);
                if (embyItem is null)
                {
                    continue;
                }

                item.ApplyEmbyItem(embyItem);
                pendingItems.Remove(item);
            }

            ProgressValue = pendingItems.Count == 0
                ? 55
                : Math.Min(54, 15 + attempt * 4);

            if (pendingItems.Count > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        foreach (var item in pendingItems)
        {
            item.SetStatus(
                item.HasProviderIds ? "Lokal bereit" : "IDs fehlen",
                item.HasProviderIds
                    ? "Emby-Item wurde innerhalb der Wartezeit nicht gefunden. NFO kann trotzdem lokal aktualisiert werden."
                    : "Emby-Item wurde innerhalb der Wartezeit nicht gefunden und lokal liegen keine IDs vor.");
        }
    }

    private async Task RefreshSelectedAnalysesAfterLibraryScanAsync(
        AppEmbySettings settings,
        IReadOnlyList<EmbySyncItemViewModel> selectedItems)
    {
        for (var index = 0; index < selectedItems.Count; index++)
        {
            var item = selectedItems[index];
            StatusText = $"Prüfe NFO nach Emby-Scan {index + 1}/{selectedItems.Count}...";
            ProgressValue = ScaleProgress(index, selectedItems.Count, 45, 55);

            // Nach einem Library-Scan kann Emby die NFO erst jetzt angelegt oder Provider-IDs
            // ergänzt haben. Diese zweite, kurze Prüfung verhindert, dass wir mit veraltetem
            // "NFO fehlt"-Zustand weiterarbeiten.
            var analysis = await _services.Sync.AnalyzeFileAsync(
                settings,
                item.MediaFilePath,
                queryEmby: true,
                CancellationToken.None);
            item.ApplyAnalysis(analysis);
        }
    }

    private void ReplaceItems(IReadOnlyList<EmbyImportEntry> importEntries)
    {
        foreach (var existingItem in Items)
        {
            existingItem.PropertyChanged -= ItemOnPropertyChanged;
        }

        Items.Clear();
        foreach (var importEntry in importEntries)
        {
            var item = new EmbySyncItemViewModel(importEntry.MediaFilePath, importEntry.ProviderIds);
            item.PropertyChanged += ItemOnPropertyChanged;
            Items.Add(item);
        }

        SelectedItem = Items.FirstOrDefault();
        RefreshSummaryAndCommands();
    }

    private void SelectAllRunnable()
    {
        foreach (var item in Items)
        {
            item.IsSelected = true;
        }

        RefreshSummaryAndCommands();
    }

    private void DeselectAll()
    {
        foreach (var item in Items)
        {
            item.IsSelected = false;
        }

        RefreshSummaryAndCommands();
    }

    private void SaveSettings()
    {
        SaveSettings(BuildSettingsFromInput());
        StatusText = "Emby-Einstellungen gespeichert.";
    }

    private void SaveSettings(AppEmbySettings settings)
    {
        _services.Settings.Save(settings);
    }

    private AppEmbySettings BuildSettingsFromInput()
    {
        return new AppEmbySettings
        {
            ServerUrl = string.IsNullOrWhiteSpace(ServerUrl) ? AppEmbySettings.DefaultServerUrl : ServerUrl,
            ApiKey = ApiKey,
            ScanWaitTimeoutSeconds = _services.Settings.Load().ScanWaitTimeoutSeconds
        }.Clone();
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        try
        {
            SetBusy(true);
            await action();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool CanLoadReport()
    {
        return !_isBusy
            && _reportPaths.Count > 0
            && _reportPaths.All(File.Exists);
    }

    private bool CanUseEmbyApi()
    {
        return !_isBusy && HasEmbyApiSettings();
    }

    private bool HasEmbyApiSettings()
    {
        return !string.IsNullOrWhiteSpace(ServerUrl)
            && !string.IsNullOrWhiteSpace(ApiKey);
    }

    private bool CanRunSync()
    {
        return CanUseEmbyApi() && Items.Any(item => item.IsSelected);
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
        OnPropertyChanged(nameof(MissingIdCount));
        OnPropertyChanged(nameof(SummaryText));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        SelectReportCommand.RaiseCanExecuteChanged();
        LoadReportCommand.RaiseCanExecuteChanged();
        TestConnectionCommand.RaiseCanExecuteChanged();
        AnalyzeItemsCommand.RaiseCanExecuteChanged();
        RunSyncCommand.RaiseCanExecuteChanged();
        SaveSettingsCommand.RaiseCanExecuteChanged();
        SelectAllCommand.RaiseCanExecuteChanged();
        DeselectAllCommand.RaiseCanExecuteChanged();
    }

    private void ItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EmbySyncItemViewModel.IsSelected)
            or nameof(EmbySyncItemViewModel.TvdbId)
            or nameof(EmbySyncItemViewModel.ImdbId))
        {
            RefreshSummaryAndCommands();
        }
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

    private string GetInitialReportDirectory()
    {
        if (_reportPaths.Count > 0)
        {
            var directory = Path.GetDirectoryName(_reportPaths[0]);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                return directory;
            }
        }

        return Directory.Exists(PortableAppStorage.LogsDirectory)
            ? PortableAppStorage.LogsDirectory
            : PortableAppStorage.AppDirectory;
    }

    private static int ScaleProgress(int index, int count, int startPercent, int endPercent)
    {
        if (count <= 0)
        {
            return endPercent;
        }

        var fraction = Math.Clamp((double)index / count, 0, 1);
        return startPercent + (int)Math.Round((endPercent - startPercent) * fraction);
    }

    private void SetReportPaths(IEnumerable<string> reportPaths)
    {
        _reportPaths.Clear();
        _reportPaths.AddRange(reportPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
        ReportPath = _reportPaths.Count switch
        {
            0 => string.Empty,
            1 => _reportPaths[0],
            _ => string.Join(Environment.NewLine, _reportPaths)
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
