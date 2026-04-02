using MkvToolnixAutomatisierung.Services;

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
    string Title,
    IReadOnlyCollection<string>? ExcludedSourcePaths = null,
    IReadOnlyList<string>? ManualAttachmentPaths = null);

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
    /// <summary>
    /// Gibt an, ob eine bereits vorhandene Arbeitskopie noch zur aktuellen Archivdatei passt.
    /// </summary>
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
/// Beschreibt Bestandteile der bisherigen Zieldatei, die durch die neue Planung entfallen oder ersetzt werden.
/// </summary>
public sealed record ArchiveUsageChange(
    string RemovedText,
    string Reason);

/// <summary>
/// Vergleich zwischen vorhandener Zieldatei und geplanter Verwendung für die GUI-Zusammenfassung.
/// </summary>
public sealed record ArchiveUsageComparison(
    ArchiveUsageChange? MainVideo,
    ArchiveUsageChange? AdditionalVideos,
    ArchiveUsageChange? Audio,
    ArchiveUsageChange? AudioDescription,
    ArchiveUsageChange? Subtitles,
    ArchiveUsageChange? Attachments)
{
    /// <summary>
    /// Leerer Vergleich ohne entfernte oder ersetzte Altbestandteile.
    /// </summary>
    public static ArchiveUsageComparison Empty { get; } = new(
        MainVideo: null,
        AdditionalVideos: null,
        Audio: null,
        AudioDescription: null,
        Subtitles: null,
        Attachments: null);
}

/// <summary>
/// Beschreibt eine einzubindende Videospur im finalen Mux-Plan.
/// </summary>
public sealed record VideoSourcePlan(
    string FilePath,
    int TrackId,
    string TrackName,
    bool IsDefaultTrack,
    string LanguageCode = "de");

/// <summary>
/// Zielnamen und Sprachcodes für automatisch erzeugte Audio- und AD-Trackbezeichnungen.
/// </summary>
public sealed record EpisodeTrackMetadata(
    string AudioTrackName,
    string AudioDescriptionTrackName,
    string AudioLanguageCode = "de",
    string AudioDescriptionLanguageCode = "de");

/// <summary>
/// Barrierefreiheits-Markierung einer Untertitelspur.
/// </summary>
public enum SubtitleAccessibility
{
    /// <summary>
    /// Normale Untertitel ohne explizite Hörgeschädigten-Markierung.
    /// </summary>
    Standard = 0,

    /// <summary>
    /// Untertitel für hörgeschädigte Zuschauer.
    /// </summary>
    HearingImpaired = 1
}

/// <summary>
/// Externe oder eingebettete Untertitelquelle für den finalen Mux.
/// </summary>
public sealed record SubtitleFile(
    string FilePath,
    SubtitleKind Kind,
    int? EmbeddedTrackId = null,
    string? SourceLabel = null,
    string LanguageCode = "de")
{
    /// <summary>
    /// Erzeugt eine automatisch erkannte externe Untertiteldatei mit der aktuell projektweit gültigen Standard-Sprache.
    /// </summary>
    /// <param name="filePath">Pfad zur externen Untertiteldatei.</param>
    /// <param name="kind">Normalisierter Untertiteltyp.</param>
    /// <returns>Untertitelmodell für den typischen Mediathek-Fall.</returns>
    public static SubtitleFile CreateDetectedExternal(string filePath, SubtitleKind kind)
    {
        // Mediathek-Untertitel sind fachlich derzeit immer deutsch. Audio kann davon bewusst abweichen.
        return new SubtitleFile(filePath, kind, LanguageCode: "de");
    }

    /// <summary>
    /// Erzeugt eine eingebettete Untertitelspur aus einer vorhandenen Ziel- oder Archivdatei.
    /// </summary>
    /// <param name="containerFilePath">Containerdatei, aus der der Track stammt.</param>
    /// <param name="kind">Normalisierter Untertiteltyp.</param>
    /// <param name="embeddedTrackId">Track-ID innerhalb des Containers.</param>
    /// <param name="sourceLabel">Optionaler Anzeigename der Spur.</param>
    /// <param name="languageCode">Sprachcode der vorhandenen Spur.</param>
    /// <returns>Untertitelmodell für eine wiederverwendete eingebettete Spur.</returns>
    public static SubtitleFile CreateEmbedded(
        string containerFilePath,
        SubtitleKind kind,
        int embeddedTrackId,
        string? sourceLabel,
        string? languageCode)
    {
        return new SubtitleFile(
            containerFilePath,
            kind,
            embeddedTrackId,
            sourceLabel,
            MediaLanguageHelper.NormalizeMuxLanguageCode(languageCode));
    }

    /// <summary>
    /// Externe Untertitel werden derzeit standardmäßig als HI/SDH behandelt, bis eine sichere automatische Unterscheidung vorliegt.
    /// </summary>
    public SubtitleAccessibility Accessibility { get; init; } = SubtitleAccessibility.HearingImpaired;

    /// <summary>
    /// Kennzeichnet, dass die Untertitelspur aus einem vorhandenen Container wiederverwendet wird.
    /// </summary>
    public bool IsEmbedded => EmbeddedTrackId is not null;

    /// <summary>
    /// Kennzeichnet, ob die Spur als Untertitel für Hörgeschädigte markiert ist.
    /// </summary>
    public bool IsHearingImpaired => Accessibility == SubtitleAccessibility.HearingImpaired;

    /// <summary>
    /// Vollständig aufgelöster Trackname für GUI und mkvmerge-Metadaten.
    /// </summary>
    public string TrackName => IsHearingImpaired
        ? $"{MediaLanguageHelper.GetLanguageDisplayName(LanguageCode)} (hörgeschädigte) - {Kind.DisplayName}"
        : $"{MediaLanguageHelper.GetLanguageDisplayName(LanguageCode)} - {Kind.DisplayName}";

    /// <summary>
    /// Kompakte Vorschau der Spur für Plan- und Archivvergleiche.
    /// </summary>
    public string PreviewLabel => IsEmbedded
        ? $"{TrackName} (aus Zieldatei)"
        : Path.GetFileName(FilePath);
}

/// <summary>
/// Normalisierte Darstellung eines Untertiteltyps inklusive Sortierpräferenz.
/// </summary>
public sealed record SubtitleKind(string DisplayName, int SortRank)
{
    /// <summary>
    /// Leitet den projektweit verwendeten Untertiteltyp aus einer Dateiendung ab.
    /// </summary>
    /// <param name="extension">Dateiendung inklusive Punkt.</param>
    /// <returns>Normalisierter Untertiteltyp mit Sortierreihenfolge.</returns>
    public static SubtitleKind FromExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".ass" => new SubtitleKind("SSA", 0),
        ".srt" => new SubtitleKind("SRT", 1),
        ".vtt" => new SubtitleKind("WebVTT", 2),
        _ => new SubtitleKind("Unbekannt", 9)
    };

    /// <summary>
    /// Leitet den projektweit verwendeten Untertiteltyp aus vorhandenen Container-Metadaten ab.
    /// </summary>
    /// <param name="codecLabel">Bereits normalisiertes oder rohes Codec-Label der Spur.</param>
    /// <returns>Normalisierter Untertiteltyp oder <see langword="null"/> für unbekannte Codecs.</returns>
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
    /// <summary>
    /// Leitet aus einer horizontalen Pixelbreite ein grobes projektweites Auflösungslabel ab.
    /// </summary>
    /// <param name="width">Horizontale Pixelbreite der Videospur.</param>
    /// <returns>Relatives Auflösungslabel für Qualitätsvergleiche.</returns>
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
