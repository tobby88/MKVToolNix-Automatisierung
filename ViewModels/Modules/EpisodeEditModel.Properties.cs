using System.ComponentModel;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

// Dieser Partial enthält die bindbaren Eigenschaften und ihre abgeleiteten Anzeigeinformationen.
public partial class EpisodeEditModel
{

    public string ReviewTitle => Title;

    public string LocalSeriesName => _localSeriesName;

    public string LocalSeasonNumber => _localSeasonNumber;

    public string LocalEpisodeNumber => _localEpisodeNumber;

    public string LocalTitle => _localTitle;

    public virtual string SeriesName
    {
        get => _seriesName;
        set
        {
            var normalized = value.Trim();
            if (_seriesName == normalized)
            {
                return;
            }

            _seriesName = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MetadataDisplayText));
        }
    }

    public virtual string SeasonNumber
    {
        get => _seasonNumber;
        set
        {
            var normalized = EpisodeMetadataMergeHelper.NormalizeEpisodeNumber(value);
            if (_seasonNumber == normalized)
            {
                return;
            }

            _seasonNumber = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EpisodeCodeDisplayText));
            OnPropertyChanged(nameof(MetadataDisplayText));
        }
    }

    public virtual string EpisodeNumber
    {
        get => _episodeNumber;
        set
        {
            var normalized = EpisodeMetadataMergeHelper.NormalizeEpisodeNumber(value);
            if (_episodeNumber == normalized)
            {
                return;
            }

            _episodeNumber = normalized;
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
        protected set
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

    public string AudioDescriptionPath
    {
        get => _audioDescriptionPath;
        protected set
        {
            var normalized = value ?? string.Empty;
            if (_audioDescriptionPath == normalized)
            {
                return;
            }

            _audioDescriptionPath = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AudioDescriptionDisplayText));
            OnPropertyChanged(nameof(VideoAndAudioDescriptionDisplayText));
            OnPropertyChanged(nameof(SourceFilePaths));
        }
    }

    public IReadOnlyList<string> SubtitlePaths => _subtitlePaths;

    public IReadOnlyList<string> AttachmentPaths => _attachmentPaths;

    public IReadOnlyList<string> ManualAttachmentPaths => _hasManualAttachmentOverride
        ? _attachmentPaths
        : [];

    public IReadOnlyList<string> RelatedEpisodeFilePaths => _relatedEpisodeFilePaths;

    public string OutputPath
    {
        get => _outputPath;
        protected set
        {
            if (_outputPath == value)
            {
                return;
            }

            _outputPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutputFileName));
            RefreshArchiveState();
            OnPropertyChanged(nameof(UsesAutomaticOutputPath));
        }
    }

    public string OutputFileName => Path.GetFileName(OutputPath);

    public string EpisodeCodeDisplayText => $"S{SeasonNumber}E{EpisodeNumber}";

    public virtual string Title
    {
        get => _title;
        set
        {
            var normalized = value.Trim();
            if (_title == normalized)
            {
                return;
            }

            _title = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TitleForMux));
            OnPropertyChanged(nameof(MetadataDisplayText));
            OnPropertyChanged(nameof(ReviewTitle));
        }
    }

    public virtual string TitleForMux
    {
        get => Title;
        set => Title = value;
    }

    public string MetadataStatusText
    {
        get => _metadataStatusText;
        protected set
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
        protected set
        {
            if (_planSummaryText == value)
            {
                return;
            }

            _planSummaryText = value;
            OnPropertyChanged();
        }
    }

    public EpisodeUsageSummary? UsageSummary
    {
        get => _usageSummary;
        protected set
        {
            if (_usageSummary == value)
            {
                return;
            }

            _usageSummary = value;
            OnPropertyChanged();
        }
    }

    public EpisodeArchiveState ArchiveState => _archiveState;

    public string ArchiveStateText => _archiveState == EpisodeArchiveState.Existing ? "vorhanden" : "neu";

    public int ArchiveSortKey => _archiveState == EpisodeArchiveState.New ? 0 : 1;

    public string ArchiveBadgeBackground => EpisodeUiStyleBuilder.BuildArchiveBadgeBackground(ArchiveState);

    public string ArchiveBadgeBorderBrush => EpisodeUiStyleBuilder.BuildArchiveBadgeBorderBrush(ArchiveState);

    public string ArchiveStateTooltip => EpisodeEditTextBuilder.BuildArchiveStateTooltip(ArchiveState);

    public bool HasPendingManualCheck => RequiresManualCheck && !IsManualCheckApproved;

    public bool HasPendingMetadataReview => RequiresMetadataReview && !IsMetadataReviewApproved;

    public bool RequiresMetadataReview
    {
        get => _requiresMetadataReview;
        protected set
        {
            if (_requiresMetadataReview == value)
            {
                return;
            }

            _requiresMetadataReview = value;
            OnPropertyChanged();
            NotifyMetadataReviewStatePropertiesChanged();
        }
    }

    public bool IsMetadataReviewApproved
    {
        get => _isMetadataReviewApproved;
        protected set
        {
            if (_isMetadataReviewApproved == value)
            {
                return;
            }

            _isMetadataReviewApproved = value;
            OnPropertyChanged();
            NotifyMetadataReviewStatePropertiesChanged();
        }
    }

    public bool RequiresManualCheck
    {
        get => _requiresManualCheck;
        protected set
        {
            if (_requiresManualCheck == value)
            {
                return;
            }

            _requiresManualCheck = value;
            OnPropertyChanged();
            NotifyManualCheckStatePropertiesChanged(includeCurrentReviewTarget: false);
        }
    }

    public IReadOnlyList<string> ManualCheckFilePaths => _manualCheckFilePaths;

    public string ManualCheckText => EpisodeEditTextBuilder.BuildManualCheckText(RequiresManualCheck, IsManualCheckApproved);

    public string? CurrentReviewTargetPath => _manualCheckFilePaths.FirstOrDefault(path => !_approvedReviewPaths.Contains(path));

    public bool IsManualCheckApproved => !RequiresManualCheck
        || string.IsNullOrWhiteSpace(CurrentReviewTargetPath);

    public bool HasPendingChecks => HasPendingManualCheck || HasPendingMetadataReview;

    public EpisodeReviewState ReviewState => EpisodeEditTextBuilder.GetReviewState(
        RequiresManualCheck,
        IsManualCheckApproved,
        RequiresMetadataReview,
        IsMetadataReviewApproved);

    public bool UsesAutomaticOutputPath => !_outputPathWasManuallyChanged && !string.IsNullOrWhiteSpace(OutputPath);

    public string ReviewHint
    {
        get
        {
            return EpisodeEditTextBuilder.BuildReviewHint(ReviewState);
        }
    }

    public string ReviewHintTooltip => EpisodeEditTextBuilder.BuildReviewHintTooltip(ReviewState);

    public string ReviewBadgeBackground => EpisodeUiStyleBuilder.BuildReviewBadgeBackground(ReviewState);

    public string ReviewBadgeBorderBrush => EpisodeUiStyleBuilder.BuildReviewBadgeBorderBrush(ReviewState);

    public IReadOnlyList<string> Notes => _notes;

    public string DetectionSeedPath => _detectionSeedPath;

    public IReadOnlyCollection<string> ExcludedSourcePaths => _excludedSourcePaths;

    public IReadOnlyList<string> RequestedSourcePaths => _requestedSourcePaths;

    public string RequestedSourcesDisplayText => EpisodeEditTextBuilder.FormatPaths(_requestedSourcePaths);

    public string MainVideoDisplayText => MainVideoPath;

    public string MetadataDisplayText => $"{SeriesName} - {EpisodeCodeDisplayText} - {Title}";

    public string AdditionalVideosDisplayText => EpisodeEditTextBuilder.FormatPaths(_additionalVideoPaths);

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
            lines.Add(string.IsNullOrWhiteSpace(AudioDescriptionPath) ? "(keine)" : AudioDescriptionPath);

            return string.Join(Environment.NewLine, lines);
        }
    }

    public virtual string SubtitleDisplayText => EpisodeEditTextBuilder.FormatPaths(_subtitlePaths);

    public virtual string AttachmentDisplayText => EpisodeEditTextBuilder.FormatPaths(_attachmentPaths);

    public string NotesDisplayText => EpisodeEditTextBuilder.BuildNotesDisplayText(_notes);

    public IReadOnlyList<string> SourceFilePaths => EnumerateSourceFilePaths().ToList();

    string IEpisodePlanInput.MainVideoPath => MainVideoPath;

    string? IEpisodePlanInput.AudioDescriptionPath => string.IsNullOrWhiteSpace(AudioDescriptionPath) ? null : AudioDescriptionPath;

    IReadOnlyList<string> IEpisodePlanInput.SubtitlePaths => SubtitlePaths;

    IReadOnlyList<string> IEpisodePlanInput.AttachmentPaths => AttachmentPaths;

    IReadOnlyList<string> IEpisodePlanInput.ManualAttachmentPaths => ManualAttachmentPaths;

    string IEpisodePlanInput.OutputPath => OutputPath;

    string IEpisodePlanInput.TitleForMux => TitleForMux;

    IReadOnlyCollection<string> IEpisodePlanInput.ExcludedSourcePaths => ExcludedSourcePaths;

}
