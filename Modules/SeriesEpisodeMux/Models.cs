namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

public sealed record SeriesEpisodeMuxRequest(
    string MainVideoPath,
    string? AudioDescriptionPath,
    IReadOnlyList<string> SubtitlePaths,
    IReadOnlyList<string> AttachmentPaths,
    string OutputFilePath,
    string Title);

public sealed record AutoDetectedEpisodeFiles(
    string MainVideoPath,
    IReadOnlyList<string> AdditionalVideoPaths,
    string? AudioDescriptionPath,
    IReadOnlyList<string> SubtitlePaths,
    IReadOnlyList<string> AttachmentPaths,
    string SuggestedOutputFilePath,
    string SuggestedTitle,
    string SeriesName,
    string SeasonNumber,
    string EpisodeNumber,
    bool RequiresManualCheck,
    IReadOnlyList<string> ManualCheckFilePaths,
    IReadOnlyList<string> Notes);

public sealed record DetectionProgressUpdate(
    string StatusText,
    int ProgressPercent);

public sealed record FileCopyPlan(
    string SourceFilePath,
    string DestinationFilePath,
    long FileSizeBytes);

public sealed record MediaTrackMetadata(
    int VideoTrackId,
    int AudioTrackId,
    int VideoWidth,
    ResolutionLabel ResolutionLabel,
    string VideoCodecLabel,
    string AudioCodecLabel,
    string VideoLanguage,
    string AudioLanguage);

public sealed record AudioTrackMetadata(
    int TrackId,
    string CodecLabel,
    string Language,
    string TrackName,
    bool IsVisualImpaired);

public sealed record ContainerTrackMetadata(
    int TrackId,
    string Type,
    string CodecLabel,
    string Language,
    string TrackName,
    int VideoWidth,
    bool IsVisualImpaired,
    bool IsHearingImpaired,
    bool IsDefaultTrack);

public sealed record ContainerAttachmentMetadata(string FileName);

public sealed record ContainerMetadata(
    IReadOnlyList<ContainerTrackMetadata> Tracks,
    IReadOnlyList<ContainerAttachmentMetadata> Attachments);

public sealed record VideoSourcePlan(
    string FilePath,
    int TrackId,
    string TrackName,
    bool IsDefaultTrack);

public sealed record EpisodeTrackMetadata(
    string AudioTrackName,
    string AudioDescriptionTrackName);

public sealed record SubtitleFile(string FilePath, SubtitleKind Kind, int? EmbeddedTrackId = null, string? SourceLabel = null)
{
    public bool IsEmbedded => EmbeddedTrackId is not null;

    public string TrackName => $"Deutsch (hoergeschaedigte) - {Kind.DisplayName}";

    public string PreviewLabel => IsEmbedded
        ? $"{(string.IsNullOrWhiteSpace(SourceLabel) ? Path.GetFileName(FilePath) : SourceLabel)} ({Kind.DisplayName}, Archiv)"
        : Path.GetFileName(FilePath);
}

public sealed record SubtitleKind(string DisplayName, int SortRank)
{
    public static SubtitleKind FromExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".ass" => new SubtitleKind("SSA", 0),
        ".srt" => new SubtitleKind("SRT", 1),
        ".vtt" => new SubtitleKind("WebVTT", 2),
        _ => new SubtitleKind("Unbekannt", 9)
    };

    public static SubtitleKind? FromExistingCodec(string codecLabel) => codecLabel.ToUpperInvariant() switch
    {
        "SSA" => new SubtitleKind("SSA", 0),
        "SRT" => new SubtitleKind("SRT", 1),
        "WEBVTT" => new SubtitleKind("WebVTT", 2),
        _ => null
    };
}

public sealed record ResolutionLabel(string Value)
{
    public static ResolutionLabel FromWidth(int width)
    {
        if (width >= 3800)
        {
            return new ResolutionLabel("UHD");
        }

        if (width >= 1900)
        {
            return new ResolutionLabel("FHD");
        }

        if (width >= 1200)
        {
            return new ResolutionLabel("HD");
        }

        return new ResolutionLabel("SD");
    }
}
