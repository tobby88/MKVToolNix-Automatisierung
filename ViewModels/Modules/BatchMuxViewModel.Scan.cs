using System.Threading;
using System.Windows;
using System.Windows.Threading;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

// Dieser Partial enthält Ordnerauswahl, Batch-Scan und die Erstbefüllung der UI-Liste.
internal sealed partial class BatchMuxViewModel
{
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
            if (_services.Archive.IsArchiveAvailable())
            {
                OutputDirectory = _services.Archive.ArchiveRootDirectory;
            }
            else
            {
                OutputDirectory = path;
                _dialogService.ShowWarning("Serienbibliothek", _services.Archive.BuildArchiveUnavailableWarningMessage());
            }
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
        var cancellationToken = CancellationToken.None;
        var operationStarted = false;
        try
        {
            SetBusy(true);
            cancellationToken = BeginBatchOperation(BatchOperationKind.Scan);
            operationStarted = true;
            ClearEpisodeItems();
            ResetLog();
            SetStatus("Bereite Batch-Scan vor...", 0);

            var sourceDirectory = SourceDirectory;
            var directoryContext = await Task.Run(
                () => _services.BatchScan.CreateDirectoryContext(sourceDirectory, cancellationToken),
                cancellationToken);
            var mainVideoFiles = directoryContext.MainVideoFiles;

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
                directoryContext,
                file,
                index,
                total,
                throttler,
                scanResults,
                cancellationToken,
                () => Volatile.Read(ref completedCount),
                () =>
                {
                    var processed = Interlocked.Increment(ref completedCount);
                    DispatchBatchProgress(() =>
                    {
                        // Einzelne Abschlussmeldungen aus den parallelen Scan-Tasks können noch kurz
                        // später in der UI ankommen. Der globale Fortschritt darf dabei nie hinter
                        // einen bereits angezeigten Vergleichsstand zurückfallen.
                        SetStatus(
                            $"Scanne Ordner... Datei {processed}/{total} abgeschlossen",
                            Math.Max(
                                ProgressValue,
                                ScaleProgress(CalculatePercent(processed, total), 0, AutomaticCompareProgressStart)));
                    });
                }));

            await Task.WhenAll(scanTasks);
            cancellationToken.ThrowIfCancellationRequested();
            await FlushPendingScanUiUpdatesAsync();
            SetStatus("Verarbeite Scanergebnisse...", AutomaticCompareProgressStart);

            var scannedItems = new List<BatchEpisodeItemViewModel>();

            foreach (var result in scanResults.OrderBy(result => result.Index))
            {
                if (result.ErrorMessage is not null)
                {
                    scannedItems.Add(BatchEpisodeItemViewModel.CreateErrorItem(result.SourcePath, result.ErrorMessage));
                    AppendLog($"FEHLER: {Path.GetFileName(result.SourcePath)} -> {result.ErrorMessage}");
                    continue;
                }

                var detected = result.Detected!;
                var localGuess = result.LocalGuess!;
                var metadataResolution = result.MetadataResolution!;
                var outputPath = result.OutputPath!;
                var relatedExistingItem = scannedItems.FirstOrDefault(item => IsSameDetectedSourceGroup(item, detected));
                if (relatedExistingItem is not null)
                {
                    relatedExistingItem.AddRequestedSource(result.SourcePath);
                    AppendLog($"DUBLETTE: {Path.GetFileName(result.SourcePath)} -> wird bereits über {Path.GetFileName(relatedExistingItem.MainVideoPath)} verarbeitet.");
                    continue;
                }

                var outputAlreadyExists = File.Exists(outputPath);
                var isArchiveTargetPath = _services.OutputPaths.IsArchivePath(outputPath);
                var statusKind = DetermineInitialStatus(detected, outputAlreadyExists, isArchiveTargetPath);

                var item = BatchEpisodeItemViewModel.CreateFromDetection(
                    requestedMainVideoPath: result.SourcePath,
                    localGuess: localGuess,
                    detected: detected,
                    metadataResolution: metadataResolution,
                    outputPath: outputPath,
                    statusKind: statusKind,
                    isSelected: true,
                    isArchiveTargetPath: isArchiveTargetPath);

                scannedItems.Add(item);
                AppendLog(BuildScanSuccessLogLine(result.SourcePath, detected, outputAlreadyExists, isArchiveTargetPath));
            }

            _episodeCollection.Reset(scannedItems);
            RefreshOutputTargetCollisions(EpisodeItems);

            var preselectedCount = EpisodeItems.Count(item => item.IsSelected);
            SetStatus(
                $"Scan abgeschlossen: {EpisodeItems.Count} Einträge, {preselectedCount} vorausgewählt",
                AutomaticCompareProgressStart);
            var archiveComparisonItems = EpisodeItems.Where(item => item.HasArchiveComparisonTarget).ToList();
            if (archiveComparisonItems.Count > 0)
            {
                ChangeCurrentBatchOperationKind(BatchOperationKind.Comparison);
            }

            await RefreshComparisonPlansAsync(
                archiveComparisonItems,
                automatic: true,
                cancellationToken);
            InvalidateBatchProgressCallbacks();
            SetStatus(StatusText, 100);
            RefreshCommands();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            InvalidateBatchProgressCallbacks();
            var comparisonWasCancelled = _operationController.CurrentOperationKind == BatchOperationKind.Comparison;
            AppendLog(comparisonWasCancelled
                ? "ABGEBROCHEN: Archivvergleich durch Benutzer abgebrochen."
                : "ABGEBROCHEN: Batch-Scan durch Benutzer abgebrochen.");
            SetStatus(comparisonWasCancelled ? "Archivvergleich abgebrochen" : "Scan abgebrochen", ProgressValue);
        }
        finally
        {
            if (operationStarted)
            {
                CompleteCurrentBatchOperation();
            }

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

        var cancellationToken = CancellationToken.None;
        var operationStarted = false;
        try
        {
            cancellationToken = BeginBatchOperation(BatchOperationKind.Scan);
            operationStarted = true;
            await ApplyDetectionToItemAsync(item, path, item.ExcludedSourcePaths, cancellationToken);
        }
        finally
        {
            if (operationStarted)
            {
                CompleteCurrentBatchOperation();
            }
        }
    }

    private static async Task FlushPendingScanUiUpdatesAsync()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        await dispatcher.InvokeAsync(
            static () => { },
            DispatcherPriority.ApplicationIdle);
    }

    private static string BuildScanSuccessLogLine(
        string sourcePath,
        AutoDetectedEpisodeFiles detected,
        bool outputAlreadyExists,
        bool isArchiveTargetPath)
    {
        if (!detected.HasPrimaryVideoSource)
        {
            return outputAlreadyExists && isArchiveTargetPath
                ? $"HINWEIS: {Path.GetFileName(sourcePath)} -> Nur Zusatzmaterial erkannt. Die vorhandene Bibliotheksdatei wird später als Hauptquelle geprüft."
                : $"HINWEIS: {Path.GetFileName(sourcePath)} -> Nur Zusatzmaterial erkannt. Ohne vorhandene Bibliotheks-MKV aktuell nicht ausführbar.";
        }

        return outputAlreadyExists
            ? $"OK: {Path.GetFileName(sourcePath)} -> In der Serienbibliothek bereits vorhanden, wird später genauer verglichen."
            : $"OK: {Path.GetFileName(sourcePath)}";
    }

    private static bool IsSameDetectedSourceGroup(
        BatchEpisodeItemViewModel existingItem,
        AutoDetectedEpisodeFiles detected)
    {
        // Mehrere Einstiegsvideos derselben erkannten Episode werden weiterhin nur einmal
        // als Batch-Zeile geführt. Ein gleicher Zielpfad allein reicht dafür aber nicht:
        // bei Einzelfolge/Doppelfolge-Konstellationen wie "Rififi" muss die zweite Quelle
        // sichtbar bleiben, damit der Episodencode manuell auf SxxEyy-Ezz korrigierbar ist.
        return existingItem.SourceFilePaths.Any(existingPath =>
            detected.RelatedFilePaths.Any(detectedPath =>
                PathComparisonHelper.AreSamePath(existingPath, detectedPath)));
    }

}
