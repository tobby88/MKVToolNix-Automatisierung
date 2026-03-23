using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
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

public sealed class BatchEpisodeItemViewModel : EpisodeEditModel
{
    private bool _isSelected;
    private string _status;

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
        string status,
        string planSummaryText,
        EpisodeUsageSummary? usageSummary,
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
            requiresManualCheck,
            manualCheckFilePaths,
            notes)
    {
        _status = status;
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
        "L�uft" => 2,
        "Vergleich offen" => 3,
        "Bereit" => 4,
        "Ziel aktuell" => 5,
        "Erfolgreich" => 6,
        _ => 99
    };

    public static BatchEpisodeItemViewModel CreateFromDetection(
        string requestedMainVideoPath,
        EpisodeMetadataGuess localGuess,
        AutoDetectedEpisodeFiles detected,
        EpisodeMetadataResolutionResult metadataResolution,
        string outputPath,
        string status,
        bool isSelected)
    {
        var outputExists = File.Exists(outputPath);
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
            outputExists
                ? "In der Serienbibliothek bereits vorhanden. Details wählen für den genauen Vergleich."
                : "Noch nicht in der Serienbibliothek vorhanden. Neue MKV wird erstellt.",
            EpisodeUsageSummary.CreatePending(
                outputExists ? "In Serienbibliothek vorhanden" : "Noch nicht in Serienbibliothek",
                outputExists ? "Vergleich wird berechnet" : "Neue MKV wird erstellt"),
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

    public void ApplyDetection(
        string requestedMainVideoPath,
        EpisodeMetadataGuess localGuess,
        AutoDetectedEpisodeFiles detected,
        EpisodeMetadataResolutionResult metadataResolution,
        string outputPath,
        string status)
    {
        ApplyDetectedEpisodeState(
            requestedMainVideoPath,
            localGuess,
            detected,
            metadataResolution,
            outputPath);
        ApplyArchiveState(outputPath, status);
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
        ApplyArchiveState(OutputPath);
        IsSelected = true;
    }

    public override void SetAutomaticOutputPath(string outputPath)
    {
        var previousOutputPath = OutputPath;
        base.SetAutomaticOutputPath(outputPath);
        if (!string.Equals(previousOutputPath, OutputPath, StringComparison.OrdinalIgnoreCase))
        {
            ApplyArchiveState(OutputPath);
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

    private void ApplyArchiveState(string outputPath, string? statusOverride = null)
    {
        var outputExists = !string.IsNullOrWhiteSpace(outputPath) && File.Exists(outputPath);
        Status = statusOverride ?? (outputExists ? "Vergleich offen" : "Bereit");
        SetPlanSummary(outputExists
            ? "In der Serienbibliothek bereits vorhanden. Details wählen für den genauen Vergleich."
            : "Noch nicht in der Serienbibliothek vorhanden. Neue MKV wird erstellt.");
        SetUsageSummary(EpisodeUsageSummary.CreatePending(
            outputExists ? "In Serienbibliothek vorhanden" : "Noch nicht in Serienbibliothek",
            outputExists ? "Vergleich wird berechnet" : "Neue MKV wird erstellt"));
    }
}

