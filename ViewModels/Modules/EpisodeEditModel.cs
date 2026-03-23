using System.ComponentModel;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

public class EpisodeEditModel : INotifyPropertyChanged, IEpisodePlanInput, IEpisodeReviewItem
{
    private string _localSeriesName = string.Empty;
    private string _localSeasonNumber = "xx";
    private string _localEpisodeNumber = "xx";
    private string _localTitle = string.Empty;
    private string _seriesName = string.Empty;
    private string _seasonNumber = "xx";
    private string _episodeNumber = "xx";
    private string _requestedMainVideoPath = string.Empty;
    private string _mainVideoPath = string.Empty;
    private List<string> _requestedSourcePaths = [];
    private List<string> _additionalVideoPaths = [];
    private string _audioDescriptionPath = string.Empty;
    private List<string> _subtitlePaths = [];
    private List<string> _attachmentPaths = [];
    private List<string> _relatedEpisodeFilePaths = [];
    private string _outputPath = string.Empty;
    private string _title = string.Empty;
    private string _metadataStatusText = string.Empty;
    private string _planSummaryText = string.Empty;
    private EpisodeUsageSummary? _usageSummary;
    private bool _requiresMetadataReview;
    private bool _isMetadataReviewApproved = true;
    private bool _outputPathWasManuallyChanged;
    private bool _requiresManualCheck;
    private List<string> _manualCheckFilePaths = [];
    private List<string> _notes = [];
    private string _detectionSeedPath = string.Empty;
    private readonly HashSet<string> _excludedSourcePaths = new(StringComparer.OrdinalIgnoreCase);
    private string? _approvedReviewPath;

    protected EpisodeEditModel()
    {
    }

    protected EpisodeEditModel(
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
        string title,
        string metadataStatusText,
        bool requiresMetadataReview,
        bool isMetadataReviewApproved,
        string planSummaryText,
        EpisodeUsageSummary? usageSummary,
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
        _audioDescriptionPath = audioDescriptionPath ?? string.Empty;
        _subtitlePaths = subtitlePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        _attachmentPaths = attachmentPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        _relatedEpisodeFilePaths = relatedEpisodeFilePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        _outputPath = outputPath;
        _title = title;
        _metadataStatusText = metadataStatusText;
        _planSummaryText = planSummaryText;
        _usageSummary = usageSummary;
        _requiresMetadataReview = requiresMetadataReview;
        _isMetadataReviewApproved = isMetadataReviewApproved;
        _requiresManualCheck = requiresManualCheck;
        _manualCheckFilePaths = manualCheckFilePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _notes = notes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _detectionSeedPath = requestedMainVideoPath;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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
            OnPropertyChanged(nameof(ArchiveStateText));
            OnPropertyChanged(nameof(ArchiveSortKey));
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

    public string ArchiveStateText => File.Exists(OutputPath) ? "vorhanden" : "neu";

    public int ArchiveSortKey => ArchiveStateText == "neu" ? 0 : 1;

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
            OnPropertyChanged(nameof(ReviewHint));
            OnPropertyChanged(nameof(HasPendingChecks));
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
            OnPropertyChanged(nameof(ReviewHint));
            OnPropertyChanged(nameof(HasPendingChecks));
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
            OnPropertyChanged(nameof(ManualCheckText));
            OnPropertyChanged(nameof(ReviewHint));
            OnPropertyChanged(nameof(HasPendingChecks));
        }
    }

    public IReadOnlyList<string> ManualCheckFilePaths => _manualCheckFilePaths;

    public string ManualCheckText => !RequiresManualCheck
        ? string.Empty
        : IsManualCheckApproved
            ? "Die aktuell ausgewählte Quelle wurde bereits geprüft und freigegeben."
            : "Die aktuell ausgewählte Quelle ist prüfpflichtig. Bitte vor dem Muxen kurz prüfen und freigeben.";

    public string? CurrentReviewTargetPath => _manualCheckFilePaths.FirstOrDefault();

    public bool IsManualCheckApproved => !RequiresManualCheck
        || string.Equals(_approvedReviewPath, CurrentReviewTargetPath, StringComparison.OrdinalIgnoreCase);

    public bool HasPendingChecks => (RequiresManualCheck && !IsManualCheckApproved)
        || (RequiresMetadataReview && !IsMetadataReviewApproved);

    public bool UsesAutomaticOutputPath => !_outputPathWasManuallyChanged && !string.IsNullOrWhiteSpace(OutputPath);

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
                return string.Join(" + ", pendingChecks) + " prüfen";
            }

            if (RequiresManualCheck || RequiresMetadataReview)
            {
                return "Freigegeben";
            }

            return "Keine nötig";
        }
    }

    public IReadOnlyList<string> Notes => _notes;

    public string DetectionSeedPath => _detectionSeedPath;

    public IReadOnlyCollection<string> ExcludedSourcePaths => _excludedSourcePaths;

    public string RequestedSourcesDisplayText => FormatPaths(_requestedSourcePaths);

    public string MainVideoDisplayText => MainVideoPath;

    public string MetadataDisplayText => $"{SeriesName} - {EpisodeCodeDisplayText} - {Title}";

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
            lines.Add(string.IsNullOrWhiteSpace(AudioDescriptionPath) ? "(keine)" : AudioDescriptionPath);

            return string.Join(Environment.NewLine, lines);
        }
    }

    public string SubtitleDisplayText => FormatPaths(_subtitlePaths);

    public string AttachmentDisplayText => FormatPaths(_attachmentPaths);

    public string NotesDisplayText => _notes.Count == 0
        ? string.Empty
        : string.Join(Environment.NewLine, _notes.Select(note => "- " + note));

    public IReadOnlyList<string> SourceFilePaths => EnumerateSourceFilePaths().ToList();

    string IEpisodePlanInput.MainVideoPath => MainVideoPath;

    string? IEpisodePlanInput.AudioDescriptionPath => string.IsNullOrWhiteSpace(AudioDescriptionPath) ? null : AudioDescriptionPath;

    IReadOnlyList<string> IEpisodePlanInput.SubtitlePaths => SubtitlePaths;

    IReadOnlyList<string> IEpisodePlanInput.AttachmentPaths => AttachmentPaths;

    string IEpisodePlanInput.OutputPath => OutputPath;

    string IEpisodePlanInput.TitleForMux => TitleForMux;

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
        OnPropertyChanged(nameof(ReviewHint));
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
        OnPropertyChanged(nameof(ReviewHint));
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
        OnPropertyChanged(nameof(ReviewHint));
        OnPropertyChanged(nameof(HasPendingChecks));
    }

    protected void SetRequestedSourcePaths(IEnumerable<string> paths)
    {
        _requestedSourcePaths = paths.ToList();
        OnPropertyChanged(nameof(RequestedSourcesDisplayText));
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
}
