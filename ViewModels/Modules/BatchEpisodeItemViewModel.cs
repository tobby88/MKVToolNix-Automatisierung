using System.ComponentModel;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

internal sealed record BatchScanResult(
    int Index,
    string SourcePath,
    AutoDetectedEpisodeFiles? Detected,
    EpisodeMetadataGuess? LocalGuess,
    EpisodeMetadataResolutionResult? MetadataResolution,
    string? OutputPath,
    string? ErrorMessage);

public sealed class BatchEpisodeItemViewModel : INotifyPropertyChanged, IEpisodePlanInput, IEpisodeReviewItem
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
    private EpisodeUsageSummary? _usageSummary;
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
        EpisodeUsageSummary? usageSummary,
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
        _usageSummary = usageSummary;
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

    public string ReviewTitle => Title;

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

    public EpisodeUsageSummary? UsageSummary
    {
        get => _usageSummary;
        private set
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
            OnPropertyChanged(nameof(StatusSortKey));
        }
    }

    public int StatusSortKey => Status switch
    {
        var value when value.StartsWith("Fehler", StringComparison.OrdinalIgnoreCase) => 0,
        "Warnung" => 1,
        "Läuft" => 2,
        "Vergleich offen" => 3,
        "Bereit" => 4,
        "Ziel aktuell" => 5,
        "Erfolgreich" => 6,
        _ => 99
    };

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
                ? "In der Serienbibliothek bereits vorhanden. Details wählen für den genauen Vergleich."
                : "Noch nicht in der Serienbibliothek vorhanden. Neue MKV wird erstellt.",
            EpisodeUsageSummary.CreatePending(
                File.Exists(outputPath) ? "In Serienbibliothek vorhanden" : "Noch nicht in Serienbibliothek",
                File.Exists(outputPath) ? "Vergleich wird berechnet" : "Neue MKV wird erstellt"),
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
            "Keine Plan-Zusammenfassung verfügbar.",
            EpisodeUsageSummary.CreatePending("Fehler", "Keine Plan-Zusammenfassung verfügbar."),
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
            ? "In der Serienbibliothek bereits vorhanden. Details wählen für den genauen Vergleich."
            : "Noch nicht in der Serienbibliothek vorhanden. Neue MKV wird erstellt.";
        UsageSummary = EpisodeUsageSummary.CreatePending(
            File.Exists(outputPath) ? "In Serienbibliothek vorhanden" : "Noch nicht in Serienbibliothek",
            File.Exists(outputPath) ? "Vergleich wird berechnet" : "Neue MKV wird erstellt");
        RequiresManualCheck = detected.RequiresManualCheck;
        _manualCheckFilePaths = detected.ManualCheckFilePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!RequiresManualCheck || !string.Equals(_approvedReviewPath, CurrentReviewTargetPath, StringComparison.OrdinalIgnoreCase))
        {
            _approvedReviewPath = null;
        }

        _notes = detected.Notes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        IsSelected = true;

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
        OnPropertyChanged(nameof(UsageSummary));
        OnPropertyChanged(nameof(MetadataDisplayText));
        OnPropertyChanged(nameof(ArchiveStateText));
        OnPropertyChanged(nameof(ArchiveSortKey));
        OnPropertyChanged(nameof(StatusSortKey));
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
        ApplyOutputPath(outputPath);
        IsSelected = true;
    }

    public void SetAutomaticOutputPath(string outputPath)
    {
        if (_outputPathWasManuallyChanged)
        {
            return;
        }

        ApplyOutputPath(outputPath);
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
        OnPropertyChanged(nameof(MetadataDisplayText));
    }

    public void ApplyLocalMetadataGuess()
    {
        SeriesName = _localSeriesName;
        SeasonNumber = _localSeasonNumber;
        EpisodeNumber = _localEpisodeNumber;
        TitleForMux = _localTitle;
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

    private void ApplyOutputPath(string outputPath)
    {
        OutputPath = outputPath;
        var outputExists = File.Exists(outputPath);
        Status = outputExists ? "Vergleich offen" : "Bereit";
        PlanSummaryText = outputExists
            ? "In der Serienbibliothek bereits vorhanden. Details wählen für den genauen Vergleich."
            : "Noch nicht in der Serienbibliothek vorhanden. Neue MKV wird erstellt.";
        UsageSummary = EpisodeUsageSummary.CreatePending(
            outputExists ? "In Serienbibliothek vorhanden" : "Noch nicht in Serienbibliothek",
            outputExists ? "Vergleich wird berechnet" : "Neue MKV wird erstellt");
        OnPropertyChanged(nameof(ArchiveStateText));
        OnPropertyChanged(nameof(ArchiveSortKey));
        OnPropertyChanged(nameof(StatusSortKey));
        IsSelected = true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
