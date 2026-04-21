using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Emby;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels.Commands;
using MkvToolnixAutomatisierung.Windows;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Verwaltet den nachgelagerten Emby-Abgleich für neu erzeugte MKV-Dateien und deren NFO-Provider-IDs.
/// </summary>
internal sealed class EmbySyncViewModel : INotifyPropertyChanged, IGlobalSettingsAwareModule
{
    private readonly EmbyModuleServices _services;
    private readonly IUserDialogService _dialogService;
    private readonly ObservableCollection<EmbySyncItemViewModel> _items = [];
    private readonly List<string> _reportPaths = [];

    private EmbySyncItemViewModel? _selectedItem;
    private string _reportPath = string.Empty;
    private string _statusText = "Bereit";
    private string _logText = string.Empty;
    private int _progressValue;
    private bool _isBusy;

    public EmbySyncViewModel(EmbyModuleServices services, IUserDialogService dialogService)
    {
        _services = services;
        _dialogService = dialogService;
        Action<Exception> unexpectedCommandErrorHandler = ex => _dialogService.ShowError($"Unerwarteter Fehler:\n\n{ex.Message}");

        SelectReportCommand = new AsyncRelayCommand(SelectReportAsync, () => !_isBusy, unexpectedCommandErrorHandler);
        OpenSettingsCommand = new RelayCommand(OpenSettings, () => !_isBusy);
        AnalyzeItemsCommand = new AsyncRelayCommand(AnalyzeItemsAsync, () => !_isBusy && Items.Count > 0, unexpectedCommandErrorHandler);
        RunScanCommand = new AsyncRelayCommand(RunScanAsync, CanRunScan, unexpectedCommandErrorHandler);
        ReviewSelectedMetadataCommand = new AsyncRelayCommand(ReviewSelectedMetadataAsync, CanReviewSelectedMetadata, unexpectedCommandErrorHandler);
        RunSyncCommand = new AsyncRelayCommand(RunSyncAsync, CanRunSync, unexpectedCommandErrorHandler);
        SelectAllCommand = new RelayCommand(SelectAllRunnable, () => !_isBusy && Items.Any(item => !item.IsSelected));
        DeselectAllCommand = new RelayCommand(DeselectAll, () => !_isBusy && Items.Any(item => item.IsSelected));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncRelayCommand SelectReportCommand { get; }

    public RelayCommand OpenSettingsCommand { get; }

    public AsyncRelayCommand AnalyzeItemsCommand { get; }

    public AsyncRelayCommand RunScanCommand { get; }

    public AsyncRelayCommand ReviewSelectedMetadataCommand { get; }

    public AsyncRelayCommand RunSyncCommand { get; }

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

    public int MissingIdCount => Items.Count(item => item.SupportsProviderIdSync && !item.HasProviderIds);

    public string SummaryText => ItemCount == 0
        ? "Noch kein Metadatenreport geladen."
        : $"{ItemCount} Datei(en), {SelectedCount} ausgewählt, {MissingIdCount} ohne erwartete TVDB-/IMDB-ID.";

    public string AnalyzeItemsTooltip => HasEmbyApiSettings()
        ? "Prüft lokale NFO-Dateien und liest zusätzlich die aktuell in Emby sichtbaren Episoden samt Provider-IDs ein."
        : "Prüft nur die lokalen NFO-Dateien. Server und API-Key werden zentral im Einstellungsdialog gepflegt.";

    public string RunScanTooltip => "Startet bevorzugt den zur Archivwurzel passenden Emby-Serienbibliotheksscan, beobachtet dessen Serverfortschritt und liest danach NFO und Emby-Items erneut ein. So können neue Emby-Treffer vor dem eigentlichen NFO-Sync noch geprüft oder korrigiert werden.";

    public string RunSyncTooltip => "Schreibt die aktuell sichtbaren TVDB-/IMDB-IDs ohne zusätzlichen Bibliotheksscan in die lokalen NFO-Dateien und stößt danach nur für tatsächlich geänderte Emby-Items einen gezielten Metadatenrefresh an.";

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
        await ImportSelectedReportsAsync();
    }

