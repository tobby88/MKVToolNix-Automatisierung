using System.Windows;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.Windows;

namespace MkvToolnixAutomatisierung.Services;

public interface IEpisodeReviewItem
{
    string ReviewTitle { get; }
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

public sealed class EpisodeReviewWorkflow
{
    private readonly UserDialogService _dialogService;
    private readonly EpisodeMetadataLookupService _episodeMetadata;

    public EpisodeReviewWorkflow(UserDialogService dialogService, EpisodeMetadataLookupService episodeMetadata)
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
        string approvedStatusText,
        string alternativeStatusText,
        Func<IReadOnlyCollection<string>, Task<bool>> tryAlternativeAsync)
    {
        while (item.RequiresManualCheck && !string.IsNullOrWhiteSpace(item.CurrentReviewTargetPath))
        {
            var reviewTargetPath = item.CurrentReviewTargetPath!;
            reportStatus(reviewStatusText, currentProgress);
            _dialogService.OpenFilesWithDefaultApp([reviewTargetPath]);

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
                reportStatus(approvedStatusText, 100);
                return true;
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
            item.LocalSeriesName,
            item.LocalTitle,
            item.LocalSeasonNumber,
            item.LocalEpisodeNumber);

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

public enum EpisodeMetadataReviewOutcome
{
    Cancelled,
    KeptLocalDetection,
    AppliedTvdbSelection
}
