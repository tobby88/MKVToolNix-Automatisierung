using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Emby;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels.Commands;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Verwaltet den nachgelagerten Emby-Abgleich für neu erzeugte MKV-Dateien und deren NFO-Provider-IDs.
/// </summary>
internal sealed class EmbySyncViewModel : INotifyPropertyChanged, IGlobalSettingsAwareModule
{
    private readonly EmbyModuleServices _services;
    private readonly IUserDialogService _dialogService;
    private readonly IEmbyProviderReviewDialogService _providerReviewDialogs;
    private readonly IModuleLogService? _moduleLogs;
    private readonly ObservableCollection<EmbySyncItemViewModel> _items = [];
    private readonly List<string> _reportPaths = [];

    private EmbySyncItemViewModel? _selectedItem;
    private string _reportPath = string.Empty;
    private string _statusText = "Bereit";
    private string _logText = string.Empty;
    private int _progressValue;
    private bool _isBusy;

    public EmbySyncViewModel(
        EmbyModuleServices services,
        IUserDialogService dialogService,
        IEmbyProviderReviewDialogService? providerReviewDialogs = null,
        IModuleLogService? moduleLogs = null)
    {
        _services = services;
        _dialogService = dialogService;
        _providerReviewDialogs = providerReviewDialogs ?? new EmbyProviderReviewDialogService();
        _moduleLogs = moduleLogs;
        Action<Exception> unexpectedCommandErrorHandler = ex => _dialogService.ShowError($"Unerwarteter Fehler:\n\n{ex.Message}");

        SelectReportCommand = new AsyncRelayCommand(SelectReportAsync, () => !_isBusy, unexpectedCommandErrorHandler);
        RunScanCommand = new AsyncRelayCommand(RunScanAsync, CanRunScan, unexpectedCommandErrorHandler);
        ToggleSelectedItemSelectionCommand = new RelayCommand(ToggleSelectedItemSelection, () => !_isBusy && SelectedItem is not null);
        ReviewSelectedMetadataCommand = new AsyncRelayCommand(ReviewSelectedMetadataAsync, CanReviewSelectedMetadata, unexpectedCommandErrorHandler);
        ReviewSelectedImdbCommand = new RelayCommand(ReviewSelectedImdb, CanReviewSelectedImdb);
        ReviewPendingProviderIdsCommand = new AsyncRelayCommand(ReviewPendingProviderIdsAsync, CanReviewPendingProviderIds, unexpectedCommandErrorHandler);
        RunSyncCommand = new AsyncRelayCommand(RunSyncAsync, CanRunSync, unexpectedCommandErrorHandler);
        SelectAllCommand = new RelayCommand(SelectAllRunnable, () => !_isBusy && Items.Any(item => !item.IsSelected));
        DeselectAllCommand = new RelayCommand(DeselectAll, () => !_isBusy && Items.Any(item => item.IsSelected));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncRelayCommand SelectReportCommand { get; }

    public AsyncRelayCommand RunScanCommand { get; }

    public RelayCommand ToggleSelectedItemSelectionCommand { get; }

    public AsyncRelayCommand ReviewSelectedMetadataCommand { get; }

    public RelayCommand ReviewSelectedImdbCommand { get; }

    public AsyncRelayCommand ReviewPendingProviderIdsCommand { get; }

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
    
    public int IncompleteIdCount => Items.Count(item => item.SupportsProviderIdSync && !item.HasCompleteProviderIds);

    public string SummaryText => ItemCount == 0
        ? "Noch kein Metadatenreport geladen."
        : $"{ItemCount} Datei(en), {SelectedCount} ausgewählt, {IncompleteIdCount} ohne vollständige TVDB-/IMDB-ID.";

    /// <summary>
    /// Kompakte Zusammenfassung der aktuell geladenen Reportauswahl für die Kopfzeile.
    /// </summary>
    public string ReportSelectionSummaryText => _reportPaths.Count switch
    {
        0 => "Noch kein Metadatenreport gewählt.",
        1 => Path.GetFileName(_reportPaths[0]),
        _ => $"{_reportPaths.Count} Metadatenreports ausgewählt"
    };

    /// <summary>
    /// Zweite, bewusst kurze Zeile unterhalb der Reportzusammenfassung.
    /// </summary>
    public string ReportSelectionDetailText => _reportPaths.Count switch
    {
        0 => "Wählt einen oder mehrere .metadata.json-Reports. Die lokale Prüfung startet danach automatisch.",
        1 => _reportPaths[0],
        _ => BuildReportSelectionDetailText()
    };

    /// <summary>
    /// Zeigt bei Bedarf die vollständige aktuelle Reportliste im Tooltip an.
    /// </summary>
    public string ReportSelectionTooltip => _reportPaths.Count == 0
        ? ReportSelectionDetailText
        : string.Join(Environment.NewLine, _reportPaths);

    public string RunScanTooltip => "Startet bevorzugt den zur Archivwurzel passenden Emby-Serienbibliotheksscan, beobachtet dessen Serverfortschritt und liest danach NFO und Emby-Items erneut ein. So können neue Emby-Treffer vor dem abschließenden Schreibschritt noch geprüft oder korrigiert werden.";

    public string ReviewPendingProviderIdsTooltip => "Arbeitet die offenen Provider-ID-Pflichtprüfungen der ausgewählten Zeilen sequenziell ab: TVDB nur bei widersprüchlichen Quellen, IMDb grundsätzlich pro Episode.";

    public string RunSyncTooltip => "Letzter Schritt: Schreibt die aktuell ausgewählten TVDB-/IMDB-Änderungen ohne zusätzlichen Bibliotheksscan in die lokalen NFO-Dateien. Wenn Emby-Zugangsdaten vorhanden sind und das Emby-Item bereits bekannt ist, wird danach nur für tatsächlich geänderte Einträge ein gezielter Metadatenrefresh angestoßen.";

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
            .Select(reportPath => new
            {
                ReportPath = reportPath,
                LastWriteTimeUtc = File.GetLastWriteTimeUtc(reportPath),
                Entries = _services.Sync.LoadNewOutputReport(reportPath)
            })
            .OrderBy(report => report.LastWriteTimeUtc)
            .ThenBy(report => report.ReportPath, StringComparer.OrdinalIgnoreCase)
            .SelectMany(report => report.Entries)
            .GroupBy(entry => entry.MediaFilePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Aggregate(MergeImportEntries))
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

        if (importEntries.Count == 0)
        {
            SaveVisibleLog("Reports prüfen");
            return Task.CompletedTask;
        }

        return AnalyzeItemsAsync();
    }

