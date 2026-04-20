using System.Threading;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

// Dieser Partial verbindet das allgemeine Review-Workflow-Objekt mit den Batch-Zeilen.
internal sealed partial class BatchMuxViewModel
{
    private async Task ReviewPendingSourcesAsync()
    {
        var selectedItems = EpisodeItems.Where(item => item.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            _dialogService.ShowWarning("Hinweis", "Bitte zuerst mindestens eine Episode für den Batch auswählen.");
            return;
        }

        var readyItems = selectedItems
            .Where(item => !item.HasErrorStatus)
            .ToList();

        if (readyItems.Count == 0)
        {
            _dialogService.ShowWarning("Hinweis", "Es gibt keine gültigen Episoden für den Batch.");
            return;
        }

        var approved = await EnsurePendingChecksApprovedAsync(readyItems);
        if (approved)
        {
            _dialogService.ShowInfo("Hinweis", "Alle offenen Quellen-, TVDB- und Hinweisprüfungen wurden abgeschlossen.");
        }
    }

    private async Task<bool> ReviewEpisodeAsync(BatchEpisodeItemViewModel item, bool isBatchPreparation)
    {
        return await _reviewWorkflow.ReviewManualSourceAsync(
            item,
            SetStatus,
            ProgressValue,
            isBatchPreparation
                ? $"Prüfe Quelle für '{item.Title}'..."
                : "Prüfe Quelle...",
            "Quellenprüfung abgebrochen",
            isBatchPreparation
                ? $"Quellenprüfung für '{item.Title}' konnte nicht geöffnet werden"
                : "Quellenprüfung konnte nicht geöffnet werden",
            isBatchPreparation
                ? $"Quelle für '{item.Title}' freigegeben"
                : "Quelle freigegeben",
            isBatchPreparation
                ? $"Alternative Quelle für '{item.Title}' gewählt"
                : "Auf alternative Quelle umgestellt",
            tentativeExclusions => ApplyDetectionToItemAsync(item, item.DetectionSeedPath, tentativeExclusions));
    }

    private async Task<bool> ReviewEpisodeMetadataAsync(BatchEpisodeItemViewModel item, bool isBatchPreparation)
    {
        // Die explizite Detailaktion im Batch soll den TVDB-Dialog immer wieder öffnen können.
        // Nur die automatische Pflichtprüfungs-Schleife filtert weiterhin separat auf offene Fälle.
        var episodeChanged = false;
        var outcome = await _reviewWorkflow.ReviewMetadataAsync(
            item,
            SetStatus,
            ProgressValue,
            isBatchPreparation
                ? $"Prüfe TVDB-Zuordnung für '{item.Title}'..."
                : "Prüfe TVDB-Zuordnung...",
            "TVDB-Prüfung abgebrochen",
            isBatchPreparation
                ? $"Lokale Erkennung für '{item.Title}' freigegeben"
                : "Lokale Erkennung freigegeben",
            isBatchPreparation
                ? $"TVDB-Zuordnung für '{item.Title}' freigegeben"
                : "TVDB-Zuordnung freigegeben",
            () =>
            {
                episodeChanged = true;
                _planCache.Invalidate(item);
                RefreshAutomaticOutputPath(item);
            });

        // Eine manuelle TVDB- oder lokale Metadatenkorrektur kann Zielpfad, Titel und damit auch
        // Archivhinweise verändern. Der Pflichtcheck darf danach nicht mit einer alten Vorschau
        // weiterlaufen, sonst verschwinden Hinweise wie "Mehrfachfolge prüfen" bis zur nächsten
        // manuellen Detailaktualisierung.
        if (outcome != EpisodeMetadataReviewOutcome.Cancelled && episodeChanged)
        {
            await RefreshComparisonForItemAsync(item, preserveCurrentPresentation: false);
        }
        else if (ReferenceEquals(SelectedEpisodeItem, item))
        {
            ScheduleSelectedItemPlanSummaryRefresh();
        }

        return outcome != EpisodeMetadataReviewOutcome.Cancelled;
    }

    private bool CanReviewPendingSources()
    {
        return !_isBusy && EpisodeItems.Any(item => item.IsSelected && item.HasPendingChecks);
    }

