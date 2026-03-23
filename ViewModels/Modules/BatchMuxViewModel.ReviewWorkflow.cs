using System.Threading;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

public sealed partial class BatchMuxViewModel
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
                ? $"Quelle für '{item.Title}' freigegeben"
                : "Quelle freigegeben",
            isBatchPreparation
                ? $"Alternative Quelle für '{item.Title}' gewählt"
                : "Auf alternative Quelle umgestellt",
            tentativeExclusions => ApplyDetectionToItemAsync(item, item.DetectionSeedPath, tentativeExclusions));
    }

    private async Task<bool> ReviewEpisodeMetadataAsync(BatchEpisodeItemViewModel item, bool isBatchPreparation)
    {
        if (!item.RequiresMetadataReview || item.IsMetadataReviewApproved)
        {
            return true;
        }

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
                RefreshAutomaticOutputPath(item);
                if (ReferenceEquals(SelectedEpisodeItem, item))
                {
                    ScheduleSelectedItemPlanSummaryRefresh();
                }
            });

        return outcome != EpisodeMetadataReviewOutcome.Cancelled;
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
        _episodeCollection.SelectAll();
    }

    private void DeselectAllEpisodes()
    {
        _episodeCollection.DeselectAll();
    }

}
