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
    string Language);

public sealed record VideoSourcePlan(
    string FilePath,
    int TrackId,
    string TrackName,
    bool IsDefaultTrack);

public sealed record EpisodeTrackMetadata(
    string AudioTrackName,
    string AudioDescriptionTrackName);

public sealed record SubtitleFile(string FilePath, SubtitleKind Kind)
{
    public string TrackName => $"Deutsch (hoergeschaedigte) - {Kind.DisplayName}";
}

public sealed record SubtitleKind(string DisplayName)
{
    public static SubtitleKind FromExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".srt" => new SubtitleKind("SRT"),
        ".ass" => new SubtitleKind("SSA"),
        ".vtt" => new SubtitleKind("WebVTT"),
        _ => new SubtitleKind("Unbekannt")
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