    private async Task<bool> EnsurePendingChecksApprovedAsync(
        IReadOnlyList<BatchEpisodeItemViewModel> readyItems,
        CancellationToken cancellationToken = default)
    {
        var pendingSourceItems = readyItems
            .Where(item => item.RequiresManualCheck && !item.IsManualCheckApproved)
            .ToList();

        var pendingMetadataItems = readyItems
            .Where(item => item.RequiresMetadataReview && !item.IsMetadataReviewApproved)
            .ToList();

        if (pendingSourceItems.Count == 0
            && pendingMetadataItems.Count == 0
            && !readyItems.Any(item => item.HasPendingPlanReview))
        {
            SetStatus("Keine offenen Pflichtprüfungen", ProgressValue);
            return true;
        }

        SetStatus("Pflichtprüfungen werden vorbereitet...", 0);

        foreach (var item in pendingSourceItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SelectedEpisodeItem = item;
            var approved = await ReviewEpisodeAsync(item, isBatchPreparation: true);
            if (!approved)
            {
                return false;
            }
        }

        foreach (var item in pendingMetadataItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SelectedEpisodeItem = item;
            var approved = await ReviewEpisodeMetadataAsync(item, isBatchPreparation: true);
            if (!approved)
            {
                return false;
            }
        }

        var pendingPlanReviewItems = readyItems
            .Where(item => item.HasPendingPlanReview)
            .ToList();
        if (pendingPlanReviewItems.Count > 0)
        {
            foreach (var item in pendingPlanReviewItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SelectedEpisodeItem = item;
                if (!_dialogService.ConfirmPlanReview(item.Title, item.PrimaryActionablePlanNote))
                {
                    SetStatus("Hinweisprüfung abgebrochen", ProgressValue);
                    return false;
                }

                item.ApprovePlanReview();
                item.RefreshArchivePresence();
            }

            RefreshOverview();
            RefreshCommands();
            SetStatus("Fachliche Hinweise freigegeben", ProgressValue);
        }

        return true;
    }

    private static string ResolveSelectedOutputDirectory(BatchEpisodeItemViewModel item)
    {
        var outputDirectory = Path.GetDirectoryName(item.OutputPath);
        var existingOutputDirectory = ResolveNearestExistingDirectory(outputDirectory);
        if (!string.IsNullOrWhiteSpace(existingOutputDirectory))
        {
            return existingOutputDirectory;
        }

        return ResolveSelectedItemDirectory(item);
    }

    private static string? ResolveNearestExistingDirectory(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return null;
        }

        var directory = new DirectoryInfo(directoryPath);
        while (directory is not null)
        {
            if (directory.Exists)
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
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
        var includeHiddenItems = SelectedFilterMode.Key != BatchEpisodeFilterMode.All
            && _dialogService.ConfirmApplyBatchSelectionToAllItems(selectItems: true);
        var changedCount = includeHiddenItems
            ? _episodeCollection.SelectAllItems()
            : _episodeCollection.SelectAllVisible();
        SetStatus(
            SelectedFilterMode.Key == BatchEpisodeFilterMode.All || includeHiddenItems
                ? $"Alle Episoden ausgewählt ({changedCount} geändert)"
                : $"Gefilterte Episoden ausgewählt ({changedCount} geändert)",
            ProgressValue);
    }

    private void DeselectAllEpisodes()
    {
        var includeHiddenItems = SelectedFilterMode.Key != BatchEpisodeFilterMode.All
            && _dialogService.ConfirmApplyBatchSelectionToAllItems(selectItems: false);
        var changedCount = includeHiddenItems
            ? _episodeCollection.DeselectAllItems()
            : _episodeCollection.DeselectAllVisible();
        SetStatus(
            SelectedFilterMode.Key == BatchEpisodeFilterMode.All || includeHiddenItems
                ? $"Auswahl geleert ({changedCount} geändert)"
                : $"Gefilterte Auswahl geleert ({changedCount} geändert)",
            ProgressValue);
    }

    private void ToggleSelectedEpisodeSelection()
    {
        if (_isBusy || SelectedEpisodeItem is not BatchEpisodeItemViewModel item)
        {
            return;
        }

        item.IsSelected = !item.IsSelected;
    }

}
