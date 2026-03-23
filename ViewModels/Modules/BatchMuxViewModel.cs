using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels.Commands;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

public sealed class BatchMuxViewModel : INotifyPropertyChanged
{
    private const string DoneFolderName = "done";
    private const int AutomaticCompareProgressStart = 80;
    private static readonly string[] PreferredDownloadsSubPath = ["MediathekView-latest-win", "Downloads"];

    private readonly AppServices _services;
    private readonly UserDialogService _dialogService;
    private readonly BufferedTextStore _logBuffer;

    private string _sourceDirectory = string.Empty;
    private string _outputDirectory = string.Empty;
    private string _statusText = "Bereit";
    private string _logText = string.Empty;
    private int _progressValue;
    private bool _isBusy;
    private BatchEpisodeItemViewModel? _selectedEpisodeItem;
    private int _selectedPlanSummaryVersion;
    private CancellationTokenSource? _selectedPlanSummaryRefreshCts;
    private string _selectedFilterMode = "Alle";
    private string _selectedSortMode = "Dateiname";

    public BatchMuxViewModel(AppServices services, UserDialogService dialogService)
    {
        _services = services;
        _dialogService = dialogService;
        _logBuffer = new BufferedTextStore(
            flush => _ = Application.Current.Dispatcher.BeginInvoke(flush),
            text => LogText = text);

        EpisodeItems.CollectionChanged += EpisodeItems_CollectionChanged;
        EpisodeItemsView = CollectionViewSource.GetDefaultView(EpisodeItems);
        EpisodeItemsView.Filter = FilterEpisodeItem;

        FilterModes =
        [
            "Alle",
            "Nur offen",
            "Nur neu",
            "Nur vorhanden",
            "Nur Fehler"
        ];

        SortModes =
        [
            "Dateiname",
            "Prüfung zuerst",
            "Status zuerst",
            "Neu zuerst"
        ];

        ApplyEpisodeItemsViewConfiguration();

        SelectSourceDirectoryCommand = new AsyncRelayCommand(SelectSourceDirectoryAsync, () => !_isBusy);
        SelectOutputDirectoryCommand = new RelayCommand(SelectOutputDirectory, () => !_isBusy && !string.IsNullOrWhiteSpace(SourceDirectory));
        ScanDirectoryCommand = new AsyncRelayCommand(ScanDirectoryAsync, () => !_isBusy && !string.IsNullOrWhiteSpace(SourceDirectory));
        SelectAllEpisodesCommand = new RelayCommand(SelectAllEpisodes, () => !_isBusy && EpisodeItems.Any(item => !item.IsSelected));
        DeselectAllEpisodesCommand = new RelayCommand(DeselectAllEpisodes, () => !_isBusy && EpisodeItems.Any(item => item.IsSelected));
        ReviewPendingSourcesCommand = new AsyncRelayCommand(ReviewPendingSourcesAsync, CanReviewPendingSources);
        OpenSelectedSourcesCommand = new RelayCommand(OpenSelectedSources, () => !_isBusy && SelectedEpisodeItem?.SourceFilePaths.Count > 0);
        ReviewSelectedMetadataCommand = new AsyncRelayCommand(ReviewSelectedMetadataAsync, () => !_isBusy && SelectedEpisodeItem is not null);
        RefreshAllComparisonsCommand = new AsyncRelayCommand(RefreshAllComparisonsAsync, () => !_isBusy && EpisodeItems.Any());
        RedetectSelectedEpisodeCommand = new AsyncRelayCommand(RedetectSelectedEpisodeAsync, () => !_isBusy && SelectedEpisodeItem is not null);
        EditSelectedAudioDescriptionCommand = new RelayCommand(EditSelectedAudioDescription, () => !_isBusy && SelectedEpisodeItem is not null);
        EditSelectedSubtitlesCommand = new RelayCommand(EditSelectedSubtitles, () => !_isBusy && SelectedEpisodeItem is not null);
        EditSelectedAttachmentsCommand = new RelayCommand(EditSelectedAttachments, () => !_isBusy && SelectedEpisodeItem is not null);
        EditSelectedOutputCommand = new RelayCommand(EditSelectedOutput, () => !_isBusy && SelectedEpisodeItem is not null);
        RunBatchCommand = new AsyncRelayCommand(RunBatchAsync, () => !_isBusy && EpisodeItems.Any(item => item.IsSelected));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncRelayCommand SelectSourceDirectoryCommand { get; }
    public RelayCommand SelectOutputDirectoryCommand { get; }
    public AsyncRelayCommand ScanDirectoryCommand { get; }
    public RelayCommand SelectAllEpisodesCommand { get; }
    public RelayCommand DeselectAllEpisodesCommand { get; }
    public AsyncRelayCommand ReviewPendingSourcesCommand { get; }
    public RelayCommand OpenSelectedSourcesCommand { get; }
    public AsyncRelayCommand ReviewSelectedMetadataCommand { get; }
    public AsyncRelayCommand RefreshAllComparisonsCommand { get; }
    public AsyncRelayCommand RedetectSelectedEpisodeCommand { get; }
    public RelayCommand EditSelectedAudioDescriptionCommand { get; }
    public RelayCommand EditSelectedSubtitlesCommand { get; }
    public RelayCommand EditSelectedAttachmentsCommand { get; }
    public RelayCommand EditSelectedOutputCommand { get; }
    public AsyncRelayCommand RunBatchCommand { get; }

    public ObservableCollection<BatchEpisodeItemViewModel> EpisodeItems { get; } = [];
    public ICollectionView EpisodeItemsView { get; }
    public IReadOnlyList<string> FilterModes { get; }
    public IReadOnlyList<string> SortModes { get; }

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
        }
    }

    public string SelectedFilterMode
    {
        get => _selectedFilterMode;
        set
        {
            if (_selectedFilterMode == value)
            {
                return;
            }

            _selectedFilterMode = value;
            OnPropertyChanged();
            EpisodeItemsView.Refresh();
        }
    }

    public string SelectedSortMode
    {
        get => _selectedSortMode;
        set
        {
            if (_selectedSortMode == value)
            {
                return;
            }

            _selectedSortMode = value;
            OnPropertyChanged();
            ApplyEpisodeItemsViewConfiguration();
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

    public int EpisodeCount => EpisodeItems.Count;

    public int SelectedEpisodeCount => EpisodeItems.Count(item => item.IsSelected);

    public int ExistingArchiveCount => EpisodeItems.Count(item => item.ArchiveStateText == "vorhanden");

    public int PendingCheckCount => EpisodeItems.Count(item => item.IsSelected && item.HasPendingChecks);

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
            OnPropertyChanged();
            RefreshCommands();
            ScheduleSelectedItemPlanSummaryRefresh();
        }
    }

    private async Task SelectSourceDirectoryAsync()
    {
        var initialDirectory = Directory.Exists(SourceDirectory) ? SourceDirectory : GetPreferredSourceDirectory();
        var path = _dialogService.SelectFolder("Quellordner für den Batch auswählen", initialDirectory);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        SourceDirectory = path;
        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            OutputDirectory = Directory.Exists(SeriesArchiveService.ArchiveRootDirectory)
                ? SeriesArchiveService.ArchiveRootDirectory
                : path;
        }

        ResetLog();
        ClearEpisodeItems();
        StatusText = "Ordner gewählt - starte Scan...";
        RefreshCommands();
        await ScanDirectoryAsync();
    }

    private void SelectOutputDirectory()
    {
        var initialDirectory = Directory.Exists(OutputDirectory) ? OutputDirectory : SourceDirectory;
        var path = _dialogService.SelectFolder("Serienbibliothek für den Batch auswählen", initialDirectory);
        if (!string.IsNullOrWhiteSpace(path))
        {
            OutputDirectory = path;
            RefreshAutomaticOutputPaths();
            if (SelectedEpisodeItem is not null)
            {
                ScheduleSelectedItemPlanSummaryRefresh();
            }
            RefreshCommands();
        }
    }

    private async Task ScanDirectoryAsync()
    {
        try
        {
            SetBusy(true);
            ClearEpisodeItems();
            ResetLog();
            SetStatus("Scanne Ordner...", 0);

            var itemsByEpisodeKey = new Dictionary<string, BatchEpisodeItemViewModel>(StringComparer.OrdinalIgnoreCase);
            var mainVideoFiles = Directory.GetFiles(SourceDirectory, "*.mp4")
                .Where(file => !LooksLikeAudioDescription(file))
                .OrderBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var total = mainVideoFiles.Count;
            if (total == 0)
            {
                SetStatus("Keine passenden Hauptvideos gefunden", 100);
                RefreshCommands();
                return;
            }

            var completedCount = 0;
            var parallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
            using var throttler = new SemaphoreSlim(parallelism);
            var scanResults = new BatchScanResult[total];

            var scanTasks = mainVideoFiles.Select((file, index) => ProcessBatchScanItemAsync(
                file,
                index,
                total,
                throttler,
                scanResults,
                () => Volatile.Read(ref completedCount),
                () =>
                {
                    var processed = Interlocked.Increment(ref completedCount);
                    _ = Application.Current.Dispatcher.BeginInvoke(() =>
                        SetStatus(
                            $"Scanne Ordner... {processed}/{total} abgeschlossen",
                            ScaleProgress(CalculatePercent(processed, total), 0, AutomaticCompareProgressStart)));
                }));

            await Task.WhenAll(scanTasks);

            foreach (var result in scanResults.OrderBy(result => result.Index))
            {
                if (result.ErrorMessage is not null)
                {
                    AddEpisodeItem(BatchEpisodeItemViewModel.CreateErrorItem(result.SourcePath, result.ErrorMessage));
                    AppendLog($"FEHLER: {Path.GetFileName(result.SourcePath)} -> {result.ErrorMessage}");
                    continue;
                }

                var detected = result.Detected!;
                var localGuess = result.LocalGuess!;
                var metadataResolution = result.MetadataResolution!;
                var outputPath = result.OutputPath!;
                var episodeKey = Path.GetFileName(outputPath);

                if (itemsByEpisodeKey.TryGetValue(episodeKey, out var existingItem))
                {
                    existingItem.AddRequestedSource(result.SourcePath);
                    AppendLog($"DUBLETTE: {Path.GetFileName(result.SourcePath)} -> wird bereits über {Path.GetFileName(existingItem.MainVideoPath)} verarbeitet.");
                    continue;
                }

                var outputAlreadyExists = File.Exists(outputPath);
                var item = BatchEpisodeItemViewModel.CreateFromDetection(
                    requestedMainVideoPath: result.SourcePath,
                    localGuess: localGuess,
                    detected: detected,
                    metadataResolution: metadataResolution,
                    outputPath: outputPath,
                    status: outputAlreadyExists ? "Vergleich offen" : "Bereit",
                    isSelected: true);

                AddEpisodeItem(item);
                itemsByEpisodeKey[episodeKey] = item;

                AppendLog(outputAlreadyExists
                    ? $"OK: {Path.GetFileName(result.SourcePath)} -> In der Serienbibliothek bereits vorhanden, wird später genauer verglichen."
                    : $"OK: {Path.GetFileName(result.SourcePath)}");
            }

            var preselectedCount = EpisodeItems.Count(item => item.IsSelected);
            SetStatus(
                $"Scan abgeschlossen: {EpisodeItems.Count} Einträge, {preselectedCount} vorausgewählt",
                AutomaticCompareProgressStart);
            await RefreshComparisonPlansAsync(
                EpisodeItems.Where(item => item.ArchiveStateText == "vorhanden").ToList(),
                automatic: true);
            RefreshCommands();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RedetectSelectedEpisodeAsync()
    {
        var item = SelectedEpisodeItem;
        if (item is null)
        {
            return;
        }

        var initialDirectory = ResolveSelectedItemDirectory(item);
        var path = _dialogService.SelectMainVideo(initialDirectory);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await ApplyDetectionToItemAsync(item, path);
    }

    private void EditSelectedAudioDescription()
    {
        var item = SelectedEpisodeItem;
        if (item is null)
        {
            return;
        }

        var choice = _dialogService.AskAudioDescriptionChoice();
        if (choice == MessageBoxResult.No)
        {
            item.SetAudioDescription(null);
            SetStatus("AD-Datei geleert", ProgressValue);
            ScheduleSelectedItemPlanSummaryRefresh();
            return;
        }

        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        var path = _dialogService.SelectAudioDescription(ResolveSelectedItemDirectory(item));
        if (!string.IsNullOrWhiteSpace(path))
        {
            item.SetAudioDescription(path);
            SetStatus("AD-Datei aktualisiert", ProgressValue);
            ScheduleSelectedItemPlanSummaryRefresh();
        }
    }

    private void EditSelectedSubtitles()
    {
        var item = SelectedEpisodeItem;
        if (item is null)
        {
            return;
        }

        var choice = _dialogService.AskSubtitlesChoice();
        if (choice == MessageBoxResult.No)
        {
            item.SetSubtitles([]);
            SetStatus("Untertitel geleert", ProgressValue);
            ScheduleSelectedItemPlanSummaryRefresh();
            return;
        }

        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        var paths = _dialogService.SelectSubtitles(ResolveSelectedItemDirectory(item));
        if (paths is not null)
        {
            item.SetSubtitles(paths);
            SetStatus("Untertitel aktualisiert", ProgressValue);
            ScheduleSelectedItemPlanSummaryRefresh();
        }
    }

    private void EditSelectedAttachments()
    {
        var item = SelectedEpisodeItem;
        if (item is null)
        {
            return;
        }

        var choice = _dialogService.AskAttachmentChoice();
        if (choice == MessageBoxResult.No)
        {
            item.SetAttachments([]);
            SetStatus("Anhänge geleert", ProgressValue);
            ScheduleSelectedItemPlanSummaryRefresh();
            return;
        }

        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        var paths = _dialogService.SelectAttachments(ResolveSelectedItemDirectory(item));
        if (paths is not null)
        {
            item.SetAttachments(paths);
            SetStatus("Anhänge aktualisiert", ProgressValue);
            ScheduleSelectedItemPlanSummaryRefresh();
        }
    }

    private void EditSelectedOutput()
    {
        var item = SelectedEpisodeItem;
        if (item is null)
        {
            return;
        }

        var path = _dialogService.SelectOutput(
            ResolveSelectedItemDirectory(item),
            string.IsNullOrWhiteSpace(item.OutputFileName) ? "Ausgabe.mkv" : item.OutputFileName);

        if (!string.IsNullOrWhiteSpace(path))
        {
            item.SetOutputPath(path);
            SetStatus("Ausgabedatei aktualisiert", ProgressValue);
            ScheduleSelectedItemPlanSummaryRefresh();
        }
    }

    private void OpenSelectedSources()
    {
        var item = SelectedEpisodeItem;
        if (item is null)
        {
            return;
        }

        _ = ReviewEpisodeAsync(item, isBatchPreparation: false);
    }

    private async Task ReviewSelectedMetadataAsync()
    {
        var item = SelectedEpisodeItem;
        if (item is null)
        {
            return;
        }

        await ReviewEpisodeMetadataAsync(item, isBatchPreparation: false);
    }

    private async Task RefreshAllComparisonsAsync()
    {
        await RefreshComparisonPlansAsync(
            EpisodeItems.Where(item => item.ArchiveStateText == "vorhanden").ToList(),
            automatic: false);
    }

    private async Task RefreshComparisonPlansAsync(
        IReadOnlyList<BatchEpisodeItemViewModel> items,
        bool automatic)
    {
        if (items.Count == 0)
        {
            SetStatus(
                automatic
                    ? "Scan abgeschlossen - keine vorhandenen Bibliotheksdateien zum Vergleichen"
                    : "Keine vorhandenen Bibliotheksdateien ausgewählt",
                automatic ? 100 : ProgressValue);
            return;
        }

        try
        {
            SetBusy(true);

            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                item.Status = "Läuft";
                SetStatus(
                    automatic
                        ? $"Vergleiche vorhandene Bibliotheksdateien... {index + 1}/{items.Count}"
                        : $"Aktualisiere Vergleiche... {index + 1}/{items.Count}",
                    automatic
                        ? ScaleProgress(CalculatePercent(index + 1, items.Count), AutomaticCompareProgressStart, 100)
                        : CalculatePercent(index + 1, items.Count));

                await RefreshComparisonForItemAsync(item);
            }

            SetStatus(
                automatic
                    ? $"Scan und Zielvergleiche abgeschlossen ({items.Count} vorhandene Bibliotheksdatei(en) geprüft)"
                    : $"Vergleiche aktualisiert ({items.Count} vorhandene Bibliotheksdatei(en) geprüft)",
                100);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshComparisonForItemAsync(BatchEpisodeItemViewModel item)
    {
        if (string.IsNullOrWhiteSpace(item.MainVideoPath)
            || string.IsNullOrWhiteSpace(item.OutputPath)
            || string.IsNullOrWhiteSpace(item.TitleForMux))
        {
            item.SetPlanSummary(string.Empty);
            return;
        }

        item.SetPlanSummary(File.Exists(item.OutputPath)
            ? "Zielvergleich wird berechnet..."
            : "Verwendungsplan wird berechnet...");
        item.SetUsageSummary(EpisodeUsageSummary.CreatePending(
            File.Exists(item.OutputPath) ? "Zielvergleich wird berechnet" : "Verwendungsplan wird berechnet",
            File.Exists(item.OutputPath) ? Path.GetFileName(item.OutputPath) : "Neue MKV wird erstellt"));

        try
        {
            var plan = await BuildPlanForItemAsync(item);
            item.SetPlanSummary(plan.BuildCompactSummaryText());
            item.SetUsageSummary(plan.BuildUsageSummary());

            if (File.Exists(item.OutputPath))
            {
                item.Status = plan.SkipMux ? "Ziel aktuell" : "Bereit";
            }
            else
            {
                item.Status = "Bereit";
            }
        }
        catch (Exception ex)
        {
            item.SetPlanSummary("Plan konnte noch nicht berechnet werden: " + ex.Message);
            item.SetUsageSummary(EpisodeUsageSummary.CreatePending("Plan konnte nicht berechnet werden", ex.Message));
            item.Status = "Warnung";
        }
    }

    private async Task ReviewPendingSourcesAsync()
    {
        var selectedItems = EpisodeItems.Where(item => item.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            _dialogService.ShowWarning("Hinweis", "Bitte zuerst mindestens eine Episode für den Batch auswählen.");
            return;
        }

        var readyItems = selectedItems
            .Where(item => item.Status != "Fehler")
            .ToList();

        if (readyItems.Count == 0)
        {
            _dialogService.ShowWarning("Hinweis", "Es gibt keine gültigen Episoden für den Batch.");
            return;
        }

        var approved = await EnsurePendingChecksApprovedAsync(readyItems);
        if (approved)
        {
            _dialogService.ShowInfo("Hinweis", "Alle offenen Quellen- und TVDB-Prüfungen wurden abgeschlossen.");
        }
    }

    private async Task RunBatchAsync()
    {
        var selectedItems = EpisodeItems.Where(item => item.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            _dialogService.ShowWarning("Hinweis", "Bitte mindestens eine Episode für den Batch auswählen.");
            return;
        }

        var readyItems = selectedItems
            .Where(item => item.Status != "Fehler")
            .ToList();

        if (readyItems.Count == 0)
        {
            _dialogService.ShowWarning("Hinweis", "Es gibt keine gültigen Episoden für den Batch.");
            return;
        }

        var approved = await EnsurePendingChecksApprovedAsync(readyItems);
        if (!approved)
        {
            _dialogService.ShowWarning(
                "Hinweis",
                "Der Batch wurde abgebrochen, weil nicht alle prüfpflichtigen Quellen freigegeben wurden.");
            SetStatus("Batch abgebrochen", 0);
            return;
        }

        SetStatus("Erstelle Mux-Pläne...", 0);
        var planningTracker = new BatchRunProgressTracker(readyItems.Count, SetStatus);
        var executablePlans = await BuildExecutionWorkItemsAsync(readyItems, planningTracker);

        if (executablePlans.Count == 0)
        {
            SetStatus("Keine weiteren Mux-Vorgänge nötig", 100);
            _dialogService.ShowInfo("Hinweis", "Alle ausgewählten Episoden sind bereits vollständig oder wurden wegen Fehlern übersprungen.");
            return;
        }

        var progressTracker = new BatchRunProgressTracker(executablePlans.Count, SetStatus);
        var copyPlans = executablePlans
            .Select(entry => entry.Plan.WorkingCopy)
            .Where(plan => plan is not null)
            .Cast<FileCopyPlan>()
            .GroupBy(plan => plan.SourceFilePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var copyPlansToExecute = copyPlans
            .Where(_services.FileCopy.NeedsCopy)
            .ToList();

        var totalCopyBytes = copyPlansToExecute.Sum(plan => plan.FileSizeBytes);

        if (!_dialogService.ConfirmBatchExecution(executablePlans.Count, copyPlansToExecute.Count, totalCopyBytes))
        {
            SetStatus("Abgebrochen", 0);
            return;
        }

        if (copyPlansToExecute.Count > 0)
        {
            await CopyArchiveFilesAsync(copyPlansToExecute, totalCopyBytes, progressTracker);
        }
        else
        {
            progressTracker.ReportCopyCompleted(reusedExistingCopies: copyPlans.Count > 0);
            if (copyPlans.Count > 0)
            {
                AppendLog("ARBEITSKOPIEN: Bereits vorhandene aktuelle Arbeitskopien werden wiederverwendet.");
            }
        }

        try
        {
            SetBusy(true);
            ResetLog();
            var successCount = 0;
            var warningCount = 0;
            var errorCount = 0;
            var movedDoneFiles = new List<string>();
            var newArchiveFiles = new List<string>();
            var doneDirectory = Path.Combine(SourceDirectory, DoneFolderName);

            for (var index = 0; index < executablePlans.Count; index++)
            {
                var workItem = executablePlans[index];
                var item = workItem.Item;
                var plan = workItem.Plan;
                var outputExistedBeforeRun = File.Exists(item.OutputPath);
                item.Status = "Läuft";
                AppendLog($"STARTE: {item.MainVideoFileName}");

                try
                {
                    var result = await _services.MuxWorkflow.ExecuteMuxAsync(
                        plan,
                        line => AppendLog($"  {line}"),
                        update => progressTracker.ReportMuxProgress(index + 1, update.ProgressPercent, update.HasWarning));

                    if (result.ExitCode == 0 && !result.HasWarning)
                    {
                        item.Status = "Erfolgreich";
                        successCount++;
                        if (!outputExistedBeforeRun && File.Exists(item.OutputPath))
                        {
                            newArchiveFiles.Add(item.OutputPath);
                        }
                        movedDoneFiles.AddRange(await MoveEpisodeFilesToDoneAsync(workItem, doneDirectory, index + 1, progressTracker));
                    }
                    else if ((result.ExitCode == 0 && result.HasWarning)
                        || (result.ExitCode == 1 && File.Exists(item.OutputPath)))
                    {
                        item.Status = "Warnung";
                        warningCount++;
                        if (!outputExistedBeforeRun && File.Exists(item.OutputPath))
                        {
                            newArchiveFiles.Add(item.OutputPath);
                        }
                        movedDoneFiles.AddRange(await MoveEpisodeFilesToDoneAsync(workItem, doneDirectory, index + 1, progressTracker));
                    }
                    else
                    {
                        item.Status = $"Fehler ({result.ExitCode})";
                        errorCount++;
                    }
                }
                catch (Exception ex)
                {
                    item.Status = "Fehler";
                    AppendLog($"  FEHLER: {ex.Message}");
                    errorCount++;
                }
                finally
                {
                    progressTracker.ReportFinalizingItem(index + 1);
                }

                progressTracker.ReportItemCompleted(index + 1);
            }

            await OfferBatchDoneCleanupAsync(doneDirectory, movedDoneFiles, progressTracker);
            WriteNewArchiveFileReport(newArchiveFiles);

            SetStatus(
                $"Batch abgeschlossen: {successCount} erfolgreich, {warningCount} Warnung(en), {errorCount} Fehler",
                100);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ApplyDetectionToItemAsync(BatchEpisodeItemViewModel item, string selectedVideoPath)
    {
        await ApplyDetectionToItemAsync(item, selectedVideoPath, item.ExcludedSourcePaths);
    }

    private async Task<bool> ApplyDetectionToItemAsync(
        BatchEpisodeItemViewModel item,
        string selectedVideoPath,
        IReadOnlyCollection<string>? excludedSourcePaths)
    {
        try
        {
            SetBusy(true);
            SetStatus("Eintrag wird neu erkannt...", 0);

            var detected = await _services.SeriesEpisodeMux.DetectFromSelectedVideoAsync(
                selectedVideoPath,
                HandleSelectedItemDetectionProgress,
                excludedSourcePaths);
            var localGuess = new EpisodeMetadataGuess(
                detected.SeriesName,
                detected.SuggestedTitle,
                detected.SeasonNumber,
                detected.EpisodeNumber);
            SetStatus("TVDB-Metadaten werden abgeglichen...", 88);
            var metadataResolution = await ResolveMetadataAsync(detected);
            detected = ApplyMetadataSelection(detected, metadataResolution);
            var outputPath = BuildOutputPath(detected);
            var outputAlreadyExists = File.Exists(outputPath);

            item.ApplyDetection(
                requestedMainVideoPath: selectedVideoPath,
                localGuess: localGuess,
                detected: detected,
                metadataResolution: metadataResolution,
                outputPath: outputPath,
                status: outputAlreadyExists ? "Vergleich offen" : "Bereit");
            item.ReplaceExcludedSourcePaths(excludedSourcePaths ?? []);

            AppendLog($"AKTUALISIERT: {Path.GetFileName(selectedVideoPath)} -> {Path.GetFileName(item.MainVideoPath)}");
            SetStatus("Eintrag aktualisiert", 100);
            if (ReferenceEquals(SelectedEpisodeItem, item))
            {
                ScheduleSelectedItemPlanSummaryRefresh();
            }
            return true;
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(ex.Message);
            AppendLog($"FEHLER: {Path.GetFileName(selectedVideoPath)} -> {ex.Message}");
            SetStatus("Fehler", 0);
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<SeriesEpisodeMuxPlan> BuildPlanForItemAsync(BatchEpisodeItemViewModel item)
    {
        var request = new SeriesEpisodeMuxRequest(
            item.MainVideoPath,
            item.AudioDescriptionPath,
            item.SubtitlePaths,
            item.AttachmentPaths,
            item.OutputPath,
            item.TitleForMux);

        return await _services.SeriesEpisodeMux.CreatePlanAsync(request);
    }

    private async Task<List<BatchExecutionWorkItem>> BuildExecutionWorkItemsAsync(
        IReadOnlyList<BatchEpisodeItemViewModel> readyItems,
        BatchRunProgressTracker progressTracker)
    {
        var executablePlans = new List<BatchExecutionWorkItem>();

        for (var index = 0; index < readyItems.Count; index++)
        {
            var item = readyItems[index];
            progressTracker.ReportPlanning(index + 1, readyItems.Count);

            try
            {
                var plan = await BuildPlanForItemAsync(item);
                if (plan.SkipMux)
                {
                    item.Status = "Ziel aktuell";
                    AppendLog($"SKIP: {item.MainVideoFileName} -> {plan.SkipReason}");
                    continue;
                }

                executablePlans.Add(new BatchExecutionWorkItem(item, plan, BuildBatchCleanupFileList(item, plan)));
            }
            catch (Exception ex)
            {
                item.Status = "Fehler";
                AppendLog($"PLAN-FEHLER: {item.MainVideoFileName} -> {ex.Message}");
            }
        }

        return executablePlans;
    }

    private string BuildOutputPath(AutoDetectedEpisodeFiles detected)
    {
        var fallbackDirectory = Path.GetDirectoryName(detected.MainVideoPath) ?? SourceDirectory;
        return BuildAutomaticOutputPath(
            fallbackDirectory,
            detected.SeriesName,
            detected.SeasonNumber,
            detected.EpisodeNumber,
            detected.SuggestedTitle);
    }

    private string BuildOutputPath(BatchEpisodeItemViewModel item)
    {
        var fallbackDirectory = Path.GetDirectoryName(item.MainVideoPath) ?? SourceDirectory;
        return BuildAutomaticOutputPath(
            fallbackDirectory,
            item.SeriesName,
            item.SeasonNumber,
            item.EpisodeNumber,
            item.TitleForMux);
    }

    private string BuildAutomaticOutputPath(
        string fallbackDirectory,
        string seriesName,
        string seasonNumber,
        string episodeNumber,
        string title)
    {
        return _services.OutputPaths.BuildOutputPath(
            fallbackDirectory,
            seriesName,
            seasonNumber,
            episodeNumber,
            title,
            OutputDirectory);
    }

    private static string GetPreferredSourceDirectory()
    {
        var downloadsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        var preferredDirectory = PreferredDownloadsSubPath.Aggregate(downloadsDirectory, Path.Combine);

        return Directory.Exists(preferredDirectory)
            ? preferredDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private void RefreshAutomaticOutputPaths()
    {
        using (EpisodeItemsView.DeferRefresh())
        {
            foreach (var item in EpisodeItems)
            {
                RefreshAutomaticOutputPath(item);
            }
        }
    }

    private void RefreshAutomaticOutputPath(BatchEpisodeItemViewModel item)
    {
        if (!item.UsesAutomaticOutputPath)
        {
            return;
        }

        item.SetAutomaticOutputPath(BuildOutputPath(item));
    }

    private void ScheduleSelectedItemPlanSummaryRefresh()
    {
        _selectedPlanSummaryRefreshCts?.Cancel();
        _selectedPlanSummaryRefreshCts?.Dispose();

        var cancellationSource = new CancellationTokenSource();
        _selectedPlanSummaryRefreshCts = cancellationSource;

        _ = RefreshSelectedItemPlanSummaryDebouncedAsync(cancellationSource.Token);
    }

    private async Task RefreshSelectedItemPlanSummaryDebouncedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(200, cancellationToken);
            await RefreshSelectedItemPlanSummaryAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshSelectedItemPlanSummaryAsync()
    {
        var item = SelectedEpisodeItem;
        var version = Interlocked.Increment(ref _selectedPlanSummaryVersion);
        if (item is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.MainVideoPath)
            || string.IsNullOrWhiteSpace(item.OutputPath)
            || string.IsNullOrWhiteSpace(item.TitleForMux))
        {
            item.SetPlanSummary(string.Empty);
            return;
        }

        try
        {
            await RefreshComparisonForItemAsync(item);
            if (version != _selectedPlanSummaryVersion || !ReferenceEquals(SelectedEpisodeItem, item))
            {
                return;
            }
        }
        catch (Exception ex)
        {
            if (version != _selectedPlanSummaryVersion || !ReferenceEquals(SelectedEpisodeItem, item))
            {
                return;
            }

            item.SetPlanSummary("Plan konnte noch nicht berechnet werden: " + ex.Message);
            item.SetUsageSummary(EpisodeUsageSummary.CreatePending("Plan konnte nicht berechnet werden", ex.Message));
        }
    }

    private async Task<IReadOnlyList<string>> MoveEpisodeFilesToDoneAsync(
        BatchExecutionWorkItem workItem,
        string doneDirectory,
        int currentItemIndex,
        BatchRunProgressTracker progressTracker)
    {
        var item = workItem.Item;
        var cleanupFiles = workItem.CleanupFiles;
        if (cleanupFiles.Count == 0)
        {
            return [];
        }

        var moveResult = await _services.Cleanup.MoveFilesToDirectoryAsync(
            cleanupFiles,
            doneDirectory,
            (current, total, filePath) =>
            {
                _ = Application.Current.Dispatcher.BeginInvoke(() =>
                    progressTracker.ReportMoveToDone(currentItemIndex, current, total));
            });

        AppendLog($"DONE: {item.MainVideoFileName} -> {moveResult.MovedFiles.Count} Datei(en) verschoben.");

        if (moveResult.FailedFiles.Count > 0)
        {
            AppendLog("  NICHT VERSCHOBEN: " + string.Join(", ", moveResult.FailedFiles.Select(Path.GetFileName)));
        }

        return moveResult.MovedFiles;
    }

    private List<string> BuildBatchCleanupFileList(BatchEpisodeItemViewModel item, SeriesEpisodeMuxPlan plan)
    {
        return _services.CleanupFiles.BuildCleanupFileList(
            item.RelatedEpisodeFilePaths.Concat(plan.GetReferencedInputFiles()),
            item.OutputPath,
            plan.WorkingCopy?.DestinationFilePath,
            SourceDirectory);
    }

    private async Task OfferBatchDoneCleanupAsync(
        string doneDirectory,
        IReadOnlyList<string> movedDoneFiles,
        BatchRunProgressTracker progressTracker)
    {
        var doneFiles = movedDoneFiles
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (doneFiles.Count == 0)
        {
            _services.Cleanup.DeleteDirectoryIfEmpty(doneDirectory);
            return;
        }

        if (_dialogService.ConfirmBatchRecycleDoneFiles(doneFiles.Count, doneDirectory))
        {
            var recycleResult = await _services.Cleanup.RecycleFilesAsync(
                doneFiles,
                (current, total, _) =>
                {
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        progressTracker.ReportRecycleProgress(current, total);
                    });
                });

            if (recycleResult.FailedFiles.Count > 0)
            {
                _dialogService.ShowWarning(
                    "Warnung",
                    "Einige Dateien aus dem Done-Ordner konnten nicht in den Papierkorb verschoben werden:\n"
                    + string.Join(Environment.NewLine, recycleResult.FailedFiles.Select(Path.GetFileName)));
            }

            _services.Cleanup.DeleteDirectoryIfEmpty(doneDirectory);
            return;
        }

        if (_dialogService.AskOpenDoneDirectory(doneDirectory))
        {
            _dialogService.OpenPathWithDefaultApp(doneDirectory);
        }
    }

    private void WriteNewArchiveFileReport(IReadOnlyList<string> newArchiveFiles)
    {
        var files = newArchiveFiles
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            return;
        }

        AppendLog(string.Empty);
        AppendLog("NEU IN SERIENBIBLIOTHEK EINGEFÜGT:");
        foreach (var file in files)
        {
            AppendLog("  " + file);
        }

        var timeStamp = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
        var reportPath = Path.Combine(SourceDirectory, $"Neu in Serienbibliothek - {timeStamp}.txt");
        File.WriteAllLines(reportPath,
        [
            "Neu in Serienbibliothek eingefügte Dateien",
            $"Erstellt am: {DateTime.Now:dd.MM.yyyy HH:mm:ss}",
            string.Empty,
            .. files
        ]);

        _dialogService.ShowInfo(
            "Neue Dateien in der Serienbibliothek",
            $"{files.Count} neue Datei(en) wurden neu in der Serienbibliothek angelegt.\n\nListe im Protokoll und in:\n{reportPath}");
    }

    private async Task CopyArchiveFilesAsync(
        IReadOnlyList<FileCopyPlan> copyPlans,
        long totalCopyBytes,
        BatchRunProgressTracker progressTracker)
    {
        long copiedBeforeCurrentFile = 0;

        for (var index = 0; index < copyPlans.Count; index++)
        {
            var copyPlan = copyPlans[index];
            AppendLog($"KOPIERE: {Path.GetFileName(copyPlan.SourceFilePath)}");

            await _services.FileCopy.CopyAsync(
                copyPlan,
                (copiedBytes, _) =>
                {
                    var combinedCopiedBytes = copiedBeforeCurrentFile + copiedBytes;

                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        progressTracker.ReportCopyProgress(index + 1, copyPlans.Count, combinedCopiedBytes, totalCopyBytes);
                    });
                });

            copiedBeforeCurrentFile += copyPlan.FileSizeBytes;
        }

        progressTracker.ReportCopyCompleted(reusedExistingCopies: false);
    }

    private async Task<bool> ReviewEpisodeAsync(BatchEpisodeItemViewModel item, bool isBatchPreparation)
    {
        while (item.RequiresManualCheck && !string.IsNullOrWhiteSpace(item.CurrentReviewTargetPath))
        {
            var reviewTargetPath = item.CurrentReviewTargetPath!;
            SetStatus(
                isBatchPreparation
                    ? $"Prüfe Quelle für '{item.Title}'..."
                    : "Prüfe Quelle...",
                ProgressValue);

            _dialogService.OpenFilesWithDefaultApp([reviewTargetPath]);

            var result = _dialogService.AskSourceReviewResult(
                Path.GetFileName(reviewTargetPath),
                canTryAlternative: true);

            if (result == MessageBoxResult.Cancel)
            {
                SetStatus("Quellenprüfung abgebrochen", ProgressValue);
                return false;
            }

            if (result == MessageBoxResult.Yes)
            {
                item.ApproveCurrentReviewTarget();
                SetStatus(
                    isBatchPreparation
                        ? $"Quelle für '{item.Title}' freigegeben"
                        : "Quelle freigegeben",
                    100);
                return true;
            }

            var tentativeExclusions = new HashSet<string>(item.ExcludedSourcePaths, StringComparer.OrdinalIgnoreCase)
            {
                reviewTargetPath
            };

            var updated = await ApplyDetectionToItemAsync(item, item.DetectionSeedPath, tentativeExclusions);
            if (!updated)
            {
                return false;
            }

            if (!item.RequiresManualCheck)
            {
                SetStatus(
                    isBatchPreparation
                        ? $"Alternative Quelle für '{item.Title}' gewählt"
                        : "Auf alternative Quelle umgestellt",
                    100);
                return true;
            }
        }

        return !item.RequiresManualCheck || item.IsManualCheckApproved;
    }

    private async Task<bool> ReviewEpisodeMetadataAsync(BatchEpisodeItemViewModel item, bool isBatchPreparation)
    {
        if (!item.RequiresMetadataReview || item.IsMetadataReviewApproved)
        {
            return true;
        }

        SetStatus(
            isBatchPreparation
                ? $"Prüfe TVDB-Zuordnung für '{item.Title}'..."
                : "Prüfe TVDB-Zuordnung...",
            ProgressValue);

        var guess = new EpisodeMetadataGuess(
            item.LocalSeriesName,
            item.LocalTitle,
            item.LocalSeasonNumber,
            item.LocalEpisodeNumber);

        var dialog = new Windows.TvdbLookupWindow(_services.EpisodeMetadata, guess)
        {
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
                ?? Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true)
        {
            SetStatus("TVDB-Prüfung abgebrochen", ProgressValue);
            return false;
        }

        if (dialog.KeepLocalDetection)
        {
            item.ApplyLocalMetadataGuess();
            RefreshAutomaticOutputPath(item);
            item.ApproveMetadataReview("Lokale Erkennung wurde bewusst beibehalten.");
            SetStatus(
                isBatchPreparation
                    ? $"Lokale Erkennung für '{item.Title}' freigegeben"
                    : "Lokale Erkennung freigegeben",
                100);
            if (ReferenceEquals(SelectedEpisodeItem, item))
            {
                ScheduleSelectedItemPlanSummaryRefresh();
            }
            return true;
        }

        if (dialog.SelectedEpisodeSelection is null)
        {
            SetStatus("TVDB-Prüfung abgebrochen", ProgressValue);
            return false;
        }

        item.ApplyTvdbSelection(dialog.SelectedEpisodeSelection);
        RefreshAutomaticOutputPath(item);
        item.ApproveMetadataReview(
            $"TVDB manuell bestätigt: S{dialog.SelectedEpisodeSelection.SeasonNumber}E{dialog.SelectedEpisodeSelection.EpisodeNumber} - {dialog.SelectedEpisodeSelection.EpisodeTitle}");

        SetStatus(
            isBatchPreparation
                ? $"TVDB-Zuordnung für '{item.Title}' freigegeben"
                : "TVDB-Zuordnung freigegeben",
            100);
        if (ReferenceEquals(SelectedEpisodeItem, item))
        {
            ScheduleSelectedItemPlanSummaryRefresh();
        }

        await Task.CompletedTask;
        return true;
    }

    private async Task<EpisodeMetadataResolutionResult> ResolveMetadataAsync(AutoDetectedEpisodeFiles detected)
    {
        return await _services.EpisodeMetadata.ResolveAutomaticallyAsync(new EpisodeMetadataGuess(
            detected.SeriesName,
            detected.SuggestedTitle,
            detected.SeasonNumber,
            detected.EpisodeNumber));
    }

    private static AutoDetectedEpisodeFiles ApplyMetadataSelection(
        AutoDetectedEpisodeFiles detected,
        EpisodeMetadataResolutionResult resolution)
    {
        return resolution.Selection is null
            ? detected
            : EpisodeMetadataMergeHelper.ApplySelection(detected, resolution.Selection);
    }

    private bool CanReviewPendingSources()
    {
        return !_isBusy && EpisodeItems.Any(item => item.IsSelected && item.HasPendingChecks);
    }

    private async Task<bool> EnsurePendingChecksApprovedAsync(IReadOnlyList<BatchEpisodeItemViewModel> readyItems)
    {
        var pendingSourceItems = readyItems
            .Where(item => item.RequiresManualCheck && !item.IsManualCheckApproved)
            .ToList();

        var pendingMetadataItems = readyItems
            .Where(item => item.RequiresMetadataReview && !item.IsMetadataReviewApproved)
            .ToList();

        if (pendingSourceItems.Count == 0 && pendingMetadataItems.Count == 0)
        {
            SetStatus("Keine offenen Pflichtprüfungen", ProgressValue);
            return true;
        }

        SetStatus("Pflichtprüfungen werden vorbereitet...", 0);

        foreach (var item in pendingSourceItems)
        {
            SelectedEpisodeItem = item;
            var approved = await ReviewEpisodeAsync(item, isBatchPreparation: true);
            if (!approved)
            {
                return false;
            }
        }

        foreach (var item in pendingMetadataItems)
        {
            SelectedEpisodeItem = item;
            var approved = await ReviewEpisodeMetadataAsync(item, isBatchPreparation: true);
            if (!approved)
            {
                return false;
            }
        }

        return true;
    }

    private static string ResolveSelectedItemDirectory(BatchEpisodeItemViewModel item)
    {
        var paths = item.SourceFilePaths
            .Concat([item.RequestedMainVideoPath, item.OutputPath])
            .Where(path => !string.IsNullOrWhiteSpace(path));

        foreach (var path in paths)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                return directory;
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private void SelectAllEpisodes()
    {
        foreach (var item in EpisodeItems)
        {
            item.IsSelected = true;
        }

        RefreshCommands();
    }

    private void DeselectAllEpisodes()
    {
        foreach (var item in EpisodeItems)
        {
            item.IsSelected = false;
        }

        RefreshCommands();
    }

    private bool FilterEpisodeItem(object item)
    {
        if (item is not BatchEpisodeItemViewModel episode)
        {
            return false;
        }

        return SelectedFilterMode switch
        {
            "Nur offen" => episode.HasPendingChecks,
            "Nur neu" => episode.ArchiveStateText == "neu",
            "Nur vorhanden" => episode.ArchiveStateText == "vorhanden",
            "Nur Fehler" => episode.Status.StartsWith("Fehler", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private void ApplyEpisodeItemsViewConfiguration()
    {
        using (EpisodeItemsView.DeferRefresh())
        {
            EpisodeItemsView.SortDescriptions.Clear();

            switch (SelectedSortMode)
            {
                case "Prüfung zuerst":
                    EpisodeItemsView.SortDescriptions.Add(new SortDescription(nameof(BatchEpisodeItemViewModel.HasPendingChecks), ListSortDirection.Descending));
                    EpisodeItemsView.SortDescriptions.Add(new SortDescription(nameof(BatchEpisodeItemViewModel.MainVideoFileName), ListSortDirection.Ascending));
                    break;
                case "Status zuerst":
                    EpisodeItemsView.SortDescriptions.Add(new SortDescription(nameof(BatchEpisodeItemViewModel.StatusSortKey), ListSortDirection.Ascending));
                    EpisodeItemsView.SortDescriptions.Add(new SortDescription(nameof(BatchEpisodeItemViewModel.MainVideoFileName), ListSortDirection.Ascending));
                    break;
                case "Neu zuerst":
                    EpisodeItemsView.SortDescriptions.Add(new SortDescription(nameof(BatchEpisodeItemViewModel.ArchiveSortKey), ListSortDirection.Ascending));
                    EpisodeItemsView.SortDescriptions.Add(new SortDescription(nameof(BatchEpisodeItemViewModel.MainVideoFileName), ListSortDirection.Ascending));
                    break;
                default:
                    EpisodeItemsView.SortDescriptions.Add(new SortDescription(nameof(BatchEpisodeItemViewModel.MainVideoFileName), ListSortDirection.Ascending));
                    break;
            }
        }
    }

    private static bool LooksLikeAudioDescription(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        return fileName.Contains("audiodeskrip", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(fileName, @"(?:^|[^a-z])AD(?:[^a-z]|$)", RegexOptions.IgnoreCase);
    }

    private static int CalculatePercent(int current, int total)
    {
        return total <= 0 ? 0 : (int)Math.Round(current * 100d / total);
    }

    private static int ScaleProgress(int value, int start, int end)
    {
        value = Math.Clamp(value, 0, 100);
        return start + (int)Math.Round((end - start) * (value / 100d));
    }

    private async Task ProcessBatchScanItemAsync(
        string file,
        int index,
        int total,
        SemaphoreSlim throttler,
        BatchScanResult[] target,
        Func<int> getCompletedCount,
        Action onCompleted)
    {
        await throttler.WaitAsync();
        try
        {
            var detected = await _services.SeriesEpisodeMux.DetectFromSelectedVideoAsync(
                file,
                update => HandleBatchDetectionProgress(getCompletedCount() + 1, total, file, update));

            var localGuess = new EpisodeMetadataGuess(
                detected.SeriesName,
                detected.SuggestedTitle,
                detected.SeasonNumber,
                detected.EpisodeNumber);

            HandleSelectedItemDetectionProgress(new DetectionProgressUpdate(
                $"TVDB-Abgleich für {Path.GetFileName(file)}...",
                CalculatePercent(getCompletedCount(), total)));

            var metadataResolution = await ResolveMetadataAsync(detected);
            detected = ApplyMetadataSelection(detected, metadataResolution);
            var outputPath = BuildOutputPath(detected);

            target[index] = new BatchScanResult(
                index,
                file,
                detected,
                localGuess,
                metadataResolution,
                outputPath,
                null);
        }
        catch (Exception ex)
        {
            target[index] = new BatchScanResult(
                index,
                file,
                null,
                null,
                null,
                null,
                ex.Message);
        }
        finally
        {
            onCompleted();
            throttler.Release();
        }
    }

    private void AppendLog(string line)
    {
        _logBuffer.AppendLine(line);
    }

    private void ResetLog()
    {
        _logBuffer.Reset();
    }

    private void HandleBatchDetectionProgress(
        int currentItem,
        int totalItems,
        string currentFilePath,
        DetectionProgressUpdate update)
    {
        void ApplyUpdate()
        {
            var baseProgress = totalItems <= 0
                ? 0
                : ((currentItem - 1) + (update.ProgressPercent / 100d)) / totalItems * 100d;

            SetStatus(
                $"Scanne Ordner... {currentItem}/{totalItems} - {Path.GetFileName(currentFilePath)} - {update.StatusText}",
                ScaleProgress((int)Math.Round(baseProgress), 0, AutomaticCompareProgressStart));
        }

        if (Application.Current.Dispatcher.CheckAccess())
        {
            ApplyUpdate();
            return;
        }

        _ = Application.Current.Dispatcher.BeginInvoke(ApplyUpdate);
    }

    private void HandleSelectedItemDetectionProgress(DetectionProgressUpdate update)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            SetStatus(update.StatusText, update.ProgressPercent);
            return;
        }

        _ = Application.Current.Dispatcher.BeginInvoke(() => SetStatus(update.StatusText, update.ProgressPercent));
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        SelectSourceDirectoryCommand.RaiseCanExecuteChanged();
        SelectOutputDirectoryCommand.RaiseCanExecuteChanged();
        ScanDirectoryCommand.RaiseCanExecuteChanged();
        SelectAllEpisodesCommand.RaiseCanExecuteChanged();
        DeselectAllEpisodesCommand.RaiseCanExecuteChanged();
        ReviewPendingSourcesCommand.RaiseCanExecuteChanged();
        OpenSelectedSourcesCommand.RaiseCanExecuteChanged();
        ReviewSelectedMetadataCommand.RaiseCanExecuteChanged();
        RefreshAllComparisonsCommand.RaiseCanExecuteChanged();
        RedetectSelectedEpisodeCommand.RaiseCanExecuteChanged();
        EditSelectedAudioDescriptionCommand.RaiseCanExecuteChanged();
        EditSelectedSubtitlesCommand.RaiseCanExecuteChanged();
        EditSelectedAttachmentsCommand.RaiseCanExecuteChanged();
        EditSelectedOutputCommand.RaiseCanExecuteChanged();
        RunBatchCommand.RaiseCanExecuteChanged();
    }

    private void ClearEpisodeItems()
    {
        foreach (var item in EpisodeItems)
        {
            item.PropertyChanged -= EpisodeItem_PropertyChanged;
        }

        EpisodeItems.Clear();
        SelectedEpisodeItem = null;
        EpisodeItemsView.Refresh();
    }

    private void AddEpisodeItem(BatchEpisodeItemViewModel item)
    {
        item.PropertyChanged += EpisodeItem_PropertyChanged;
        EpisodeItems.Add(item);
        EpisodeItemsView.Refresh();
    }

    private void EpisodeItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshCommands();
        RefreshOverview();
        EpisodeItemsView.Refresh();
    }

    private void EpisodeItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshCommands();

        if (e.PropertyName is nameof(BatchEpisodeItemViewModel.TitleForMux)
            && sender is BatchEpisodeItemViewModel titleItem)
        {
            RefreshAutomaticOutputPath(titleItem);
        }

        if (ShouldRefreshOverview(e.PropertyName))
        {
            RefreshOverview();
        }

        if (ShouldRefreshView(e.PropertyName))
        {
            EpisodeItemsView.Refresh();
        }

        if (sender is BatchEpisodeItemViewModel item
            && ReferenceEquals(SelectedEpisodeItem, item)
            && ShouldRefreshSelectedItemPlanSummary(e.PropertyName))
        {
            ScheduleSelectedItemPlanSummaryRefresh();
        }
    }

    private void SetStatus(string text, int progress)
    {
        StatusText = text;
        ProgressValue = Math.Clamp(progress, 0, 100);
    }

    private void RefreshOverview()
    {
        OnPropertyChanged(nameof(EpisodeCount));
        OnPropertyChanged(nameof(SelectedEpisodeCount));
        OnPropertyChanged(nameof(ExistingArchiveCount));
        OnPropertyChanged(nameof(PendingCheckCount));
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

    private bool ShouldRefreshView(string? propertyName)
    {
        if (propertyName is null)
        {
            return true;
        }

        return propertyName switch
        {
            nameof(BatchEpisodeItemViewModel.HasPendingChecks) => SelectedFilterMode == "Nur offen" || SelectedSortMode == "Prüfung zuerst",
            nameof(BatchEpisodeItemViewModel.Status)
                or nameof(BatchEpisodeItemViewModel.StatusSortKey) => SelectedFilterMode == "Nur Fehler" || SelectedSortMode == "Status zuerst",
            nameof(BatchEpisodeItemViewModel.OutputPath)
                or nameof(BatchEpisodeItemViewModel.ArchiveStateText)
                or nameof(BatchEpisodeItemViewModel.ArchiveSortKey) => SelectedFilterMode is "Nur neu" or "Nur vorhanden" || SelectedSortMode == "Neu zuerst",
            nameof(BatchEpisodeItemViewModel.MainVideoPath)
                or nameof(BatchEpisodeItemViewModel.MainVideoFileName) => SelectedSortMode == "Dateiname" || SelectedSortMode == "Prüfung zuerst" || SelectedSortMode == "Status zuerst" || SelectedSortMode == "Neu zuerst",
            _ => false
        };
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}