    private async Task AnalyzeItemsAsync()
    {
        if (Items.Count == 0)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var settings = LoadConfiguredSettings();
            var canQueryEmby = HasEmbyApiSettings();
            var archiveRootPath = _services.ArchiveSettings.Load().DefaultSeriesArchiveRootPath;
            EmbyLibraryMatch? libraryMatch = null;
            if (canQueryEmby)
            {
                try
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
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    canQueryEmby = false;
                    AppendLog($"Emby-Abfrage nicht möglich: {ex.Message}. Prüfe lokale NFO-Dateien ohne Emby-Itemdaten.");
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

                var analysis = await AnalyzeFileWithLocalFallbackAsync(
                    item,
                    settings,
                    canQueryEmby,
                    archiveRootPath,
                    libraryMatch,
                    "Reports prüfen");
                item.ApplyAnalysis(analysis);
            }

            ProgressValue = 100;
            StatusText = $"Prüfung abgeschlossen: {items.Count} Datei(en), {MissingIdCount} ohne Provider-ID";
            AppendLog(StatusText);
            RefreshSummaryAndCommands();
            SaveVisibleLog("Reports prüfen");
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
            SaveVisibleLog("Emby scannen");
        });
    }

    private async Task ReviewSelectedMetadataAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        if (!SelectedItem.CanReviewTvdb)
        {
            _dialogService.ShowWarning(
                "TVDB-Prüfung",
                "Die ausgewählte Zeile hat keine TVDB-ID und der Dateiname kann nicht automatisch in die TVDB-Suche übernommen werden.");
            return;
        }

        var reviewApplied = ApplyTvdbReviewResult(
            SelectedItem,
            _providerReviewDialogs.ReviewTvdb(SelectedItem, _services.EpisodeMetadata, _services.SettingsDialog),
            isBatchReview: false);
        SaveVisibleLog("TVDB prüfen");
        if (reviewApplied)
        {
            RefreshSummaryAndCommands();
        }
    }

    private async Task ReviewPendingProviderIdsAsync()
    {
        var selectedItems = Items
            .Where(item => item.IsSelected && item.SupportsProviderIdSync)
            .ToList();
        if (selectedItems.Count == 0)
        {
            _dialogService.ShowWarning("Emby-Abgleich", "Es sind keine prüfbaren Dateien ausgewählt.");
            return;
        }

        var processedAny = false;
        foreach (var item in selectedItems.Where(item => item.RequiresTvdbReview).ToList())
        {
            SelectedItem = item;
            StatusText = $"Prüfe TVDB-Zuordnung: {item.MediaFileName}";
            processedAny = true;

            if (!ApplyTvdbReviewResult(
                    item,
                    _providerReviewDialogs.ReviewTvdb(item, _services.EpisodeMetadata, _services.SettingsDialog),
                    isBatchReview: true))
            {
                StatusText = "Provider-ID-Pflichtprüfung abgebrochen.";
                RefreshSummaryAndCommands();
                return;
            }

            await Task.Yield();
        }

        foreach (var item in selectedItems.Where(item => item.RequiresImdbReview).ToList())
        {
            SelectedItem = item;
            StatusText = $"Prüfe IMDb-Zuordnung: {item.MediaFileName}";
            processedAny = true;

            if (!ApplyImdbReviewResult(
                    item,
                    _providerReviewDialogs.ReviewImdb(item, _services.ImdbLookup, _services.EpisodeMetadata.LoadSettings().ImdbLookupMode),
                    isBatchReview: true))
            {
                StatusText = "Provider-ID-Pflichtprüfung abgebrochen.";
                RefreshSummaryAndCommands();
                return;
            }

            await Task.Yield();
        }

        StatusText = processedAny
            ? "Provider-ID-Pflichtprüfungen abgeschlossen."
            : "Keine offenen Provider-ID-Pflichtprüfungen.";
        AppendLog(StatusText);
        RefreshSummaryAndCommands();
        SaveVisibleLog("Pflichtchecks");
    }

    private async Task RunSyncAsync()
    {
        var selectedItems = Items.Where(item => item.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            _dialogService.ShowWarning("Emby-Abgleich", "Es sind keine Dateien für den abschließenden Schreibschritt ausgewählt.");
            return;
        }

        var pendingReviewCount = selectedItems.Count(item => item.HasPendingProviderReview);
        if (pendingReviewCount > 0)
        {
            _dialogService.ShowWarning(
                "Emby-Abgleich",
                $"Vor dem Schreiben sind noch {pendingReviewCount} Provider-ID-Pflichtprüfung(en) offen. Bitte zuerst \"Pflichtchecks starten\" ausführen.");
            return;
        }

        await RunBusyAsync(async () =>
        {
            var settings = LoadConfiguredSettings();
            var canRefreshEmby = HasEmbyApiSettings();
            var updatedCount = 0;
            var refreshOnlyCount = 0;
            var skippedCount = 0;
            var refreshFailureCount = 0;
            var completedMediaFilePaths = new List<string>();

            for (var index = 0; index < selectedItems.Count; index++)
            {
                var item = selectedItems[index];
                StatusText = $"Aktualisiere NFO {index + 1}/{selectedItems.Count}...";
                ProgressValue = ScaleProgress(index, selectedItems.Count, 10, 95);

                if (!item.SupportsProviderIdSync)
                {
                    item.SetStatus("Ohne NFO-Sync", "Für diesen Emby-Eintrag gibt es keine Episoden-NFO. Ein TVDB-/IMDB-Sync ist hier nicht nötig.");
                    completedMediaFilePaths.Add(item.MediaFilePath);
                    continue;
                }

                if (!item.ProviderIds.HasAny && !item.IsImdbUnavailable)
                {
                    item.SetStatus("Übersprungen", "Keine TVDB- oder IMDB-ID vorhanden. Bitte IDs manuell ergänzen oder Emby-Metadaten prüfen.");
                    skippedCount++;
                    continue;
                }

                if (!item.HasValidProviderIds)
                {
                    item.MarkInvalidProviderIds();
                    skippedCount++;
                    continue;
                }

                var updateResult = _services.Sync.UpdateNfoProviderIds(
                    item.MediaFilePath,
                    item.ProviderIds,
                    removeImdbId: item.IsImdbUnavailable);
                if (!updateResult.Success)
                {
                    item.SetStatus("NFO prüfen", updateResult.Message);
                    skippedCount++;
                    continue;
                }

                if (!updateResult.NfoChanged)
                {
                    if (item.HasCompleteProviderIds)
                    {
                        if (canRefreshEmby && item.HasKnownEmbyProviderIdMismatch)
                        {
                            try
                            {
                                await _services.Sync.RefreshItemMetadataAsync(settings, item.EmbyItemId);
                                refreshOnlyCount++;
                                item.MarkCurrentAndRefreshed();
                                completedMediaFilePaths.Add(item.MediaFilePath);
                            }
                            catch (Exception ex)
                            {
                                refreshFailureCount++;
                                item.MarkCurrentRefreshFailed(ex.Message);
                                AppendLog($"Emby-Refresh fehlgeschlagen: {item.MediaFileName} -> {ex.Message}");
                            }

                            continue;
                        }

                        item.SetStatus("NFO aktuell", updateResult.Message);
                        completedMediaFilePaths.Add(item.MediaFilePath);
                    }
                    else
                    {
                        item.MarkCurrentWithMissingIds();
                    }
                    continue;
                }

                updatedCount++;

                if (!canRefreshEmby)
                {
                    item.MarkUpdated(
                        metadataRefreshTriggered: false,
                        noRefreshReason: string.IsNullOrWhiteSpace(item.EmbyItemId)
                            ? "Emby-Item noch nicht gefunden."
                            : "Emby-Refresh nicht ausgeführt, weil keine Emby-API-Zugangsdaten konfiguriert sind.");
                    completedMediaFilePaths.Add(item.MediaFilePath);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.EmbyItemId))
                {
                    item.MarkUpdated(metadataRefreshTriggered: false);
                    completedMediaFilePaths.Add(item.MediaFilePath);
                    continue;
                }

                try
                {
                    await _services.Sync.RefreshItemMetadataAsync(settings, item.EmbyItemId);
                    item.MarkUpdated(metadataRefreshTriggered: true);
                    completedMediaFilePaths.Add(item.MediaFilePath);
                }
                catch (Exception ex)
                {
                    refreshFailureCount++;
                    item.MarkRefreshFailed(ex.Message);
                    AppendLog($"Emby-Refresh fehlgeschlagen: {item.MediaFileName} -> {ex.Message}");
                }
            }

            ProgressValue = 100;
            StatusText = BuildRunSyncSummary(updatedCount, refreshOnlyCount, skippedCount, refreshFailureCount, canRefreshEmby);
            AppendLog(StatusText);
            MarkSelectedReportsDone(completedMediaFilePaths);
            RefreshSummaryAndCommands();
            SaveVisibleLog("Änderungen schreiben");
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
            var analysis = await AnalyzeFileWithLocalFallbackAsync(
                item,
                settings,
                queryEmby: true,
                archiveRootPath,
                libraryMatch,
                "Nach Emby-Scan");
            item.ApplyAnalysis(analysis);
        }
    }

    /// <summary>
    /// Liest immer zuerst die lokale NFO und degradiert nur den optionalen Emby-Anteil,
    /// wenn der Server während der Prüfung nicht erreichbar ist.
    /// </summary>
    private async Task<EmbyFileAnalysis> AnalyzeFileWithLocalFallbackAsync(
        EmbySyncItemViewModel item,
        AppEmbySettings settings,
        bool queryEmby,
        string? archiveRootPath,
        EmbyLibraryMatch? libraryMatch,
        string operationLabel)
    {
        try
        {
            return await _services.Sync.AnalyzeFileAsync(
                settings,
                item.MediaFilePath,
                queryEmby,
                archiveRootPath,
                libraryMatch,
                CancellationToken.None);
        }
        catch (Exception ex) when (queryEmby && ex is not OperationCanceledException)
        {
            AppendLog($"{operationLabel}: Emby-Abfrage für {item.MediaFileName} fehlgeschlagen: {ex.Message}. Verwende lokale NFO-Daten.");
            return _services.Sync.AnalyzeLocalFile(item.MediaFilePath);
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

    private void ToggleSelectedItemSelection()
    {
        if (_isBusy || SelectedItem is not EmbySyncItemViewModel item)
        {
            return;
        }

        item.IsSelected = !item.IsSelected;
    }

    private void MarkSelectedReportsDone(IReadOnlyList<string> completedMediaFilePaths)
    {
        if (completedMediaFilePaths.Count == 0 || _reportPaths.Count == 0)
        {
            return;
        }

        var completion = _services.Sync.MarkOutputReportsDone(_reportPaths, completedMediaFilePaths);
        if (completion.UpdatedReportPaths.Count > 0)
        {
            AppendLog($"Metadatenreport aktualisiert: {completion.UpdatedReportPaths.Count} teilweise erledigt.");
        }

        if (completion.MovedReports.Count > 0)
        {
            foreach (var movedReport in completion.MovedReports)
            {
                ReplaceReportPath(movedReport.SourcePath, movedReport.TargetPath);
                AppendLog($"Metadatenreport erledigt und verschoben: {movedReport.TargetPath}");
            }

            OnPropertyChanged(nameof(ReportSelectionSummaryText));
            OnPropertyChanged(nameof(ReportSelectionDetailText));
            OnPropertyChanged(nameof(ReportSelectionTooltip));
            ReportPath = _reportPaths.Count switch
            {
                0 => string.Empty,
                1 => _reportPaths[0],
                _ => string.Join(Environment.NewLine, _reportPaths)
            };
        }

        foreach (var failedReport in completion.FailedReports)
        {
            AppendLog($"Metadatenreport konnte nicht als erledigt markiert werden: {failedReport}");
        }
    }

    private void ReplaceReportPath(string sourcePath, string targetPath)
    {
        var index = _reportPaths.FindIndex(path => string.Equals(path, sourcePath, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _reportPaths[index] = targetPath;
        }
    }

    public void HandleGlobalSettingsChanged()
    {
        RefreshCommands();
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
        return !_isBusy && SelectedItem?.CanReviewTvdb == true;
    }

    private bool CanReviewSelectedImdb()
    {
        return !_isBusy && SelectedItem?.CanReviewImdb == true;
    }

    private bool CanReviewPendingProviderIds()
    {
        return !_isBusy && Items.Any(item => item.IsSelected && item.HasPendingProviderReview);
    }

    private bool CanRunSync()
    {
        return !_isBusy && Items.Any(item => item.IsSelected);
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
        OnPropertyChanged(nameof(IncompleteIdCount));
        OnPropertyChanged(nameof(SummaryText));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        SelectReportCommand.RaiseCanExecuteChanged();
        RunScanCommand.RaiseCanExecuteChanged();
        ToggleSelectedItemSelectionCommand.RaiseCanExecuteChanged();
        ReviewSelectedMetadataCommand.RaiseCanExecuteChanged();
        ReviewSelectedImdbCommand.RaiseCanExecuteChanged();
        ReviewPendingProviderIdsCommand.RaiseCanExecuteChanged();
        RunSyncCommand.RaiseCanExecuteChanged();
        SelectAllCommand.RaiseCanExecuteChanged();
        DeselectAllCommand.RaiseCanExecuteChanged();
    }

    private void ItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EmbySyncItemViewModel.IsSelected)
            or nameof(EmbySyncItemViewModel.TvdbId)
            or nameof(EmbySyncItemViewModel.ImdbId)
            or nameof(EmbySyncItemViewModel.HasCompleteProviderIds)
            or nameof(EmbySyncItemViewModel.SupportsProviderIdSync)
            or nameof(EmbySyncItemViewModel.HasPendingProviderReview)
            or nameof(EmbySyncItemViewModel.RequiresTvdbReview)
            or nameof(EmbySyncItemViewModel.RequiresImdbReview))
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

    /// <summary>
    /// Schreibt den sichtbaren Emby-Abgleichsverlauf nach abgeschlossenen Workflow-Schritten.
    /// </summary>
    private void SaveVisibleLog(string operationLabel)
    {
        if (_moduleLogs is null || string.IsNullOrWhiteSpace(LogText))
        {
            return;
        }

        try
        {
            _moduleLogs.SaveModuleLog("Emby-Abgleich", operationLabel, ReportPath, LogText);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogService.ShowWarning("Protokoll", $"Das Emby-Protokoll konnte nicht gespeichert werden.\n\n{ex.Message}");
        }
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
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reportPath in reportPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (seenPaths.Add(reportPath))
            {
                _reportPaths.Add(reportPath);
            }
        }

        ReportPath = _reportPaths.Count switch
        {
            0 => string.Empty,
            1 => _reportPaths[0],
            _ => string.Join(Environment.NewLine, _reportPaths)
        };
        OnPropertyChanged(nameof(ReportSelectionSummaryText));
        OnPropertyChanged(nameof(ReportSelectionDetailText));
        OnPropertyChanged(nameof(ReportSelectionTooltip));
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

    private void ReviewSelectedImdb()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var reviewApplied = ApplyImdbReviewResult(
            SelectedItem,
            _providerReviewDialogs.ReviewImdb(SelectedItem, _services.ImdbLookup, _services.EpisodeMetadata.LoadSettings().ImdbLookupMode),
            isBatchReview: false);
        SaveVisibleLog("IMDb prüfen");
        if (reviewApplied)
        {
            RefreshSummaryAndCommands();
        }
    }

    private bool ApplyTvdbReviewResult(
        EmbySyncItemViewModel item,
        EmbyTvdbReviewResult result,
        bool isBatchReview)
    {
        switch (result.Kind)
        {
            case EmbyProviderReviewResultKind.KeepCurrent:
                item.ApproveCurrentTvdbId();
                AppendLog($"TVDB geprüft und aktuelle ID beibehalten: {item.MediaFileName}");
                StatusText = isBatchReview ? "TVDB-Zuordnung geprüft." : "TVDB-Zuordnung aktualisiert.";
                return true;
            case EmbyProviderReviewResultKind.Applied when result.Selection is not null:
                item.ApplyTvdbSelection(result.Selection);
                AppendLog(
                    $"TVDB manuell gesetzt: {item.MediaFileName} -> "
                    + $"S{result.Selection.SeasonNumber}E{result.Selection.EpisodeNumber} - {result.Selection.EpisodeTitle}");
                StatusText = isBatchReview ? "TVDB-Zuordnung geprüft." : "TVDB-Zuordnung aktualisiert.";
                return true;
            case EmbyProviderReviewResultKind.Unavailable:
                item.SetStatus("NFO prüfen", "TVDB-Prüfung nicht möglich: Dateiname liefert keine Serien-/Episodenvorbelegung.");
                AppendLog($"TVDB-Prüfung nicht möglich: {item.MediaFileName}");
                return false;
            default:
                AppendLog($"TVDB-Prüfung abgebrochen: {item.MediaFileName}");
                return false;
        }
    }

    private bool ApplyImdbReviewResult(
        EmbySyncItemViewModel item,
        EmbyImdbReviewResult result,
        bool isBatchReview)
    {
        switch (result.Kind)
        {
            case EmbyProviderReviewResultKind.Applied when !string.IsNullOrWhiteSpace(result.ImdbId):
                item.ApplyImdbSelection(result.ImdbId!);
                AppendLog($"IMDb manuell gesetzt: {item.MediaFileName} -> {result.ImdbId}");
                StatusText = isBatchReview ? "IMDb-Zuordnung geprüft." : "IMDb-Zuordnung aktualisiert.";
                return true;
            case EmbyProviderReviewResultKind.NoImdbId:
                item.MarkImdbUnavailable();
                AppendLog($"IMDb bewusst leer gelassen: {item.MediaFileName}");
                StatusText = isBatchReview ? "IMDb-Zuordnung geprüft." : "IMDb-Zuordnung aktualisiert.";
                return true;
            default:
                AppendLog($"IMDb-Prüfung abgebrochen: {item.MediaFileName}");
                return false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Führt Dubletten aus mehreren Reports zu einem stabilen Zielzustand zusammen.
    /// </summary>
    /// <remarks>
    /// Die Reports werden vorab chronologisch aufsteigend geladen. Beim Zusammenführen hat daher der
    /// zeitlich spätere Report Vorrang, fehlende Provider-IDs werden aber weiterhin aus älteren Reports
    /// übernommen. So gewinnt ein neuerer, vollständigerer Import, ohne ergänzende ältere Daten zu verlieren.
    /// </remarks>
    private static EmbyImportEntry MergeImportEntries(EmbyImportEntry earlierEntry, EmbyImportEntry laterEntry)
    {
        return new EmbyImportEntry(
            laterEntry.MediaFilePath,
            new EmbyProviderIds(
                PickPreferredProviderId(earlierEntry.ProviderIds.TvdbId, laterEntry.ProviderIds.TvdbId),
                PickPreferredProviderId(earlierEntry.ProviderIds.ImdbId, laterEntry.ProviderIds.ImdbId)));
    }

    private static string? PickPreferredProviderId(string? earlierValue, string? laterValue)
    {
        return string.IsNullOrWhiteSpace(laterValue) ? earlierValue : laterValue;
    }

    private static string BuildRunSyncSummary(
        int updatedCount,
        int refreshOnlyCount,
        int skippedCount,
        int refreshFailureCount,
        bool canRefreshEmby)
    {
        var parts = new List<string>
        {
            $"Änderungen geschrieben: {updatedCount} aktualisiert, {skippedCount} übersprungen."
        };
        if (refreshOnlyCount > 0)
        {
            parts.Add($"{refreshOnlyCount} Emby-Refresh ohne NFO-Änderung.");
        }

        if (refreshFailureCount > 0)
        {
            parts.Add($"{refreshFailureCount} Emby-Refresh-Fehler.");
        }

        if (!canRefreshEmby)
        {
            parts.Add("Emby-Refresh wurde wegen fehlender API-Zugangsdaten nicht ausgeführt.");
        }

        return string.Join(" ", parts);
    }

    private string BuildReportSelectionDetailText()
    {
        var reportNames = _reportPaths
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(3)
            .ToList();
        var remainingCount = _reportPaths.Count - reportNames.Count;
        var visiblePart = string.Join(" | ", reportNames);
        return remainingCount > 0
            ? $"{visiblePart} | +{remainingCount} weitere"
            : visiblePart;
    }
}
