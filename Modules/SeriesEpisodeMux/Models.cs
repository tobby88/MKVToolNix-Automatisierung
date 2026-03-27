namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

/// <summary>
/// Vom UI zusammengestellte Eingabe für einen konkreten Mux-Vorgang.
/// </summary>
public sealed record SeriesEpisodeMuxRequest(
    string MainVideoPath,
    string? AudioDescriptionPath,
    IReadOnlyList<string> SubtitlePaths,
    IReadOnlyList<string> AttachmentPaths,
    string OutputFilePath,
    string Title);

/// <summary>
/// Ergebnis der automatischen Dateierkennung rund um eine Episode.
/// </summary>
public sealed record AutoDetectedEpisodeFiles(
    string MainVideoPath,
    IReadOnlyList<string> AdditionalVideoPaths,
    string? AudioDescriptionPath,
    IReadOnlyList<string> SubtitlePaths,
    IReadOnlyList<string> AttachmentPaths,
    IReadOnlyList<string> RelatedFilePaths,
    string SuggestedOutputFilePath,
    string SuggestedTitle,
    string SeriesName,
    string SeasonNumber,
    string EpisodeNumber,
    bool RequiresManualCheck,
    IReadOnlyList<string> ManualCheckFilePaths,
    IReadOnlyList<string> Notes);

/// <summary>
/// Fortschrittsmeldung aus der Dateierkennung.
/// </summary>
public sealed record DetectionProgressUpdate(
    string StatusText,
    int ProgressPercent);

/// <summary>
/// Beschreibt eine wiederverwendbare oder neu anzulegende Arbeitskopie einer bestehenden Archivdatei.
/// </summary>
public sealed record FileCopyPlan(
    string SourceFilePath,
    string DestinationFilePath,
    long FileSizeBytes,
    DateTime SourceLastWriteUtc)
{
    public bool IsReusable
    {
        get
        {
            if (!File.Exists(DestinationFilePath))
            {
                return false;
            }

            var destinationInfo = new FileInfo(DestinationFilePath);
            return destinationInfo.Length == FileSizeBytes
                && destinationInfo.LastWriteTimeUtc >= SourceLastWriteUtc;
        }
    }
}

/// <summary>
/// Verdichtete Primär-Metadaten einer Videodatei für Qualitätsvergleiche.
/// </summary>
public sealed record MediaTrackMetadata(
    int VideoTrackId,
    int AudioTrackId,
    int VideoWidth,
    ResolutionLabel ResolutionLabel,
    string VideoCodecLabel,
    string AudioCodecLabel,
    string VideoLanguage,
    string AudioLanguage);

/// <summary>
/// Metadaten der ersten relevanten Audiospur einer Quelldatei.
/// </summary>
public sealed record AudioTrackMetadata(
    int TrackId,
    string CodecLabel,
    string Language,
    string TrackName,
    bool IsVisualImpaired);

/// <summary>
/// Vollständige Track-Metadaten eines vorhandenen Containers.
/// </summary>
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

/// <summary>
/// Anhänge, die beim Archivabgleich erhalten bleiben können.
/// </summary>
public sealed record ContainerAttachmentMetadata(string FileName);

/// <summary>
/// Zusammenfassung aller Tracks und Anhänge eines Containers.
/// </summary>
public sealed record ContainerMetadata(
    IReadOnlyList<ContainerTrackMetadata> Tracks,
    IReadOnlyList<ContainerAttachmentMetadata> Attachments);

/// <summary>
/// Beschreibt eine einzubindende Videospur im finalen Mux-Plan.
/// </summary>
public sealed record VideoSourcePlan(
    string FilePath,
    int TrackId,
    string TrackName,
    bool IsDefaultTrack);

/// <summary>
/// Zielnamen für automatisch erzeugte Audio- und AD-Trackbezeichnungen.
/// </summary>
public sealed record EpisodeTrackMetadata(
    string AudioTrackName,
    string AudioDescriptionTrackName);

/// <summary>
/// Externe oder eingebettete Untertitelquelle für den finalen Mux.
/// </summary>
public sealed record SubtitleFile(string FilePath, SubtitleKind Kind, int? EmbeddedTrackId = null, string? SourceLabel = null)
{
    public bool IsEmbedded => EmbeddedTrackId is not null;

    public string TrackName => $"Deutsch (hörgeschädigte) - {Kind.DisplayName}";

    public string PreviewLabel => IsEmbedded
        ? $"{(string.IsNullOrWhiteSpace(SourceLabel) ? Path.GetFileName(FilePath) : SourceLabel)} ({Kind.DisplayName}, aus Zieldatei)"
        : Path.GetFileName(FilePath);
}

/// <summary>
/// Normalisierte Darstellung eines Untertiteltyps inklusive Sortierpräferenz.
/// </summary>
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

/// <summary>
/// Vereinfachtes Auflösungslabel, das nur für relative Qualitätsentscheidungen gebraucht wird.
/// </summary>
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
