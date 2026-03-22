using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels.Commands;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

public sealed class BatchMuxViewModel : INotifyPropertyChanged
{
    private const string DefaultSourceDirectory = @"C:\Users\tobby\Downloads\MediathekView-latest-win\Downloads";
    private const string DoneFolderName = "done";

    private readonly AppServices _services;
    private readonly UserDialogService _dialogService;

    private string _sourceDirectory = string.Empty;
    private string _outputDirectory = string.Empty;
    private string _statusText = "Bereit";
    private string _logText = string.Empty;
    private int _progressValue;
    private bool _isBusy;
    private BatchEpisodeItemViewModel? _selectedEpisodeItem;
    private int _selectedPlanSummaryVersion;

    public BatchMuxViewModel(AppServices services, UserDialogService dialogService)
    {
        _services = services;
        _dialogService = dialogService;

        EpisodeItems.CollectionChanged += EpisodeItems_CollectionChanged;

        SelectSourceDirectoryCommand = new AsyncRelayCommand(SelectSourceDirectoryAsync, () => !_isBusy);
        SelectOutputDirectoryCommand = new RelayCommand(SelectOutputDirectory, () => !_isBusy && !string.IsNullOrWhiteSpace(SourceDirectory));
        ScanDirectoryCommand = new AsyncRelayCommand(ScanDirectoryAsync, () => !_isBusy && !string.IsNullOrWhiteSpace(SourceDirectory));
        SelectAllEpisodesCommand = new RelayCommand(SelectAllEpisodes, () => !_isBusy && EpisodeItems.Any(item => !item.IsSelected));
        DeselectAllEpisodesCommand = new RelayCommand(DeselectAllEpisodes, () => !_isBusy && EpisodeItems.Any(item => item.IsSelected));
        ReviewPendingSourcesCommand = new AsyncRelayCommand(ReviewPendingSourcesAsync, CanReviewPendingSources);
        OpenSelectedSourcesCommand = new RelayCommand(OpenSelectedSources, () => !_isBusy && SelectedEpisodeItem?.SourceFilePaths.Count > 0);
        ReviewSelectedMetadataCommand = new AsyncRelayCommand(ReviewSelectedMetadataAsync, () => !_isBusy && SelectedEpisodeItem is not null);
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
    public AsyncRelayCommand RedetectSelectedEpisodeCommand { get; }
    public RelayCommand EditSelectedAudioDescriptionCommand { get; }
    public RelayCommand EditSelectedSubtitlesCommand { get; }
    public RelayCommand EditSelectedAttachmentsCommand { get; }
    public RelayCommand EditSelectedOutputCommand { get; }
    public AsyncRelayCommand RunBatchCommand { get; }

    public ObservableCollection<BatchEpisodeItemViewModel> EpisodeItems { get; } = [];

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
            _ = RefreshSelectedItemPlanSummaryAsync();
        }
    }

    private async Task SelectSourceDirectoryAsync()
    {
        var initialDirectory = Directory.Exists(SourceDirectory) ? SourceDirectory : DefaultSourceDirectory;
        var path = _dialogService.SelectFolder("Quellordner fuer den Batch auswaehlen", initialDirectory);
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

        LogText = string.Empty;
        ClearEpisodeItems();
        StatusText = "Ordner gewaehlt - starte Scan...";
        RefreshCommands();
        await ScanDirectoryAsync();
    }

    private void SelectOutputDirectory()
    {
        var initialDirectory = Directory.Exists(OutputDirectory) ? OutputDirectory : SourceDirectory;
        var path = _dialogService.SelectFolder("Ausgabeordner fuer den Batch auswaehlen", initialDirectory);
        if (!string.IsNullOrWhiteSpace(path))
        {
            OutputDirectory = path;
            RefreshCommands();
        }
    }

    private async Task ScanDirectoryAsync()
    {
        try
        {
            SetBusy(true);
            ClearEpisodeItems();
            LogText = string.Empty;
            SetStatus("Scanne Ordner...", 0);

            var itemsByEpisodeKey = new Dictionary<string, BatchEpisodeItemViewModel>(StringComparer.OrdinalIgnoreCase);
            var mainVideoFiles = Directory.GetFiles(SourceDirectory, "*.mp4")
                .Where(file => !LooksLikeAudioDescription(file))
                .OrderBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var total = mainVideoFiles.Count;
            for (var index = 0; index < total; index++)
            {
                var file = mainVideoFiles[index];
                try
                {
                    var detected = await _services.SeriesEpisodeMux.DetectFromSelectedVideoAsync(
                        file,
                        update => HandleBatchDetectionProgress(index + 1, total, file, update));
                    var localGuess = new EpisodeMetadataGuess(
                        detected.SeriesName,
                        detected.SuggestedTitle,
                        detected.SeasonNumber,
                        detected.EpisodeNumber);
                    SetStatus($"Scanne Ordner... {index + 1}/{total} - {Path.GetFileName(file)} - TVDB-Abgleich", CalculatePercent(index, total));
                    var metadataResolution = await ResolveMetadataAsync(detected);
                    detected = ApplyMetadataSelection(detected, metadataResolution);
                    var outputPath = BuildOutputPath(detected);
                    var episodeKey = Path.GetFileName(outputPath);

                    if (itemsByEpisodeKey.TryGetValue(episodeKey, out var existingItem))
                    {
                        existingItem.AddRequestedSource(file);
                        AppendLog($"DUBLETTE: {Path.GetFileName(file)} -> wird bereits ueber {Path.GetFileName(existingItem.MainVideoPath)} verarbeitet.");
                    }
                    else
                    {
                        var outputAlreadyExists = File.Exists(outputPath);
                        var item = BatchEpisodeItemViewModel.CreateFromDetection(
                            requestedMainVideoPath: file,
                            localGuess: localGuess,
                            detected: detected,
                            metadataResolution: metadataResolution,
                            outputPath: outputPath,
                            status: outputAlreadyExists ? "Archiv vorhanden" : "Bereit",
                            isSelected: true);

                        AddEpisodeItem(item);
                        itemsByEpisodeKey[episodeKey] = item;

                        AppendLog(outputAlreadyExists
                            ? $"OK: {Path.GetFileName(file)} -> Archivdatei vorhanden, wird spaeter genauer verglichen."
                            : $"OK: {Path.GetFileName(file)}");
                    }
                }
                catch (Exception ex)
                {
                    AddEpisodeItem(BatchEpisodeItemViewModel.CreateErrorItem(file, ex.Message));
                    AppendLog($"FEHLER: {Path.GetFileName(file)} -> {ex.Message}");
                }

                SetStatus($"Scanne Ordner... {index + 1}/{total}", CalculatePercent(index + 1, total));
                await Task.Yield();
            }

            var preselectedCount = EpisodeItems.Count(item => item.IsSelected);
            SetStatus($"Scan abgeschlossen: {EpisodeItems.Count} Eintraege, {preselectedCount} vorausgewaehlt", 100);
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
            _ = RefreshSelectedItemPlanSummaryAsync();
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
            _ = RefreshSelectedItemPlanSummaryAsync();
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
            _ = RefreshSelectedItemPlanSummaryAsync();
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
            _ = RefreshSelectedItemPlanSummaryAsync();
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
            SetStatus("Anhaenge geleert", ProgressValue);
            _ = RefreshSelectedItemPlanSummaryAsync();
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
            SetStatus("Anhaenge aktualisiert", ProgressValue);
            _ = RefreshSelectedItemPlanSummaryAsync();
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
            _ = RefreshSelectedItemPlanSummaryAsync();
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

    private async Task ReviewPendingSourcesAsync()
    {
        var selectedItems = EpisodeItems.Where(item => item.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            _dialogService.ShowWarning("Hinweis", "Bitte zuerst mindestens eine Episode fuer den Batch auswaehlen.");
            return;
        }

        var readyItems = selectedItems
            .Where(item => item.Status is not "Fehler" and not "Existiert bereits")
            .ToList();

        if (readyItems.Count == 0)
        {
            _dialogService.ShowWarning("Hinweis", "Es gibt keine gueltigen Episoden fuer den Batch.");
            return;
        }

        var approved = await EnsurePendingChecksApprovedAsync(readyItems);
        if (approved)
        {
            _dialogService.ShowInfo("Hinweis", "Alle offenen Quellen- und TVDB-Pruefungen wurden abgeschlossen.");
        }
    }

    private async Task RunBatchAsync()
    {
        var selectedItems = EpisodeItems.Where(item => item.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            _dialogService.ShowWarning("Hinweis", "Bitte mindestens eine Episode fuer den Batch auswaehlen.");
            return;
        }

        var readyItems = selectedItems
            .Where(item => item.Status is not "Fehler" and not "Existiert bereits")
            .ToList();

        if (readyItems.Count == 0)
        {
            _dialogService.ShowWarning("Hinweis", "Es gibt keine gueltigen Episoden fuer den Batch.");
            return;
        }

        var approved = await EnsurePendingChecksApprovedAsync(readyItems);
        if (!approved)
        {
            _dialogService.ShowWarning(
                "Hinweis",
                "Der Batch wurde abgebrochen, weil nicht alle pruefpflichtigen Quellen freigegeben wurden.");
            SetStatus("Batch abgebrochen", 0);
            return;
        }

        SetStatus("Erstelle Mux-Plaene...", 0);
        var executablePlans = new List<(BatchEpisodeItemViewModel Item, SeriesEpisodeMuxPlan Plan)>();
        foreach (var item in readyItems)
        {
            try
            {
                var plan = await BuildPlanForItemAsync(item);
                if (plan.SkipMux)
                {
                    item.Status = "Archiv aktuell";
                    AppendLog($"SKIP: {item.MainVideoFileName} -> {plan.SkipReason}");
                    continue;
                }

                executablePlans.Add((item, plan));
            }
            catch (Exception ex)
            {
                item.Status = "Fehler";
                AppendLog($"PLAN-FEHLER: {item.MainVideoFileName} -> {ex.Message}");
            }
        }

        if (executablePlans.Count == 0)
        {
            SetStatus("Keine weiteren Mux-Vorgaenge noetig", 100);
            _dialogService.ShowInfo("Hinweis", "Alle ausgewaehlten Episoden sind bereits vollstaendig oder wurden wegen Fehlern uebersprungen.");
            return;
        }

        var copyPlans = executablePlans
            .Select(entry => entry.Plan.WorkingCopy)
            .Where(plan => plan is not null)
            .Cast<FileCopyPlan>()
            .GroupBy(plan => plan.SourceFilePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (copyPlans.Count > 0)
        {
            var totalCopyBytes = copyPlans.Sum(plan => plan.FileSizeBytes);
            if (!_dialogService.ConfirmBatchArchiveCopy(copyPlans.Count, totalCopyBytes))
            {
                SetStatus("Abgebrochen", 0);
                return;
            }

            await CopyArchiveFilesAsync(copyPlans, totalCopyBytes);
        }

        if (!_dialogService.ConfirmBatchStart(executablePlans.Count))
        {
            SetStatus("Abgebrochen", 0);
            return;
        }

        try
        {
            SetBusy(true);
            LogText = string.Empty;
            var successCount = 0;
            var warningCount = 0;
            var errorCount = 0;
            var movedDoneFiles = new List<string>();
            var doneDirectory = Path.Combine(SourceDirectory, DoneFolderName);

            for (var index = 0; index < executablePlans.Count; index++)
            {
                var (item, plan) = executablePlans[index];
                item.Status = "Laeuft";
                AppendLog($"STARTE: {item.MainVideoFileName}");

                try
                {
                    var result = await _services.SeriesEpisodeMux.ExecuteAsync(
                        plan,
                        line => AppendLog($"  {line}"),
                        update => UpdateRuntimeStatus(index + 1, executablePlans.Count, update));

                    if (result.ExitCode == 0 && !result.HasWarning)
                    {
                        item.Status = "Erfolgreich";
                        successCount++;
                        movedDoneFiles.AddRange(await MoveEpisodeFilesToDoneAsync(item, plan, doneDirectory));
                    }
                    else if ((result.ExitCode == 0 && result.HasWarning)
                        || (result.ExitCode == 1 && File.Exists(item.OutputPath)))
                    {
                        item.Status = "Warnung";
                        warningCount++;
                        movedDoneFiles.AddRange(await MoveEpisodeFilesToDoneAsync(item, plan, doneDirectory));
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
                    _services.Cleanup.DeleteTemporaryFile(plan.WorkingCopy?.DestinationFilePath);
                }

                SetStatus($"Batch laeuft... {index + 1}/{executablePlans.Count}", CalculatePercent(index + 1, executablePlans.Count));
            }

            await OfferBatchDoneCleanupAsync(doneDirectory, movedDoneFiles);

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
                status: outputAlreadyExists ? "Archiv vorhanden" : "Bereit");
            item.ReplaceExcludedSourcePaths(excludedSourcePaths ?? []);

            AppendLog($"AKTUALISIERT: {Path.GetFileName(selectedVideoPath)} -> {Path.GetFileName(item.MainVideoPath)}");
            SetStatus("Eintrag aktualisiert", 100);
            if (ReferenceEquals(SelectedEpisodeItem, item))
            {
                _ = RefreshSelectedItemPlanSummaryAsync();
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

    private string BuildOutputPath(AutoDetectedEpisodeFiles detected)
    {
        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            return detected.SuggestedOutputFilePath;
        }

        if (detected.SuggestedOutputFilePath.StartsWith(SeriesArchiveService.ArchiveRootDirectory, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(OutputDirectory, SeriesArchiveService.ArchiveRootDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return detected.SuggestedOutputFilePath;
            }

            var relativePath = Path.GetRelativePath(SeriesArchiveService.ArchiveRootDirectory, detected.SuggestedOutputFilePath);
            return Path.Combine(OutputDirectory, relativePath);
        }

        return Path.Combine(OutputDirectory, Path.GetFileName(detected.SuggestedOutputFilePath));
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

        item.SetPlanSummary(File.Exists(item.OutputPath)
            ? "Archivvergleich wird berechnet..."
            : "Verwendungsplan wird berechnet...");

        try
        {
            var plan = await BuildPlanForItemAsync(item);
            if (version != _selectedPlanSummaryVersion || !ReferenceEquals(SelectedEpisodeItem, item))
            {
                return;
            }

            item.SetPlanSummary(plan.BuildCompactSummaryText());
        }
        catch (Exception ex)
        {
            if (version != _selectedPlanSummaryVersion || !ReferenceEquals(SelectedEpisodeItem, item))
            {
                return;
            }

            item.SetPlanSummary("Plan konnte noch nicht berechnet werden: " + ex.Message);
        }
    }

    private async Task<IReadOnlyList<string>> MoveEpisodeFilesToDoneAsync(
        BatchEpisodeItemViewModel item,
        SeriesEpisodeMuxPlan plan,
        string doneDirectory)
    {
        var cleanupFiles = BuildBatchCleanupFileList(item, plan);
        if (cleanupFiles.Count == 0)
        {
            return [];
        }

        var moveResult = await _services.Cleanup.MoveFilesToDirectoryAsync(
            cleanupFiles,
            doneDirectory,
            (current, total, filePath) =>
            {
                var progress = total <= 0 ? 0 : (int)Math.Round(current * 100d / total);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    SetStatus($"Verschiebe Dateien nach 'done'... {current}/{total}", progress);
                });
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
        return item.RelatedEpisodeFilePaths
            .Concat(plan.GetReferencedInputFiles())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(File.Exists)
            .Where(path => path.StartsWith(SourceDirectory, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.StartsWith(SeriesArchiveService.ArchiveRootDirectory, StringComparison.OrdinalIgnoreCase))
            .Where(path => !string.Equals(path, item.OutputPath, StringComparison.OrdinalIgnoreCase))
            .Where(path => plan.WorkingCopy is null
                || !string.Equals(path, plan.WorkingCopy.DestinationFilePath, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task OfferBatchDoneCleanupAsync(string doneDirectory, IReadOnlyList<string> movedDoneFiles)
    {
        var doneFiles = movedDoneFiles
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (doneFiles.Count == 0)
        {
            return;
        }

        if (_dialogService.ConfirmBatchRecycleDoneFiles(doneFiles.Count, doneDirectory))
        {
            var recycleResult = await _services.Cleanup.RecycleFilesAsync(
                doneFiles,
                (current, total, _) =>
                {
                    var progress = total <= 0 ? 0 : (int)Math.Round(current * 100d / total);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        SetStatus($"Verschiebe Done-Dateien in den Papierkorb... {current}/{total}", progress);
                    });
                });

            if (recycleResult.FailedFiles.Count > 0)
            {
                _dialogService.ShowWarning(
                    "Warnung",
                    "Einige Dateien aus dem Done-Ordner konnten nicht in den Papierkorb verschoben werden:\n"
                    + string.Join(Environment.NewLine, recycleResult.FailedFiles.Select(Path.GetFileName)));
            }

            return;
        }

        if (_dialogService.AskOpenDoneDirectory(doneDirectory))
        {
            _dialogService.OpenPathWithDefaultApp(doneDirectory);
        }
    }

    private async Task CopyArchiveFilesAsync(IReadOnlyList<FileCopyPlan> copyPlans, long totalCopyBytes)
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
                    var progress = totalCopyBytes <= 0
                        ? 0
                        : (int)Math.Round(combinedCopiedBytes * 100d / totalCopyBytes);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        SetStatus($"Kopiere Archivdateien... {index + 1}/{copyPlans.Count}", progress);
                    });
                });

            copiedBeforeCurrentFile += copyPlan.FileSizeBytes;
        }
    }

    private async Task<bool> ReviewEpisodeAsync(BatchEpisodeItemViewModel item, bool isBatchPreparation)
    {
        while (item.RequiresManualCheck && !string.IsNullOrWhiteSpace(item.CurrentReviewTargetPath))
        {
            var reviewTargetPath = item.CurrentReviewTargetPath!;
            SetStatus(
                isBatchPreparation
                    ? $"Pruefe Quelle fuer '{item.Title}'..."
                    : "Pruefe Quelle...",
                ProgressValue);

            _dialogService.OpenFilesWithDefaultApp([reviewTargetPath]);

            var result = _dialogService.AskSourceReviewResult(
                Path.GetFileName(reviewTargetPath),
                canTryAlternative: true);

            if (result == MessageBoxResult.Cancel)
            {
                SetStatus("Quellenpruefung abgebrochen", ProgressValue);
                return false;
            }

            if (result == MessageBoxResult.Yes)
            {
                item.ApproveCurrentReviewTarget();
                SetStatus(
                    isBatchPreparation
                        ? $"Quelle fuer '{item.Title}' freigegeben"
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
                        ? $"Alternative Quelle fuer '{item.Title}' gewaehlt"
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
                ? $"Pruefe TVDB-Zuordnung fuer '{item.Title}'..."
                : "Pruefe TVDB-Zuordnung...",
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
            SetStatus("TVDB-Pruefung abgebrochen", ProgressValue);
            return false;
        }

        if (dialog.KeepLocalDetection)
        {
            item.ApplyLocalMetadataGuess();
            item.ApproveMetadataReview("Lokale Erkennung wurde bewusst beibehalten.");
            SetStatus(
                isBatchPreparation
                    ? $"Lokale Erkennung fuer '{item.Title}' freigegeben"
                    : "Lokale Erkennung freigegeben",
                100);
            return true;
        }

        if (dialog.SelectedEpisodeSelection is null)
        {
            SetStatus("TVDB-Pruefung abgebrochen", ProgressValue);
            return false;
        }

        item.ApplyTvdbSelection(dialog.SelectedEpisodeSelection);
        item.ApproveMetadataReview(
            $"TVDB manuell bestaetigt: S{dialog.SelectedEpisodeSelection.SeasonNumber}E{dialog.SelectedEpisodeSelection.EpisodeNumber} - {dialog.SelectedEpisodeSelection.EpisodeTitle}");

        SetStatus(
            isBatchPreparation
                ? $"TVDB-Zuordnung fuer '{item.Title}' freigegeben"
                : "TVDB-Zuordnung freigegeben",
            100);

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
            SetStatus("Keine offenen Pflichtpruefungen", ProgressValue);
            return true;
        }

        SetStatus("Pflichtpruefungen werden vorbereitet...", 0);

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

    private void UpdateRuntimeStatus(int currentItem, int totalItems, MuxExecutionUpdate update)
    {
        var currentBatchProgress = CalculatePercent(currentItem - 1, totalItems);
        if (update.ProgressPercent is int itemProgress)
        {
            var sliceSize = totalItems <= 0 ? 0d : 100d / totalItems;
            currentBatchProgress = (int)Math.Round(((currentItem - 1) * sliceSize) + (itemProgress / 100d * sliceSize));
        }

        var statusText = update.ProgressPercent is int itemProgressPercent
            ? $"Batch laeuft... {currentItem}/{totalItems} ({itemProgressPercent}% in aktueller Episode)"
            : $"Batch laeuft... {currentItem}/{totalItems}";

        if (update.HasWarning)
        {
            statusText += " - Warnung erkannt";
        }

        if (Application.Current.Dispatcher.CheckAccess())
        {
            SetStatus(statusText, currentBatchProgress);
            return;
        }

        Application.Current.Dispatcher.Invoke(() => SetStatus(statusText, currentBatchProgress));
    }

    private void AppendLog(string line)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            LogText += line + Environment.NewLine;
            return;
        }

        Application.Current.Dispatcher.Invoke(() => LogText += line + Environment.NewLine);
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
                (int)Math.Round(baseProgress));
        }

        if (Application.Current.Dispatcher.CheckAccess())
        {
            ApplyUpdate();
            return;
        }

        Application.Current.Dispatcher.Invoke(ApplyUpdate);
    }

    private void HandleSelectedItemDetectionProgress(DetectionProgressUpdate update)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            SetStatus(update.StatusText, update.ProgressPercent);
            return;
        }

        Application.Current.Dispatcher.Invoke(() => SetStatus(update.StatusText, update.ProgressPercent));
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
    }

    private void AddEpisodeItem(BatchEpisodeItemViewModel item)
    {
        item.PropertyChanged += EpisodeItem_PropertyChanged;
        EpisodeItems.Add(item);
    }

    private void EpisodeItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshCommands();
    }

    private void EpisodeItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshCommands();
    }

    private void SetStatus(string text, int progress)
    {
        StatusText = text;
        ProgressValue = Math.Clamp(progress, 0, 100);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class BatchEpisodeItemViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _status;
    private string _localSeriesName;
    private string _localSeasonNumber;
    private string _localEpisodeNumber;
    private string _localTitle;
    private string _seriesName;
    private string _seasonNumber;
    private string _episodeNumber;
    private string _requestedMainVideoPath;
    private string _mainVideoPath;
    private List<string> _requestedSourcePaths;
    private List<string> _additionalVideoPaths;
    private string? _audioDescriptionPath;
    private List<string> _subtitlePaths;
    private List<string> _attachmentPaths;
    private List<string> _relatedEpisodeFilePaths;
    private string _outputPath;
    private string _titleForMux;
    private string _metadataStatusText;
    private string _planSummaryText;
    private bool _requiresMetadataReview;
    private bool _isMetadataReviewApproved;
    private bool _outputPathWasManuallyChanged;
    private bool _requiresManualCheck;
    private List<string> _manualCheckFilePaths;
    private List<string> _notes;
    private string _detectionSeedPath;
    private readonly HashSet<string> _excludedSourcePaths = new(StringComparer.OrdinalIgnoreCase);
    private string? _approvedReviewPath;

    private BatchEpisodeItemViewModel(
        string requestedMainVideoPath,
        string mainVideoPath,
        string localSeriesName,
        string localSeasonNumber,
        string localEpisodeNumber,
        string localTitle,
        string seriesName,
        string seasonNumber,
        string episodeNumber,
        IReadOnlyList<string> additionalVideoPaths,
        string? audioDescriptionPath,
        IReadOnlyList<string> subtitlePaths,
        IReadOnlyList<string> attachmentPaths,
        IReadOnlyList<string> relatedEpisodeFilePaths,
        string outputPath,
        string titleForMux,
        string metadataStatusText,
        bool requiresMetadataReview,
        bool isMetadataReviewApproved,
        string status,
        string planSummaryText,
        bool isSelected,
        bool requiresManualCheck,
        IReadOnlyList<string> manualCheckFilePaths,
        IReadOnlyList<string> notes)
    {
        _requestedMainVideoPath = requestedMainVideoPath;
        _mainVideoPath = mainVideoPath;
        _localSeriesName = localSeriesName;
        _localSeasonNumber = localSeasonNumber;
        _localEpisodeNumber = localEpisodeNumber;
        _localTitle = localTitle;
        _seriesName = seriesName;
        _seasonNumber = seasonNumber;
        _episodeNumber = episodeNumber;
        _requestedSourcePaths = [requestedMainVideoPath];
        _additionalVideoPaths = additionalVideoPaths.ToList();
        _audioDescriptionPath = audioDescriptionPath;
        _subtitlePaths = subtitlePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        _attachmentPaths = attachmentPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        _relatedEpisodeFilePaths = relatedEpisodeFilePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        _outputPath = outputPath;
        _titleForMux = titleForMux;
        _metadataStatusText = metadataStatusText;
        _planSummaryText = planSummaryText;
        _requiresMetadataReview = requiresMetadataReview;
        _isMetadataReviewApproved = isMetadataReviewApproved;
        _status = status;
        _isSelected = isSelected;
        _requiresManualCheck = requiresManualCheck;
        _manualCheckFilePaths = manualCheckFilePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _notes = notes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _detectionSeedPath = requestedMainVideoPath;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title => TitleForMux;

    public string LocalSeriesName => _localSeriesName;

    public string LocalSeasonNumber => _localSeasonNumber;

    public string LocalEpisodeNumber => _localEpisodeNumber;

    public string LocalTitle => _localTitle;

    public string SeriesName
    {
        get => _seriesName;
        private set
        {
            if (_seriesName == value)
            {
                return;
            }

            _seriesName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MetadataDisplayText));
        }
    }

    public string SeasonNumber
    {
        get => _seasonNumber;
        private set
        {
            if (_seasonNumber == value)
            {
                return;
            }

            _seasonNumber = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EpisodeCodeDisplayText));
            OnPropertyChanged(nameof(MetadataDisplayText));
        }
    }

    public string EpisodeNumber
    {
        get => _episodeNumber;
        private set
        {
            if (_episodeNumber == value)
            {
                return;
            }

            _episodeNumber = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EpisodeCodeDisplayText));
            OnPropertyChanged(nameof(MetadataDisplayText));
        }
    }

    public string MainVideoFileName => Path.GetFileName(MainVideoPath);

    public string RequestedMainVideoPath => _requestedMainVideoPath;

    public string MainVideoPath
    {
        get => _mainVideoPath;
        private set
        {
            if (_mainVideoPath == value)
            {
                return;
            }

            _mainVideoPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MainVideoFileName));
            OnPropertyChanged(nameof(MainVideoDisplayText));
            OnPropertyChanged(nameof(SourceFilePaths));
        }
    }

    public IReadOnlyList<string> AdditionalVideoPaths => _additionalVideoPaths;

    public string? AudioDescriptionPath => _audioDescriptionPath;

    public IReadOnlyList<string> SubtitlePaths => _subtitlePaths;

    public IReadOnlyList<string> AttachmentPaths => _attachmentPaths;

    public IReadOnlyList<string> RelatedEpisodeFilePaths => _relatedEpisodeFilePaths;

    public string OutputPath
    {
        get => _outputPath;
        private set
        {
            if (_outputPath == value)
            {
                return;
            }

            _outputPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutputFileName));
        }
    }

    public string OutputFileName => Path.GetFileName(OutputPath);

    public string EpisodeCodeDisplayText => $"S{SeasonNumber}E{EpisodeNumber}";

    public string TitleForMux
    {
        get => _titleForMux;
        set
        {
            var normalized = value.Trim();
            if (_titleForMux == normalized)
            {
                return;
            }

            _titleForMux = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(MetadataDisplayText));
            UpdateSuggestedOutputPathIfAutomatic();
        }
    }

    public string MetadataStatusText
    {
        get => _metadataStatusText;
        private set
        {
            if (_metadataStatusText == value)
            {
                return;
            }

            _metadataStatusText = value;
            OnPropertyChanged();
        }
    }

    public string PlanSummaryText
    {
        get => _planSummaryText;
        private set
        {
            if (_planSummaryText == value)
            {
                return;
            }

            _planSummaryText = value;
            OnPropertyChanged();
        }
    }

    public string ArchiveStateText => File.Exists(OutputPath) ? "vorhanden" : "frei";

    public bool RequiresMetadataReview
    {
        get => _requiresMetadataReview;
        private set
        {
            if (_requiresMetadataReview == value)
            {
                return;
            }

            _requiresMetadataReview = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReviewHint));
            OnPropertyChanged(nameof(HasPendingChecks));
        }
    }

    public bool IsMetadataReviewApproved
    {
        get => _isMetadataReviewApproved;
        private set
        {
            if (_isMetadataReviewApproved == value)
            {
                return;
            }

            _isMetadataReviewApproved = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReviewHint));
            OnPropertyChanged(nameof(HasPendingChecks));
        }
    }

    public bool RequiresManualCheck
    {
        get => _requiresManualCheck;
        private set
        {
            if (_requiresManualCheck == value)
            {
                return;
            }

            _requiresManualCheck = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReviewHint));
            OnPropertyChanged(nameof(HasPendingChecks));
        }
    }

    public IReadOnlyList<string> ManualCheckFilePaths => _manualCheckFilePaths;

    public string? CurrentReviewTargetPath => _manualCheckFilePaths.FirstOrDefault();

    public bool IsManualCheckApproved => !RequiresManualCheck
        || string.Equals(_approvedReviewPath, CurrentReviewTargetPath, StringComparison.OrdinalIgnoreCase);

    public bool HasPendingChecks => (RequiresManualCheck && !IsManualCheckApproved)
        || (RequiresMetadataReview && !IsMetadataReviewApproved);

    public string ReviewHint
    {
        get
        {
            var pendingChecks = new List<string>();
            if (RequiresManualCheck && !IsManualCheckApproved)
            {
                pendingChecks.Add("Quelle");
            }

            if (RequiresMetadataReview && !IsMetadataReviewApproved)
            {
                pendingChecks.Add("TVDB");
            }

            if (pendingChecks.Count > 0)
            {
                return string.Join(" + ", pendingChecks) + " pruefen";
            }

            if (RequiresManualCheck || RequiresMetadataReview)
            {
                return "Freigegeben";
            }

            return string.Empty;
        }
    }

    public IReadOnlyList<string> Notes => _notes;

    public string DetectionSeedPath => _detectionSeedPath;

    public IReadOnlyCollection<string> ExcludedSourcePaths => _excludedSourcePaths;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (_status == value)
            {
                return;
            }

            _status = value;
            OnPropertyChanged();
        }
    }

    public string RequestedSourcesDisplayText => FormatPaths(_requestedSourcePaths);

    public string MainVideoDisplayText => MainVideoPath;

    public string MetadataDisplayText => $"{SeriesName} - {EpisodeCodeDisplayText} - {TitleForMux}";

    public string AdditionalVideosDisplayText => FormatPaths(_additionalVideoPaths);

    public string AudioDescriptionDisplayText => string.IsNullOrWhiteSpace(AudioDescriptionPath) ? "(keine)" : AudioDescriptionPath;

    public string VideoAndAudioDescriptionDisplayText
    {
        get
        {
            var lines = new List<string>();

            if (_additionalVideoPaths.Count > 0)
            {
                lines.Add("Weitere Videospuren:");
                lines.AddRange(_additionalVideoPaths);
            }

            lines.Add("AD:");
            lines.Add(string.IsNullOrWhiteSpace(AudioDescriptionPath) ? "(keine)" : AudioDescriptionPath!);

            return string.Join(Environment.NewLine, lines);
        }
    }

    public string SubtitleDisplayText => FormatPaths(_subtitlePaths);

    public string AttachmentDisplayText => FormatPaths(_attachmentPaths);

    public string NotesDisplayText => _notes.Count == 0
        ? string.Empty
        : string.Join(Environment.NewLine, _notes.Select(note => "- " + note));

    public IReadOnlyList<string> SourceFilePaths => EnumerateSourceFilePaths().ToList();

    public static BatchEpisodeItemViewModel CreateFromDetection(
        string requestedMainVideoPath,
        EpisodeMetadataGuess localGuess,
        AutoDetectedEpisodeFiles detected,
        EpisodeMetadataResolutionResult metadataResolution,
        string outputPath,
        string status,
        bool isSelected)
    {
        return new BatchEpisodeItemViewModel(
            requestedMainVideoPath,
            detected.MainVideoPath,
            localGuess.SeriesName,
            localGuess.SeasonNumber,
            localGuess.EpisodeNumber,
            localGuess.EpisodeTitle,
            detected.SeriesName,
            detected.SeasonNumber,
            detected.EpisodeNumber,
            detected.AdditionalVideoPaths,
            detected.AudioDescriptionPath,
            detected.SubtitlePaths,
            detected.AttachmentPaths,
            detected.RelatedFilePaths,
            outputPath,
            detected.SuggestedTitle,
            metadataResolution.StatusText,
            metadataResolution.RequiresReview,
            !metadataResolution.RequiresReview,
            status,
            File.Exists(outputPath)
                ? "Archiv-MKV vorhanden. Detail auswaehlen fuer den genauen Vergleich."
                : "Zieldatei noch nicht vorhanden. Neue MKV wird erstellt.",
            isSelected,
            detected.RequiresManualCheck,
            detected.ManualCheckFilePaths,
            detected.Notes);
    }

    public static BatchEpisodeItemViewModel CreateErrorItem(string requestedMainVideoPath, string errorMessage)
    {
        return new BatchEpisodeItemViewModel(
            requestedMainVideoPath,
            requestedMainVideoPath,
            Path.GetFileNameWithoutExtension(requestedMainVideoPath),
            "xx",
            "xx",
            Path.GetFileNameWithoutExtension(requestedMainVideoPath),
            Path.GetFileNameWithoutExtension(requestedMainVideoPath),
            "xx",
            "xx",
            [],
            null,
            [],
            [],
            [],
            string.Empty,
            Path.GetFileNameWithoutExtension(requestedMainVideoPath),
            "Keine TVDB-Daten vorhanden.",
            false,
            true,
            "Fehler",
            "Keine Plan-Zusammenfassung verfuegbar.",
            isSelected: false,
            requiresManualCheck: false,
            manualCheckFilePaths: [],
            notes: [errorMessage]);
    }

    public void AddRequestedSource(string requestedSourcePath)
    {
        if (_requestedSourcePaths.Contains(requestedSourcePath, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _requestedSourcePaths.Add(requestedSourcePath);
        _requestedSourcePaths = _requestedSourcePaths
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_notes.All(note => !note.Contains("Weitere gefundene Quelldateien", StringComparison.OrdinalIgnoreCase)))
        {
            _notes.Add("Weitere gefundene Quelldateien wurden dieser Episode automatisch zugeordnet.");
            OnPropertyChanged(nameof(Notes));
            OnPropertyChanged(nameof(NotesDisplayText));
        }

        OnPropertyChanged(nameof(RequestedSourcesDisplayText));
    }

    public void ApplyDetection(
        string requestedMainVideoPath,
        EpisodeMetadataGuess localGuess,
        AutoDetectedEpisodeFiles detected,
        EpisodeMetadataResolutionResult metadataResolution,
        string outputPath,
        string status)
    {
        _requestedMainVideoPath = requestedMainVideoPath;
        _detectionSeedPath = requestedMainVideoPath;
        _requestedSourcePaths = [requestedMainVideoPath];
        _localSeriesName = localGuess.SeriesName;
        _localSeasonNumber = localGuess.SeasonNumber;
        _localEpisodeNumber = localGuess.EpisodeNumber;
        _localTitle = localGuess.EpisodeTitle;
        MainVideoPath = detected.MainVideoPath;
        SeriesName = detected.SeriesName;
        SeasonNumber = detected.SeasonNumber;
        EpisodeNumber = detected.EpisodeNumber;
        _additionalVideoPaths = detected.AdditionalVideoPaths.ToList();
        _audioDescriptionPath = detected.AudioDescriptionPath;
        _subtitlePaths = detected.SubtitlePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        _attachmentPaths = detected.AttachmentPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        _relatedEpisodeFilePaths = detected.RelatedFilePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        _outputPathWasManuallyChanged = false;
        OutputPath = outputPath;
        TitleForMux = detected.SuggestedTitle;
        MetadataStatusText = metadataResolution.StatusText;
        RequiresMetadataReview = metadataResolution.RequiresReview;
        IsMetadataReviewApproved = !metadataResolution.RequiresReview;
        Status = status;
        PlanSummaryText = File.Exists(outputPath)
            ? "Archiv-MKV vorhanden. Detail auswaehlen fuer den genauen Vergleich."
            : "Zieldatei noch nicht vorhanden. Neue MKV wird erstellt.";
        RequiresManualCheck = detected.RequiresManualCheck;
        _manualCheckFilePaths = detected.ManualCheckFilePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!RequiresManualCheck || !string.Equals(_approvedReviewPath, CurrentReviewTargetPath, StringComparison.OrdinalIgnoreCase))
        {
            _approvedReviewPath = null;
        }
        _notes = detected.Notes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        IsSelected = status != "Existiert bereits";

        OnPropertyChanged(nameof(RequestedMainVideoPath));
        OnPropertyChanged(nameof(RequestedSourcesDisplayText));
        OnPropertyChanged(nameof(SeriesName));
        OnPropertyChanged(nameof(SeasonNumber));
        OnPropertyChanged(nameof(EpisodeNumber));
        OnPropertyChanged(nameof(EpisodeCodeDisplayText));
        OnPropertyChanged(nameof(AdditionalVideoPaths));
        OnPropertyChanged(nameof(AdditionalVideosDisplayText));
        OnPropertyChanged(nameof(AudioDescriptionPath));
        OnPropertyChanged(nameof(AudioDescriptionDisplayText));
        OnPropertyChanged(nameof(VideoAndAudioDescriptionDisplayText));
        OnPropertyChanged(nameof(SubtitlePaths));
        OnPropertyChanged(nameof(SubtitleDisplayText));
        OnPropertyChanged(nameof(AttachmentPaths));
        OnPropertyChanged(nameof(AttachmentDisplayText));
        OnPropertyChanged(nameof(MetadataStatusText));
        OnPropertyChanged(nameof(PlanSummaryText));
        OnPropertyChanged(nameof(MetadataDisplayText));
        OnPropertyChanged(nameof(ArchiveStateText));
        OnPropertyChanged(nameof(IsMetadataReviewApproved));
        OnPropertyChanged(nameof(ManualCheckFilePaths));
        OnPropertyChanged(nameof(IsManualCheckApproved));
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(NotesDisplayText));
        OnPropertyChanged(nameof(SourceFilePaths));
    }

    public void SetAudioDescription(string? path)
    {
        _audioDescriptionPath = string.IsNullOrWhiteSpace(path) ? null : path;
        _approvedReviewPath = null;
        OnPropertyChanged(nameof(AudioDescriptionPath));
        OnPropertyChanged(nameof(AudioDescriptionDisplayText));
        OnPropertyChanged(nameof(VideoAndAudioDescriptionDisplayText));
        OnPropertyChanged(nameof(SourceFilePaths));
        OnPropertyChanged(nameof(IsManualCheckApproved));
        OnPropertyChanged(nameof(ReviewHint));
    }

    public void SetSubtitles(IEnumerable<string> paths)
    {
        _subtitlePaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        OnPropertyChanged(nameof(SubtitlePaths));
        OnPropertyChanged(nameof(SubtitleDisplayText));
    }

    public void SetAttachments(IEnumerable<string> paths)
    {
        _attachmentPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        OnPropertyChanged(nameof(AttachmentPaths));
        OnPropertyChanged(nameof(AttachmentDisplayText));
    }

    public void SetOutputPath(string outputPath)
    {
        _outputPathWasManuallyChanged = true;
        OutputPath = outputPath;
        Status = File.Exists(outputPath) ? "Existiert bereits" : "Bereit";
        PlanSummaryText = File.Exists(outputPath)
            ? "Archiv-MKV vorhanden. Detail auswaehlen fuer den genauen Vergleich."
            : "Zieldatei noch nicht vorhanden. Neue MKV wird erstellt.";
        OnPropertyChanged(nameof(ArchiveStateText));
        IsSelected = Status != "Existiert bereits";
    }

    public void SetPlanSummary(string summaryText)
    {
        PlanSummaryText = summaryText;
    }

    public void ReplaceExcludedSourcePaths(IEnumerable<string> excludedSourcePaths)
    {
        _excludedSourcePaths.Clear();
        foreach (var excludedSourcePath in excludedSourcePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            _excludedSourcePaths.Add(excludedSourcePath);
        }
    }

    public void ApproveCurrentReviewTarget()
    {
        _approvedReviewPath = CurrentReviewTargetPath;
        OnPropertyChanged(nameof(IsManualCheckApproved));
        OnPropertyChanged(nameof(ReviewHint));
        OnPropertyChanged(nameof(HasPendingChecks));
    }

    public void ApplyTvdbSelection(TvdbEpisodeSelection selection)
    {
        SeriesName = selection.TvdbSeriesName;
        SeasonNumber = selection.SeasonNumber;
        EpisodeNumber = selection.EpisodeNumber;
        TitleForMux = selection.EpisodeTitle;
        UpdateSuggestedOutputPathIfAutomatic();
        OnPropertyChanged(nameof(MetadataDisplayText));
    }

    public void ApplyLocalMetadataGuess()
    {
        SeriesName = _localSeriesName;
        SeasonNumber = _localSeasonNumber;
        EpisodeNumber = _localEpisodeNumber;
        TitleForMux = _localTitle;
        UpdateSuggestedOutputPathIfAutomatic();
        OnPropertyChanged(nameof(MetadataDisplayText));
    }

    public void ApproveMetadataReview(string statusText)
    {
        MetadataStatusText = statusText;
        RequiresMetadataReview = false;
        IsMetadataReviewApproved = true;
    }

    private IEnumerable<string> EnumerateSourceFilePaths()
    {
        if (!string.IsNullOrWhiteSpace(MainVideoPath))
        {
            yield return MainVideoPath;
        }

        foreach (var additionalVideoPath in _additionalVideoPaths)
        {
            yield return additionalVideoPath;
        }

        if (!string.IsNullOrWhiteSpace(AudioDescriptionPath))
        {
            yield return AudioDescriptionPath!;
        }
    }

    private static string FormatPaths(IEnumerable<string> paths)
    {
        var list = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return list.Count == 0
            ? "(keine)"
            : string.Join(Environment.NewLine, list);
    }

    private void UpdateSuggestedOutputPathIfAutomatic()
    {
        if (_outputPathWasManuallyChanged || string.IsNullOrWhiteSpace(OutputPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(OutputPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        OutputPath = EpisodeMetadataMergeHelper.BuildSuggestedOutputFilePath(
            directory,
            SeriesName,
            SeasonNumber,
            EpisodeNumber,
            TitleForMux);
        Status = File.Exists(OutputPath) ? "Existiert bereits" : "Bereit";
        IsSelected = Status != "Existiert bereits";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
