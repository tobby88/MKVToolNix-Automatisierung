using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

/// <summary>
/// Vom UI zusammengestellte Eingabe für einen konkreten Mux-Vorgang.
/// </summary>
/// <param name="MainVideoPath">Ausgewählte Hauptvideoquelle oder bei Zusatzmaterial-only-Fällen der Platzhalterpfad der Episode.</param>
/// <param name="AudioDescriptionPath">Optional ausgewählte AD-Datei.</param>
/// <param name="SubtitlePaths">Aktuell ausgewählte externe Untertiteldateien.</param>
/// <param name="AttachmentPaths">Aktuell ausgewählte TXT-Anhänge inklusive automatisch erkannter Videobegleiter.</param>
/// <param name="OutputFilePath">Vollständiger Zielpfad der Ausgabe-MKV.</param>
/// <param name="Title">Finaler Episodentitel für Container und Dateiname.</param>
/// <param name="ExcludedSourcePaths">Optionaler Satz bewusst ausgeschlossener Quellpfade für Fallback-Detection.</param>
/// <param name="ManualAttachmentPaths">Optional nur manuell ausgewählte TXT-Anhänge ohne automatisch erkannte Videobegleiter.</param>
/// <param name="HasPrimaryVideoSource">Kennzeichnet, ob aktuell eine frische Hauptvideoquelle vorliegt.</param>
/// <param name="PlannedVideoPaths">
/// Bereits erkannte und vom UI bestätigte Videopfad-Auswahl in finaler Reihenfolge.
/// Wenn gesetzt, baut die Planerstellung daraus weiter und vermeidet eine erneute Ordnererkennung.
/// </param>
/// <param name="DetectionNotes">Optional bereits bekannte Detection-Hinweise, die in den Plan übernommen werden sollen.</param>
/// <param name="VideoLanguageOverride">Optionaler manueller Sprachcode für alle geplanten Videospuren.</param>
/// <param name="AudioLanguageOverride">Optionaler manueller Sprachcode für alle geplanten normalen Audiospuren.</param>
/// <param name="OriginalLanguage">
/// Originalsprache der Serie (aus TVDB-Metadaten), z. B. <c>swe</c> für Schwedisch oder <c>de</c> für Deutsch.
/// Null oder leer, wenn unbekannt; in diesem Fall wird der <c>--original-flag</c> wie bisher auf <c>yes</c> gesetzt.
/// </param>
public sealed record SeriesEpisodeMuxRequest(
    string MainVideoPath,
    string? AudioDescriptionPath,
    IReadOnlyList<string> SubtitlePaths,
    IReadOnlyList<string> AttachmentPaths,
    string OutputFilePath,
    string Title,
    IReadOnlyCollection<string>? ExcludedSourcePaths = null,
    IReadOnlyList<string>? ManualAttachmentPaths = null,
    bool HasPrimaryVideoSource = true,
    IReadOnlyList<string>? PlannedVideoPaths = null,
    IReadOnlyList<string>? DetectionNotes = null,
    string? VideoLanguageOverride = null,
    string? AudioLanguageOverride = null,
    string? OriginalLanguage = null);

/// <summary>
/// Ergebnis der automatischen Dateierkennung rund um eine Episode.
/// </summary>
/// <param name="MainVideoPath">Erkannte Hauptvideoquelle oder Platzhalterpfad bei Quellen ohne eigenes Video.</param>
/// <param name="AdditionalVideoPaths">Zusätzliche Videodateien, die zur gleichen Episode gehören.</param>
/// <param name="AudioDescriptionPath">Optional erkannte Audiodeskriptionsquelle.</param>
/// <param name="SubtitlePaths">Erkannte externe Untertiteldateien.</param>
/// <param name="AttachmentPaths">Erkannte Anhänge, insbesondere TXT-Metadaten aus der Mediathek.</param>
/// <param name="RelatedFilePaths">Alle Quelldateien, die für spätere Aufräum- und Review-Schritte zur Episode gehören.</param>
/// <param name="SuggestedOutputFilePath">Automatisch vorgeschlagener Zielpfad.</param>
/// <param name="SuggestedTitle">Automatisch erkannter oder abgeleiteter Episodentitel.</param>
/// <param name="SeriesName">Automatisch erkannter Serienname.</param>
/// <param name="SeasonNumber">Automatisch erkannte Staffelnummer oder <c>xx</c>.</param>
/// <param name="EpisodeNumber">Automatisch erkannte Episodennummer oder <c>xx</c>.</param>
/// <param name="RequiresManualCheck">Kennzeichnet, ob die Erkennung vor dem Muxen eine manuelle Prüfung braucht.</param>
/// <param name="ManualCheckFilePaths">Quellpfade, die den manuellen Prüfbedarf ausgelöst haben.</param>
/// <param name="Notes">Erkennungshinweise, die im Plan sichtbar bleiben sollen.</param>
/// <param name="HasPrimaryVideoSource">Kennzeichnet, ob eine echte neue Hauptvideoquelle vorhanden ist.</param>
/// <param name="OriginalLanguage">Optional bekannte Originalsprache der Serie aus TVDB oder gespeicherten Serien-Mappings.</param>
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
    IReadOnlyList<string> Notes,
    bool HasPrimaryVideoSource = true,
    string? OriginalLanguage = null);

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
/// Enthält bewusst nur die erste Audiospur der Quelle; für vollständige Audioauswahl ist
/// <see cref="ContainerMetadata"/> vorgesehen.
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
    bool IsDefaultTrack,
    bool IsOriginalLanguage = false,
    TimeSpan? Duration = null);

