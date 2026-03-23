using System.ComponentModel;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

public partial class EpisodeEditModel
{
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

        OnPropertyChanged(nameof(RequestedSourcePaths));
        OnPropertyChanged(nameof(RequestedSourcesDisplayText));
    }

    protected void ApplyDetectedEpisodeState(
        string requestedMainVideoPath,
        EpisodeMetadataGuess localGuess,
        AutoDetectedEpisodeFiles detected,
        EpisodeMetadataResolutionResult metadataResolution,
        string outputPath,
        string? titleOverride = null)
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
        AudioDescriptionPath = detected.AudioDescriptionPath ?? string.Empty;
        _subtitlePaths = detected.SubtitlePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        _attachmentPaths = detected.AttachmentPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        _relatedEpisodeFilePaths = detected.RelatedFilePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        _outputPathWasManuallyChanged = false;
        OutputPath = outputPath;
        Title = string.IsNullOrWhiteSpace(titleOverride) ? detected.SuggestedTitle : titleOverride;
        MetadataStatusText = metadataResolution.StatusText;
        RequiresMetadataReview = metadataResolution.RequiresReview;
        IsMetadataReviewApproved = !metadataResolution.RequiresReview;
        RequiresManualCheck = detected.RequiresManualCheck;
        _manualCheckFilePaths = detected.ManualCheckFilePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!RequiresManualCheck || !string.Equals(_approvedReviewPath, CurrentReviewTargetPath, StringComparison.OrdinalIgnoreCase))
        {
            _approvedReviewPath = null;
        }

        _notes = detected.Notes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        OnPropertyChanged(nameof(RequestedMainVideoPath));
        OnPropertyChanged(nameof(RequestedSourcePaths));
        OnPropertyChanged(nameof(RequestedSourcesDisplayText));
        OnPropertyChanged(nameof(LocalSeriesName));
        OnPropertyChanged(nameof(LocalSeasonNumber));
        OnPropertyChanged(nameof(LocalEpisodeNumber));
        OnPropertyChanged(nameof(LocalTitle));
        OnPropertyChanged(nameof(AdditionalVideoPaths));
        OnPropertyChanged(nameof(AdditionalVideosDisplayText));
        OnPropertyChanged(nameof(SubtitlePaths));
        OnPropertyChanged(nameof(SubtitleDisplayText));
        OnPropertyChanged(nameof(AttachmentPaths));
        OnPropertyChanged(nameof(AttachmentDisplayText));
        OnPropertyChanged(nameof(MetadataStatusText));
        OnPropertyChanged(nameof(IsMetadataReviewApproved));
        OnPropertyChanged(nameof(ManualCheckFilePaths));
        OnPropertyChanged(nameof(IsManualCheckApproved));
        OnPropertyChanged(nameof(HasPendingMetadataReview));
        OnPropertyChanged(nameof(HasPendingManualCheck));
        OnPropertyChanged(nameof(ReviewState));
        OnPropertyChanged(nameof(ReviewHint));
        OnPropertyChanged(nameof(ReviewBadgeBackground));
        OnPropertyChanged(nameof(ReviewBadgeBorderBrush));
        OnPropertyChanged(nameof(HasPendingChecks));
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(NotesDisplayText));
        OnPropertyChanged(nameof(SourceFilePaths));
        OnPropertyChanged(nameof(ManualCheckText));
        OnPropertyChanged(nameof(UsesAutomaticOutputPath));
    }

    public virtual void SetAudioDescription(string? path)
    {
        AudioDescriptionPath = string.IsNullOrWhiteSpace(path) ? string.Empty : path;
        _approvedReviewPath = null;
        OnPropertyChanged(nameof(IsManualCheckApproved));
        OnPropertyChanged(nameof(ManualCheckText));
        OnPropertyChanged(nameof(HasPendingManualCheck));
        OnPropertyChanged(nameof(ReviewState));
        OnPropertyChanged(nameof(ReviewHint));
        OnPropertyChanged(nameof(ReviewBadgeBackground));
        OnPropertyChanged(nameof(ReviewBadgeBorderBrush));
        OnPropertyChanged(nameof(HasPendingChecks));
    }

    public virtual void SetSubtitles(IEnumerable<string> paths)
    {
        _subtitlePaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        OnPropertyChanged(nameof(SubtitlePaths));
        OnPropertyChanged(nameof(SubtitleDisplayText));
    }

    public virtual void SetAttachments(IEnumerable<string> paths)
    {
        _attachmentPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        OnPropertyChanged(nameof(AttachmentPaths));
        OnPropertyChanged(nameof(AttachmentDisplayText));
    }

    public virtual void SetOutputPath(string outputPath)
    {
        _outputPathWasManuallyChanged = true;
        OutputPath = outputPath;
    }

    public virtual void SetAutomaticOutputPath(string outputPath)
    {
        if (_outputPathWasManuallyChanged)
        {
            return;
        }

        OutputPath = outputPath;
    }

    public void SetPlanSummary(string summaryText)
    {
        PlanSummaryText = summaryText;
    }

    public void SetUsageSummary(EpisodeUsageSummary? usageSummary)
    {
        UsageSummary = usageSummary;
    }

    public void ReplaceExcludedSourcePaths(IEnumerable<string> excludedSourcePaths)
    {
        _excludedSourcePaths.Clear();
        foreach (var excludedSourcePath in excludedSourcePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            _excludedSourcePaths.Add(excludedSourcePath);
        }
    }

    public virtual void ApproveCurrentReviewTarget()
    {
        _approvedReviewPath = CurrentReviewTargetPath;
        OnPropertyChanged(nameof(IsManualCheckApproved));
        OnPropertyChanged(nameof(ManualCheckText));
        OnPropertyChanged(nameof(HasPendingManualCheck));
        OnPropertyChanged(nameof(ReviewState));
        OnPropertyChanged(nameof(ReviewHint));
        OnPropertyChanged(nameof(ReviewBadgeBackground));
        OnPropertyChanged(nameof(ReviewBadgeBorderBrush));
        OnPropertyChanged(nameof(HasPendingChecks));
    }

    public virtual void ApplyTvdbSelection(TvdbEpisodeSelection selection)
    {
        SeriesName = selection.TvdbSeriesName;
        SeasonNumber = selection.SeasonNumber;
        EpisodeNumber = selection.EpisodeNumber;
        Title = selection.EpisodeTitle;
    }

    public virtual void ApplyLocalMetadataGuess()
    {
        SeriesName = LocalSeriesName;
        SeasonNumber = LocalSeasonNumber;
        EpisodeNumber = LocalEpisodeNumber;
        Title = LocalTitle;
    }

    public virtual void ApproveMetadataReview(string statusText)
    {
        MetadataStatusText = statusText;
        RequiresMetadataReview = false;
        IsMetadataReviewApproved = true;
    }

    protected bool OutputPathWasManuallyChanged => _outputPathWasManuallyChanged;

    protected void MarkOutputPathAsAutomatic()
    {
        _outputPathWasManuallyChanged = false;
        OnPropertyChanged(nameof(UsesAutomaticOutputPath));
    }

    protected void SetMetadataResolutionState(EpisodeMetadataResolutionResult resolution)
    {
        MetadataStatusText = resolution.StatusText;
        RequiresMetadataReview = resolution.RequiresReview;
        IsMetadataReviewApproved = !resolution.RequiresReview;
    }

    protected void SetNotes(IEnumerable<string> notes)
    {
        _notes = notes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(NotesDisplayText));
    }

    protected void SetRequestedMainVideoPath(string path)
    {
        _requestedMainVideoPath = path;
        OnPropertyChanged(nameof(RequestedMainVideoPath));
    }

    protected void SetDetectionSeedPath(string path)
    {
        _detectionSeedPath = path;
    }

    protected void SetLocalMetadataGuess(EpisodeMetadataGuess guess)
    {
        _localSeriesName = guess.SeriesName;
        _localSeasonNumber = guess.SeasonNumber;
        _localEpisodeNumber = guess.EpisodeNumber;
        _localTitle = guess.EpisodeTitle;
        OnPropertyChanged(nameof(LocalSeriesName));
        OnPropertyChanged(nameof(LocalSeasonNumber));
        OnPropertyChanged(nameof(LocalEpisodeNumber));
        OnPropertyChanged(nameof(LocalTitle));
    }

    protected void SetRelatedEpisodeFilePaths(IEnumerable<string> paths)
    {
        _relatedEpisodeFilePaths = paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        OnPropertyChanged(nameof(RelatedEpisodeFilePaths));
    }

    protected void SetAdditionalVideoPaths(IEnumerable<string> paths)
    {
        _additionalVideoPaths = paths.ToList();
        OnPropertyChanged(nameof(AdditionalVideoPaths));
        OnPropertyChanged(nameof(AdditionalVideosDisplayText));
        OnPropertyChanged(nameof(VideoAndAudioDescriptionDisplayText));
        OnPropertyChanged(nameof(SourceFilePaths));
    }

    protected void SetManualCheckFiles(bool requiresManualCheck, IEnumerable<string> filePaths)
    {
        RequiresManualCheck = requiresManualCheck;
        _manualCheckFilePaths = filePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!RequiresManualCheck || !string.Equals(_approvedReviewPath, CurrentReviewTargetPath, StringComparison.OrdinalIgnoreCase))
        {
            _approvedReviewPath = null;
        }

        OnPropertyChanged(nameof(ManualCheckFilePaths));
        OnPropertyChanged(nameof(IsManualCheckApproved));
        OnPropertyChanged(nameof(ManualCheckText));
        OnPropertyChanged(nameof(HasPendingManualCheck));
        OnPropertyChanged(nameof(ReviewState));
        OnPropertyChanged(nameof(ReviewHint));
        OnPropertyChanged(nameof(ReviewBadgeBackground));
        OnPropertyChanged(nameof(ReviewBadgeBorderBrush));
        OnPropertyChanged(nameof(HasPendingChecks));
    }

    protected void SetRequestedSourcePaths(IEnumerable<string> paths)
    {
        _requestedSourcePaths = paths.ToList();
        OnPropertyChanged(nameof(RequestedSourcePaths));
        OnPropertyChanged(nameof(RequestedSourcesDisplayText));
    }

    protected void RefreshArchiveState()
    {
        SetArchiveState(ResolveArchiveState(OutputPath));
    }

    protected void SetArchiveState(EpisodeArchiveState archiveState)
    {
        if (_archiveState == archiveState)
        {
            return;
        }

        _archiveState = archiveState;
        OnPropertyChanged(nameof(ArchiveState));
        OnPropertyChanged(nameof(ArchiveStateText));
        OnPropertyChanged(nameof(ArchiveBadgeBackground));
        OnPropertyChanged(nameof(ArchiveBadgeBorderBrush));
        OnPropertyChanged(nameof(ArchiveSortKey));
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static EpisodeArchiveState ResolveArchiveState(string outputPath)
    {
        return !string.IsNullOrWhiteSpace(outputPath) && File.Exists(outputPath)
            ? EpisodeArchiveState.Existing
            : EpisodeArchiveState.New;
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
            yield return AudioDescriptionPath;
        }
    }


}
