using System.ComponentModel;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Beschreibt nur, ob das aktuelle Ausgabeziel schon existiert oder neu erzeugt würde.
/// </summary>
public enum EpisodeArchiveState
{
    New = 0,
    Existing = 1
}

/// <summary>
/// Gemeinsame Basis für Einzel- und Batch-Episoden mit allen editierbaren Metadaten und Prüfzuständen.
/// </summary>
public partial class EpisodeEditModel : INotifyPropertyChanged, IEpisodePlanInput, IEpisodeReviewItem
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
    private bool _hasManualAttachmentOverride;
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
    private EpisodeArchiveState _archiveState = EpisodeArchiveState.New;
    private readonly HashSet<string> _excludedSourcePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _approvedReviewPaths = new(StringComparer.OrdinalIgnoreCase);

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
        EpisodeArchiveState? initialArchiveState,
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
        _archiveState = initialArchiveState ?? ResolveArchiveState(outputPath);
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
}
