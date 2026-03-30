using System.Windows;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.Windows;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Gemeinsame Oberfläche für Einzel- und Batch-Episoden, damit Review-Dialoge dieselbe Logik verwenden können.
/// </summary>
public interface IEpisodeReviewItem
{
    string ReviewTitle { get; }
    string SeriesName { get; }
    string SeasonNumber { get; }
    string EpisodeNumber { get; }
    string Title { get; }
    bool RequiresManualCheck { get; }
    bool IsManualCheckApproved { get; }
    string? CurrentReviewTargetPath { get; }
    string DetectionSeedPath { get; }
    IReadOnlyCollection<string> ExcludedSourcePaths { get; }
    bool RequiresMetadataReview { get; }
    bool IsMetadataReviewApproved { get; }
    string LocalSeriesName { get; }
    string LocalSeasonNumber { get; }
    string LocalEpisodeNumber { get; }
    string LocalTitle { get; }
    void ApproveCurrentReviewTarget();
    void ApplyLocalMetadataGuess();
    void ApplyTvdbSelection(TvdbEpisodeSelection selection);
    void ApproveMetadataReview(string statusText);
}

/// <summary>
/// Kapselt Pflichtprüfungen für Quelle und TVDB-Metadaten inklusive Dialogabfolge.
/// </summary>
public sealed class EpisodeReviewWorkflow
{
    private readonly IUserDialogService _dialogService;
    private readonly EpisodeMetadataLookupService _episodeMetadata;

    public EpisodeReviewWorkflow(IUserDialogService dialogService, EpisodeMetadataLookupService episodeMetadata)
    {
        _dialogService = dialogService;
        _episodeMetadata = episodeMetadata;
    }

    public async Task<bool> ReviewManualSourceAsync(
        IEpisodeReviewItem item,
        Action<string, int> reportStatus,
        int currentProgress,
        string reviewStatusText,
        string cancelledStatusText,
        string openFailedStatusText,
        string approvedStatusText,
        string alternativeStatusText,
        Func<IReadOnlyCollection<string>, Task<bool>> tryAlternativeAsync)
    {
        while (item.RequiresManualCheck && !string.IsNullOrWhiteSpace(item.CurrentReviewTargetPath))
        {
            var reviewTargetPath = item.CurrentReviewTargetPath!;
            reportStatus(reviewStatusText, currentProgress);
            if (!_dialogService.TryOpenFilesWithDefaultApp([reviewTargetPath]))
            {
                reportStatus(openFailedStatusText, currentProgress);
                return false;
            }

            var result = _dialogService.AskSourceReviewResult(
                Path.GetFileName(reviewTargetPath),
                canTryAlternative: true);

            if (result == MessageBoxResult.Cancel)
            {
                reportStatus(cancelledStatusText, currentProgress);
                return false;
            }

            if (result == MessageBoxResult.Yes)
            {
                item.ApproveCurrentReviewTarget();
                if (!item.RequiresManualCheck || item.IsManualCheckApproved)
                {
                    reportStatus(approvedStatusText, 100);
                    return true;
                }

                continue;
            }

            var tentativeExclusions = new HashSet<string>(item.ExcludedSourcePaths, StringComparer.OrdinalIgnoreCase)
            {
                reviewTargetPath
            };

            var updated = await tryAlternativeAsync(tentativeExclusions);
            if (!updated)
            {
                return false;
            }

            if (!item.RequiresManualCheck)
            {
                reportStatus(alternativeStatusText, 100);
                return true;
            }
        }

        return !item.RequiresManualCheck || item.IsManualCheckApproved;
    }

    public Task<EpisodeMetadataReviewOutcome> ReviewMetadataAsync(
        IEpisodeReviewItem item,
        Action<string, int> reportStatus,
        int currentProgress,
        string reviewStatusText,
        string cancelledStatusText,
        string localApprovedStatusText,
        string tvdbApprovedStatusText,
        Action onEpisodeChanged)
    {
        reportStatus(reviewStatusText, currentProgress);

        var guess = new EpisodeMetadataGuess(
            item.SeriesName,
            item.Title,
            item.SeasonNumber,
            item.EpisodeNumber);

        var dialog = new TvdbLookupWindow(_episodeMetadata, guess)
        {
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
                ?? Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true)
        {
            reportStatus(cancelledStatusText, currentProgress);
            return Task.FromResult(EpisodeMetadataReviewOutcome.Cancelled);
        }

        if (dialog.KeepLocalDetection)
        {
            item.ApplyLocalMetadataGuess();
            onEpisodeChanged();
            item.ApproveMetadataReview("Lokale Erkennung wurde bewusst beibehalten.");
            reportStatus(localApprovedStatusText, 100);
            return Task.FromResult(EpisodeMetadataReviewOutcome.KeptLocalDetection);
        }

        if (dialog.SelectedEpisodeSelection is null)
        {
            reportStatus(cancelledStatusText, currentProgress);
            return Task.FromResult(EpisodeMetadataReviewOutcome.Cancelled);
        }

        item.ApplyTvdbSelection(dialog.SelectedEpisodeSelection);
        onEpisodeChanged();
        item.ApproveMetadataReview(
            $"TVDB manuell bestätigt: S{dialog.SelectedEpisodeSelection.SeasonNumber}E{dialog.SelectedEpisodeSelection.EpisodeNumber} - {dialog.SelectedEpisodeSelection.EpisodeTitle}");
        reportStatus(tvdbApprovedStatusText, 100);
        return Task.FromResult(EpisodeMetadataReviewOutcome.AppliedTvdbSelection);
    }
}

/// <summary>
/// Ergebnis des manuellen TVDB-Dialogs.
/// </summary>
public enum EpisodeMetadataReviewOutcome
{
    Cancelled,
    KeptLocalDetection,
    AppliedTvdbSelection
}