    private Task ImportSelectedReportsAsync()
    {
        if (_reportPaths.Count == 0 || _reportPaths.Any(path => !File.Exists(path)))
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
            var settings = LoadConfiguredSettings();
            var canQueryEmby = queryEmby && HasEmbyApiSettings();
            var archiveRootPath = _services.ArchiveSettings.Load().DefaultSeriesArchiveRootPath;
            EmbyLibraryMatch? libraryMatch = null;
            if (canQueryEmby)
            {
                libraryMatch = await _services.Sync.FindSeriesLibraryAsync(settings, archiveRootPath, CancellationToken.None);
                if (libraryMatch is null)
                {
                    AppendLog($"Keine passende Emby-Serienbibliothek zur Archivwurzel gefunden: {archiveRootPath}");
                }
                else
                {
                    AppendLog($"Emby-Serienbibliothek erkannt: {libraryMatch.Library.Name} ({libraryMatch.MatchedLocation})");
                }
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
                    archiveRootPath,
                    libraryMatch,
                    CancellationToken.None);
                item.ApplyAnalysis(analysis);
            }

            ProgressValue = 100;
            StatusText = $"Prüfung abgeschlossen: {items.Count} Datei(en), {MissingIdCount} ohne Provider-ID";
            AppendLog(StatusText);
            RefreshSummaryAndCommands();
        });
    }

    private async Task RunScanAsync()
    {
        var selectedItems = Items.Where(item => item.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            _dialogService.ShowWarning("Emby-Abgleich", "Es sind keine Dateien für den Emby-Scan ausgewählt.");
            return;
        }

        await RunBusyAsync(async () =>
        {
            var settings = LoadConfiguredSettings();
            var archiveSettings = _services.ArchiveSettings.Load();
            EmbyLibraryMatch? libraryMatch = null;

            StatusText = "Starte Emby-Scan...";
            ProgressValue = 5;
            var scanTrigger = await _services.Sync.TriggerSeriesLibraryScanAsync(
                settings,
                archiveSettings.DefaultSeriesArchiveRootPath);
            ProgressValue = 10;
            AppendLog(scanTrigger.Message);
            if (scanTrigger.UsedGlobalLibraryScan)
            {
                AppendLog("Hinweis: Für den globalen Emby-Fallback ist derzeit kein bibliotheksscharfer Serverfortschritt verfügbar.");
            }
            else if (scanTrigger.Library is not null && !string.IsNullOrWhiteSpace(scanTrigger.MatchedLibraryPath))
            {
                libraryMatch = new EmbyLibraryMatch(scanTrigger.Library, scanTrigger.MatchedLibraryPath!);
                await WaitForSeriesLibraryScanAsync(settings, scanTrigger.Library);
            }

            StatusText = "Warte auf neue Emby-Items für den lokalen NFO-Abgleich...";
            ProgressValue = Math.Max(ProgressValue, 50);
            await ResolveEmbyItemsWithinBudgetAsync(settings, selectedItems, archiveSettings.DefaultSeriesArchiveRootPath, libraryMatch);
            await RefreshSelectedAnalysesAfterLibraryScanAsync(settings, selectedItems, archiveSettings.DefaultSeriesArchiveRootPath, libraryMatch);

            ProgressValue = 100;
            StatusText = scanTrigger.UsedGlobalLibraryScan
                ? "Emby-Scan und Nachprüfung abgeschlossen. Der globale Server-Scan kann noch im Hintergrund weiterlaufen."
                : "Emby-Scan und Nachprüfung abgeschlossen.";
            AppendLog(StatusText);
            RefreshSummaryAndCommands();
        });
    }

    private async Task ReviewSelectedMetadataAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        if (!TryGetSelectedMetadataGuess(out var guess, out var reason))
        {
            _dialogService.ShowWarning("TVDB-Prüfung", reason);
            return;
        }

        var dialog = new TvdbLookupWindow(_services.EpisodeMetadata, guess!, _services.SettingsDialog)
        {
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
                ?? Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() != true)
        {
            AppendLog($"TVDB-Prüfung abgebrochen: {SelectedItem.MediaFileName}");
            return;
        }

        if (dialog.KeepLocalDetection)
        {
            AppendLog($"Lokale TVDB-Erkennung beibehalten: {SelectedItem.MediaFileName}");
            return;
        }

        if (dialog.SelectedEpisodeSelection is null)
        {
            return;
        }

        SelectedItem.ApplyTvdbSelection(dialog.SelectedEpisodeSelection);
        AppendLog(
            $"TVDB manuell gesetzt: {SelectedItem.MediaFileName} -> "
            + $"S{dialog.SelectedEpisodeSelection.SeasonNumber}E{dialog.SelectedEpisodeSelection.EpisodeNumber} - {dialog.SelectedEpisodeSelection.EpisodeTitle}");
        StatusText = "TVDB-Zuordnung aktualisiert. Vor dem NFO-Sync bitte bei Bedarf nochmals prüfen.";
        RefreshSummaryAndCommands();
    }

    private async Task RunSyncAsync()
    {
        var selectedItems = Items.Where(item => item.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            _dialogService.ShowWarning("Emby-Abgleich", "Es sind keine Dateien für den NFO-Sync ausgewählt.");
            return;
        }

        await RunBusyAsync(async () =>
        {
            var settings = LoadConfiguredSettings();
            var updatedCount = 0;
            var skippedCount = 0;

            for (var index = 0; index < selectedItems.Count; index++)
            {
                var item = selectedItems[index];
                StatusText = $"Aktualisiere NFO {index + 1}/{selectedItems.Count}...";
                ProgressValue = ScaleProgress(index, selectedItems.Count, 10, 95);

                if (!item.SupportsProviderIdSync)
                {
                    item.SetStatus("Ohne NFO-Sync", "Für diesen Emby-Eintrag gibt es keine Episoden-NFO. Ein TVDB-/IMDB-Sync ist hier nicht nötig.");
                    continue;
                }

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
            StatusText = $"NFO-Sync abgeschlossen: {updatedCount} aktualisiert, {skippedCount} übersprungen.";
            AppendLog(StatusText);
            RefreshSummaryAndCommands();
        });
    }

    private async Task ResolveEmbyItemsWithinBudgetAsync(
        AppEmbySettings settings,
        IReadOnlyList<EmbySyncItemViewModel> selectedItems,
        string? archiveRootPath,
        EmbyLibraryMatch? libraryMatch)
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
                var embyItem = await _services.Sync.FindItemByPathAsync(
                    settings,
                    item.MediaFilePath,
                    archiveRootPath,
                    libraryMatch);
                if (embyItem is null)
                {
                    continue;
                }

                item.ApplyEmbyItem(embyItem);
                pendingItems.Remove(item);
            }

            ProgressValue = pendingItems.Count == 0
                ? 70
                : Math.Min(69, 50 + attempt * 4);

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
        IReadOnlyList<EmbySyncItemViewModel> selectedItems,
        string? archiveRootPath,
        EmbyLibraryMatch? libraryMatch)
    {
        for (var index = 0; index < selectedItems.Count; index++)
        {
            var item = selectedItems[index];
            StatusText = $"Prüfe NFO nach Emby-Scan {index + 1}/{selectedItems.Count}...";
            ProgressValue = ScaleProgress(index, selectedItems.Count, 70, 80);

            // Nach einem Library-Scan kann Emby die NFO erst jetzt angelegt oder Provider-IDs
            // ergänzt haben. Diese zweite, kurze Prüfung verhindert, dass wir mit veraltetem
            // "NFO fehlt"-Zustand weiterarbeiten.
            var analysis = await _services.Sync.AnalyzeFileAsync(
                settings,
                item.MediaFilePath,
                queryEmby: true,
                archiveRootPath,
                libraryMatch,
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

    public void HandleGlobalSettingsChanged()
    {
        RefreshCommands();
    }

    private void OpenSettings()
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
            ?? Application.Current?.MainWindow;
        if (_services.SettingsDialog.ShowDialog(owner, AppSettingsPage.Emby))
        {
            RefreshCommands();
            StatusText = "Einstellungen aktualisiert.";
        }
    }

    private AppEmbySettings LoadConfiguredSettings()
    {
        return _services.Settings.Load();
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

    private bool HasEmbyApiSettings()
    {
        var settings = LoadConfiguredSettings();
        return !string.IsNullOrWhiteSpace(settings.ServerUrl)
            && !string.IsNullOrWhiteSpace(settings.ApiKey);
    }

    private bool CanRunScan()
    {
        return !_isBusy && HasEmbyApiSettings() && Items.Any(item => item.IsSelected);
    }

    private bool CanReviewSelectedMetadata()
    {
        return !_isBusy && TryGetSelectedMetadataGuess(out _, out _);
    }

    private bool CanRunSync()
    {
        return !_isBusy && HasEmbyApiSettings() && Items.Any(item => item.IsSelected);
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
        OpenSettingsCommand.RaiseCanExecuteChanged();
        AnalyzeItemsCommand.RaiseCanExecuteChanged();
        RunScanCommand.RaiseCanExecuteChanged();
        ReviewSelectedMetadataCommand.RaiseCanExecuteChanged();
        RunSyncCommand.RaiseCanExecuteChanged();
        SelectAllCommand.RaiseCanExecuteChanged();
        DeselectAllCommand.RaiseCanExecuteChanged();
    }

    private void ItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EmbySyncItemViewModel.IsSelected)
            or nameof(EmbySyncItemViewModel.TvdbId)
            or nameof(EmbySyncItemViewModel.ImdbId)
            or nameof(EmbySyncItemViewModel.SupportsProviderIdSync))
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

    private async Task WaitForSeriesLibraryScanAsync(AppEmbySettings settings, EmbyLibraryFolder library)
    {
        var timeoutSeconds = Math.Clamp(settings.ScanWaitTimeoutSeconds, 5, 600);
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var activationDeadline = DateTime.UtcNow.AddSeconds(Math.Min(timeoutSeconds, 10));
        var sawActiveServerProgress = false;

        while (DateTime.UtcNow <= deadline)
        {
            var currentLibrary = await _services.Sync.GetLibraryByIdAsync(settings, library.Id, CancellationToken.None);
            if (currentLibrary is null)
            {
                AppendLog($"Serienbibliothek konnte nach dem Start nicht mehr von Emby gelesen werden: {library.Name}");
                return;
            }

            if (TryGetActiveLibraryScanState(currentLibrary, out var progressPercent, out var refreshStatusText))
            {
                sawActiveServerProgress = true;
                ProgressValue = ScalePercent(progressPercent, 10, 50);
                StatusText = string.IsNullOrWhiteSpace(refreshStatusText)
                    ? $"Serienbibliothek scannt... {progressPercent}%"
                    : $"Serienbibliothek scannt... {progressPercent}% ({refreshStatusText})";
            }
            else if (sawActiveServerProgress)
            {
                ProgressValue = 50;
                StatusText = $"Serienbibliotheksscan abgeschlossen: {currentLibrary.Name}";
                AppendLog(StatusText);
                return;
            }
            else if (DateTime.UtcNow >= activationDeadline)
            {
                AppendLog($"Emby meldet für '{currentLibrary.Name}' keinen expliziten Refresh-Fortschritt. Fahre mit dem lokalen Abgleich fort.");
                ProgressValue = 50;
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        AppendLog($"Timeout beim Warten auf den Emby-Serienbibliotheksscan: {library.Name}");
        ProgressValue = 50;
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

    private static bool TryGetActiveLibraryScanState(
        EmbyLibraryFolder library,
        out int progressPercent,
        out string? refreshStatusText)
    {
        progressPercent = Math.Clamp((int)Math.Round(library.RefreshProgress ?? 0), 0, 100);
        refreshStatusText = string.IsNullOrWhiteSpace(library.RefreshStatus)
            ? null
            : library.RefreshStatus.Trim();

        if (progressPercent is > 0 and < 100)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(refreshStatusText)
            && !IsCompletedLibraryScanStatus(refreshStatusText);
    }

    private static bool IsCompletedLibraryScanStatus(string refreshStatusText)
    {
        return refreshStatusText.Contains("idle", StringComparison.OrdinalIgnoreCase)
            || refreshStatusText.Contains("ready", StringComparison.OrdinalIgnoreCase)
            || refreshStatusText.Contains("complete", StringComparison.OrdinalIgnoreCase)
            || refreshStatusText.Contains("finished", StringComparison.OrdinalIgnoreCase)
            || refreshStatusText.Contains("stopped", StringComparison.OrdinalIgnoreCase);
    }

    private static int ScalePercent(int progressPercent, int startPercent, int endPercent)
    {
        var fraction = Math.Clamp(progressPercent / 100d, 0, 1);
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

    private bool TryGetSelectedMetadataGuess(out EpisodeMetadataGuess? guess, out string reason)
    {
        if (SelectedItem is null)
        {
            guess = null;
            reason = "Bitte zuerst eine MKV-Zeile auswählen.";
            return false;
        }

        if (SelectedItem.TryBuildMetadataGuess(out guess))
        {
            reason = string.Empty;
            return true;
        }

        reason = "Die ausgewählte MKV folgt nicht der erwarteten Episodenbenennung und kann deshalb nicht automatisch in die TVDB-Suche vorbefüllt werden.";
        return false;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
