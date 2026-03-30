using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Rohes Scan-Ergebnis, bevor daraus eine bindbare Batch-Zeile gebaut wird.
/// </summary>
internal sealed record BatchScanResult(
    int Index,
    string SourcePath,
    AutoDetectedEpisodeFiles? Detected,
    EpisodeMetadataGuess? LocalGuess,
    EpisodeMetadataResolutionResult? MetadataResolution,
    string? OutputPath,
    string? ErrorMessage);

/// <summary>
/// Repräsentiert eine Zeile im Batch-Bildschirm inklusive Auswahl-, Status- und Review-Zustand.
/// </summary>
public sealed class BatchEpisodeItemViewModel : EpisodeEditModel
{
    private bool _isSelected;
    private string? _statusTextOverride;
    private BatchEpisodeStatusKind _statusKind;

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
        string title,
        string metadataStatusText,
        bool requiresMetadataReview,
        bool isMetadataReviewApproved,
        BatchEpisodeStatusKind statusKind,
        string planSummaryText,
        EpisodeUsageSummary? usageSummary,
        EpisodeArchiveState archiveState,
        bool isSelected,
        bool requiresManualCheck,
        IReadOnlyList<string> manualCheckFilePaths,
        IReadOnlyList<string> notes)
        : base(
            requestedMainVideoPath,
            mainVideoPath,
            localSeriesName,
            localSeasonNumber,
            localEpisodeNumber,
            localTitle,
            seriesName,
            seasonNumber,
            episodeNumber,
            additionalVideoPaths,
            audioDescriptionPath,
            subtitlePaths,
            attachmentPaths,
            relatedEpisodeFilePaths,
            outputPath,
            title,
            metadataStatusText,
            requiresMetadataReview,
            isMetadataReviewApproved,
            planSummaryText,
            usageSummary,
            archiveState,
            requiresManualCheck,
            manualCheckFilePaths,
            notes)
    {
        _statusKind = statusKind;
        _isSelected = isSelected;
    }

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

    public string Status => string.IsNullOrWhiteSpace(_statusTextOverride)
        ? EpisodeEditTextBuilder.BuildBatchStatusText(StatusKind)
        : _statusTextOverride;

    public BatchEpisodeStatusKind StatusKind
    {
        get => _statusKind;
        private set
        {
            if (_statusKind == value)
            {
                return;
            }

            _statusKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusSortKey));
            OnPropertyChanged(nameof(HasErrorStatus));
            OnPropertyChanged(nameof(StatusBadgeBackground));
            OnPropertyChanged(nameof(StatusBadgeBorderBrush));
            OnPropertyChanged(nameof(StatusTooltip));
        }
    }

    public bool HasErrorStatus => StatusKind == BatchEpisodeStatusKind.Error;

    public int StatusSortKey => (int)StatusKind;

    public string StatusBadgeBackground => EpisodeUiStyleBuilder.BuildBatchStatusBadgeBackground(StatusKind);

    public string StatusBadgeBorderBrush => EpisodeUiStyleBuilder.BuildBatchStatusBadgeBorderBrush(StatusKind);

    public string StatusTooltip => EpisodeEditTextBuilder.BuildBatchStatusTooltip(StatusKind, Status);

    public void SetStatus(BatchEpisodeStatusKind statusKind, string? statusText = null)
    {
        var previousStatus = Status;
        StatusKind = statusKind;
        var normalizedOverride = string.IsNullOrWhiteSpace(statusText) ? null : statusText;
        if (_statusTextOverride != normalizedOverride)
        {
            _statusTextOverride = normalizedOverride;
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusTooltip));
            return;
        }

        if (!string.Equals(previousStatus, Status, StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusTooltip));
        }
    }

    public static BatchEpisodeItemViewModel CreateFromDetection(
        string requestedMainVideoPath,
        EpisodeMetadataGuess localGuess,
        AutoDetectedEpisodeFiles detected,
        EpisodeMetadataResolutionResult metadataResolution,
        string outputPath,
        BatchEpisodeStatusKind statusKind,
        bool isSelected)
    {
        var outputExists = File.Exists(outputPath);
        var archiveState = outputExists ? EpisodeArchiveState.Existing : EpisodeArchiveState.New;
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
            statusKind,
            outputExists
                ? "In der Serienbibliothek bereits vorhanden. Details wählen für den genauen Vergleich."
                : "Noch nicht in der Serienbibliothek vorhanden. Neue MKV wird erstellt.",
            EpisodeUsageSummary.CreatePending(
                outputExists ? "In Serienbibliothek vorhanden" : "Noch nicht in Serienbibliothek",
                outputExists ? "Vergleich wird berechnet" : "Neue MKV wird erstellt"),
            archiveState,
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
            BatchEpisodeStatusKind.Error,
            "Keine Plan-Zusammenfassung verfügbar.",
            EpisodeUsageSummary.CreatePending("Fehler", "Keine Plan-Zusammenfassung verfügbar."),
            EpisodeArchiveState.New,
            isSelected: false,
            requiresManualCheck: false,
            manualCheckFilePaths: [],
            notes: [errorMessage]);
    }

    public void ApplyDetection(
        string requestedMainVideoPath,
        EpisodeMetadataGuess localGuess,
        AutoDetectedEpisodeFiles detected,
        EpisodeMetadataResolutionResult metadataResolution,
        string outputPath,
        BatchEpisodeStatusKind statusKind)
    {
        ApplyDetectedEpisodeState(
            requestedMainVideoPath,
            localGuess,
            detected,
            metadataResolution,
            outputPath);
        ApplyArchiveState(statusKind, refreshArchiveState: false);
        IsSelected = true;
    }

    public override void SetAudioDescription(string? path)
    {
        base.SetAudioDescription(path);
        IsSelected = true;
    }

    public override void SetSubtitles(IEnumerable<string> paths)
    {
        base.SetSubtitles(paths);
        IsSelected = true;
    }

    public override void SetAttachments(IEnumerable<string> paths)
    {
        base.SetAttachments(paths);
        IsSelected = true;
    }

    public override void SetOutputPath(string outputPath)
    {
        base.SetOutputPath(outputPath);
        ApplyArchiveState(refreshArchiveState: false);
        IsSelected = true;
    }

    public override void SetAutomaticOutputPath(string outputPath)
    {
        var previousOutputPath = OutputPath;
        base.SetAutomaticOutputPath(outputPath);
        if (!string.Equals(previousOutputPath, OutputPath, StringComparison.OrdinalIgnoreCase))
        {
            ApplyArchiveState(refreshArchiveState: false);
        }
    }

    public override void ApproveCurrentReviewTarget()
    {
        base.ApproveCurrentReviewTarget();
        IsSelected = true;
    }

    public override void ApplyTvdbSelection(TvdbEpisodeSelection selection)
    {
        base.ApplyTvdbSelection(selection);
        IsSelected = true;
    }

    public override void ApplyLocalMetadataGuess()
    {
        base.ApplyLocalMetadataGuess();
        IsSelected = true;
    }

    public override void ApproveMetadataReview(string statusText)
    {
        base.ApproveMetadataReview(statusText);
        IsSelected = true;
    }

    public void RefreshArchivePresence(BatchEpisodeStatusKind? statusOverride = null)
    {
        ApplyArchiveState(statusOverride, preservePlanSummary: true);
    }

    private void ApplyArchiveState(
        BatchEpisodeStatusKind? statusOverride = null,
        bool preservePlanSummary = false,
        bool refreshArchiveState = true)
    {
        if (refreshArchiveState)
        {
            RefreshArchiveState();
        }

        var outputExists = ArchiveState == EpisodeArchiveState.Existing;
        SetStatus(statusOverride ?? (outputExists ? BatchEpisodeStatusKind.ComparisonPending : BatchEpisodeStatusKind.Ready));
        if (!preservePlanSummary)
        {
            SetPlanSummary(outputExists
                ? "In der Serienbibliothek bereits vorhanden. Details wählen für den genauen Vergleich."
                : "Noch nicht in der Serienbibliothek vorhanden. Neue MKV wird erstellt.");
            SetUsageSummary(EpisodeUsageSummary.CreatePending(
                outputExists ? "In Serienbibliothek vorhanden" : "Noch nicht in Serienbibliothek",
                outputExists ? "Vergleich wird berechnet" : "Neue MKV wird erstellt"));
        }
    }

}