/// <summary>
/// Anhänge, die beim Archivabgleich erhalten bleiben können.
/// </summary>
public sealed record ContainerAttachmentMetadata(
    int Id,
    string FileName);

/// <summary>
/// Zusammenfassung aller Tracks und Anhänge eines Containers.
/// </summary>
public sealed record ContainerMetadata(
    string Title,
    IReadOnlyList<ContainerTrackMetadata> Tracks,
    IReadOnlyList<ContainerAttachmentMetadata> Attachments);

/// <summary>
/// Beschreibt einen einzelnen zu ändernden Track-Header-Wert einer vorhandenen MKV-Datei.
/// </summary>
/// <param name="PropertyName">Von <c>mkvpropedit</c> verstandener Property-Name, z. B. <c>name</c> oder <c>flag-default</c>.</param>
/// <param name="DisplayName">Lesbarer Anzeigename für Hinweise und Vorschau.</param>
/// <param name="CurrentDisplayValue">Aktueller Wert in lesbarer Form.</param>
/// <param name="ExpectedDisplayValue">Zielwert in lesbarer Form.</param>
/// <param name="ExpectedMkvPropEditValue">Zielwert in der von <c>mkvpropedit</c> erwarteten Rohform.</param>
public sealed record TrackHeaderValueEdit(
    string PropertyName,
    string DisplayName,
    string CurrentDisplayValue,
    string ExpectedDisplayValue,
    string ExpectedMkvPropEditValue);

/// <summary>
/// Beschreibt direkte Header-Anpassungen an einer konkreten Spur einer vorhandenen MKV-Datei.
/// </summary>
/// <param name="Selector">
/// Von <c>mkvpropedit</c> verstandener Edit-Selektor für genau den zu bearbeitenden Track.
/// Die Nummerierung folgt bewusst der Reihenfolge aus <c>mkvmerge --identify</c>, damit die
/// Planung dieselbe Tracksicht nutzt wie der restliche Archivabgleich.
/// </param>
/// <param name="DisplayLabel">Lesbare Kurzbeschreibung der betroffenen Spur für GUI und Vorschau.</param>
/// <param name="CurrentTrackName">Aktuell im Container gesetzter Trackname.</param>
/// <param name="ExpectedTrackName">Projektweit erwarteter Zielname für diesen Track.</param>
/// <param name="ValueEdits">Alle Header-Werte, die an dieser Spur gesetzt werden müssen.</param>
public sealed record TrackHeaderEditOperation(
    string Selector,
    string DisplayLabel,
    string CurrentTrackName,
    string ExpectedTrackName,
    IReadOnlyList<TrackHeaderValueEdit>? ValueEdits = null);

/// <summary>
/// Beschreibt eine direkte Anpassung des Container-Titels einer vorhandenen MKV-Datei.
/// </summary>
/// <param name="CurrentTitle">Aktuell im Matroska-Header gespeicherter Titel.</param>
/// <param name="ExpectedTitle">Projektweit erwarteter Episodentitel für denselben Container.</param>
public sealed record ContainerTitleEditOperation(
    string CurrentTitle,
    string ExpectedTitle);

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
/// Beschreibt eine einzubindende normale Audiospur im finalen Mux-Plan.
/// </summary>
/// <param name="FilePath">Quelldatei, aus der die Audiospur stammt.</param>
/// <param name="TrackId">Track-ID innerhalb von <paramref name="FilePath"/>.</param>
/// <param name="TrackName">Finaler Trackname für GUI und mkvmerge-Metadaten.</param>
/// <param name="IsDefaultTrack">Kennzeichnet die Standard-Tonspur des finalen Containers.</param>
/// <param name="LanguageCode">Projektweit normalisierter Sprachcode der Spur.</param>
public sealed record AudioSourcePlan(
    string FilePath,
    int TrackId,
    string TrackName,
    bool IsDefaultTrack,
    string LanguageCode = "de");

/// <summary>
/// Beschreibt eine einzubindende Audiodeskriptionsspur im finalen Mux-Plan.
/// </summary>
/// <param name="FilePath">Quelldatei, aus der die AD-Spur stammt.</param>
/// <param name="TrackId">Track-ID innerhalb von <paramref name="FilePath"/>.</param>
/// <param name="TrackName">Finaler Trackname für GUI und mkvmerge-Metadaten.</param>
/// <param name="LanguageCode">Projektweit normalisierter Sprachcode der AD-Spur.</param>
public sealed record AudioDescriptionSourcePlan(
    string FilePath,
    int TrackId,
    string TrackName,
    string LanguageCode = "de");

/// <summary>
/// Bereits fachlich ausgewählte Videospur aus einer frischen Quelldatei oder aus einer vorhandenen Ziel-MKV.
/// </summary>
/// <param name="FilePath">Quelldatei, aus der die Videospur gelesen wird.</param>
/// <param name="TrackId">Track-ID innerhalb von <paramref name="FilePath"/>.</param>
/// <param name="VideoWidth">Horizontale Pixelbreite der Spur für Qualitäts- und Namenslogik.</param>
/// <param name="CodecLabel">Normalisiertes Codec-Label der Videospur.</param>
/// <param name="LanguageCode">Projektweit normalisierter Sprachcode der Spur.</param>
public sealed record VideoTrackSelection(
    string FilePath,
    int TrackId,
    int VideoWidth,
    string CodecLabel,
    string LanguageCode);

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

        if (width >= 900)
        {
            return new ResolutionLabel("qHD");
        }

        return new ResolutionLabel("SD");
    }
}
