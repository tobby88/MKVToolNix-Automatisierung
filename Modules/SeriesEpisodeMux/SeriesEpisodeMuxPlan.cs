namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

/// <summary>
/// Vollständig aufgelöster Plan für einen einzelnen mkvmerge-Aufruf inklusive Archiv- und Zusatzspurenlogik.
/// </summary>
public sealed class SeriesEpisodeMuxPlan
{
    /// <summary>
    /// Initialisiert einen vollständigen Mux-Plan für einen echten mkvmerge-Lauf.
    /// </summary>
    /// <param name="mkvMergePath">Pfad zur verwendeten <c>mkvmerge.exe</c>.</param>
    /// <param name="outputFilePath">Finaler Ausgabepfad der Zieldatei.</param>
    /// <param name="title">MKV-Titel für den finalen Container.</param>
    /// <param name="videoSources">Alle einzubindenden Videospuren in Mux-Reihenfolge.</param>
    /// <param name="primaryAudioFilePath">Datei, aus der die primäre Audiospur stammt.</param>
    /// <param name="primaryAudioTrackId">Track-ID der primären Audiospur.</param>
    /// <param name="primarySourceAudioTrackIds">Optional explizit weiterzuverwendende Audio-Track-IDs der Primärquelle.</param>
    /// <param name="primarySourceSubtitleTrackIds">Optional explizit weiterzuverwendende Untertitel-Track-IDs der Primärquelle.</param>
    /// <param name="includePrimarySourceAttachments">Gibt an, ob Anhänge der Primärquelle übernommen werden sollen.</param>
    /// <param name="attachmentSourcePath">Optionale separate Quelle für wiederverwendete Archiv-Anhänge.</param>
    /// <param name="audioDescriptionFilePath">Optionale Datei mit Audiodeskriptionsspur.</param>
    /// <param name="audioDescriptionTrackId">Track-ID der AD-Spur in <paramref name="audioDescriptionFilePath"/>, falls eingebettet.</param>
    /// <param name="subtitleFiles">Alle einzubindenden Untertitelspuren.</param>
    /// <param name="attachmentFilePaths">Zusätzlich anzuhängende externe Dateien.</param>
    /// <param name="preservedAttachmentNames">Namen wiederverwendeter Archiv-Anhänge für die GUI-Vorschau.</param>
    /// <param name="usageComparison">Vergleich zwischen bisheriger Ziel-MKV und geplanter Verwendung.</param>
    /// <param name="workingCopy">Optionale Arbeitskopie einer vorhandenen Archivdatei.</param>
    /// <param name="metadata">Bereits aufgelöste Trackmetadaten für Audio und AD.</param>
    /// <param name="notes">Zusätzliche Hinweise für GUI und Vorschau.</param>
    public SeriesEpisodeMuxPlan(
        string mkvMergePath,
        string outputFilePath,
        string title,
        IReadOnlyList<VideoSourcePlan> videoSources,
        string primaryAudioFilePath,
        int primaryAudioTrackId,
        IReadOnlyList<int>? primarySourceAudioTrackIds,
        IReadOnlyList<int>? primarySourceSubtitleTrackIds,
        bool includePrimarySourceAttachments,
        string? attachmentSourcePath,
        string? audioDescriptionFilePath,
        int? audioDescriptionTrackId,
        IReadOnlyList<SubtitleFile> subtitleFiles,
        IReadOnlyList<string> attachmentFilePaths,
        IReadOnlyList<string> preservedAttachmentNames,
        ArchiveUsageComparison usageComparison,
        FileCopyPlan? workingCopy,
        EpisodeTrackMetadata metadata,
        IReadOnlyList<string> notes)
    {
        if (videoSources.Count == 0)
        {
            throw new ArgumentException("Mindestens eine Videospur muss vorhanden sein.", nameof(videoSources));
        }

        MkvMergePath = mkvMergePath;
        OutputFilePath = outputFilePath;
        Title = title;
        VideoSources = videoSources;
        PrimaryAudioFilePath = primaryAudioFilePath;
        PrimaryAudioTrackId = primaryAudioTrackId;
        PrimarySourceAudioTrackIds = primarySourceAudioTrackIds;
        PrimarySourceSubtitleTrackIds = primarySourceSubtitleTrackIds;
        IncludePrimarySourceAttachments = includePrimarySourceAttachments;
        AttachmentSourcePath = attachmentSourcePath;
        AudioDescriptionFilePath = audioDescriptionFilePath;
        AudioDescriptionTrackId = audioDescriptionTrackId;
        SubtitleFiles = subtitleFiles;
        AttachmentFilePaths = attachmentFilePaths;
        PreservedAttachmentNames = preservedAttachmentNames;
        UsageComparison = usageComparison;
        SkipUsageSummary = null;
        WorkingCopy = workingCopy;
        Metadata = metadata;
        Notes = notes;
    }

    private SeriesEpisodeMuxPlan(
        string mkvMergePath,
        string outputFilePath,
        string title,
        string skipReason,
        EpisodeUsageSummary? skipUsageSummary,
        IReadOnlyList<string> notes)
    {
        MkvMergePath = mkvMergePath;
        OutputFilePath = outputFilePath;
        Title = title;
        SkipMux = true;
        SkipReason = skipReason;
        SkipUsageSummary = skipUsageSummary;
        VideoSources = [];
        PrimaryAudioFilePath = string.Empty;
        PrimarySourceAudioTrackIds = null;
        PrimarySourceSubtitleTrackIds = null;
        IncludePrimarySourceAttachments = false;
        AttachmentSourcePath = null;
        SubtitleFiles = [];
        AttachmentFilePaths = [];
        PreservedAttachmentNames = [];
        UsageComparison = ArchiveUsageComparison.Empty;
        Metadata = new EpisodeTrackMetadata("Deutsch - Audio", "Deutsch (sehbehinderte) - Audio");
        Notes = notes;
    }

    /// <summary>
    /// Pfad zur verwendeten <c>mkvmerge.exe</c>.
    /// </summary>
    public string MkvMergePath { get; }

    /// <summary>
    /// Finaler Ausgabepfad der Zieldatei.
    /// </summary>
    public string OutputFilePath { get; }

    /// <summary>
    /// Titel, der als Container-Titel in die Ziel-MKV geschrieben wird.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Kennzeichnet einen rein informativen Skip-Plan ohne echten Mux-Lauf.
    /// </summary>
    public bool SkipMux { get; }

    /// <summary>
    /// Fachliche Begründung, warum kein Mux-Lauf nötig ist.
    /// </summary>
    public string? SkipReason { get; }

    /// <summary>
    /// Optional bereits aufgelöste Nutzungsübersicht für reine Skip-Pläne.
    /// </summary>
    public EpisodeUsageSummary? SkipUsageSummary { get; }

    /// <summary>
    /// Alle einzubindenden Videospuren in finaler Mux-Reihenfolge.
    /// </summary>
    public IReadOnlyList<VideoSourcePlan> VideoSources { get; }

    /// <summary>
    /// Datei, aus der die primäre Audiospur stammt.
    /// </summary>
    public string PrimaryAudioFilePath { get; }

    /// <summary>
    /// Track-ID der primären Audiospur innerhalb von <see cref="PrimaryAudioFilePath"/>.
    /// </summary>
    public int PrimaryAudioTrackId { get; }

    /// <summary>
    /// Optional explizit weiterzuverwendende Audio-Track-IDs der Primärquelle.
    /// </summary>
    public IReadOnlyList<int>? PrimarySourceAudioTrackIds { get; }

    /// <summary>
    /// Optional explizit weiterzuverwendende Untertitel-Track-IDs der Primärquelle.
    /// </summary>
    public IReadOnlyList<int>? PrimarySourceSubtitleTrackIds { get; }

    /// <summary>
    /// Gibt an, ob Anhänge der Primärquelle übernommen werden sollen.
    /// </summary>
    public bool IncludePrimarySourceAttachments { get; }

    /// <summary>
    /// Optionale separate Quelle für wiederverwendete Archiv-Anhänge.
    /// </summary>
    public string? AttachmentSourcePath { get; }

    /// <summary>
    /// Optionale Datei mit Audiodeskriptionsspur.
    /// </summary>
    public string? AudioDescriptionFilePath { get; }

    /// <summary>
    /// Track-ID der AD-Spur in <see cref="AudioDescriptionFilePath"/>, falls diese eingebettet ist.
    /// </summary>
    public int? AudioDescriptionTrackId { get; }

    /// <summary>
    /// Alle einzubindenden Untertitelspuren.
    /// </summary>
    public IReadOnlyList<SubtitleFile> SubtitleFiles { get; }

    /// <summary>
    /// Zusätzlich anzuhängende externe Dateien.
    /// </summary>
    public IReadOnlyList<string> AttachmentFilePaths { get; }

    /// <summary>
    /// Namen wiederverwendeter Archiv-Anhänge für die GUI-Vorschau.
    /// </summary>
    public IReadOnlyList<string> PreservedAttachmentNames { get; }

    /// <summary>
    /// Vergleich zwischen bisheriger Ziel-MKV und geplanter Verwendung.
    /// </summary>
    public ArchiveUsageComparison UsageComparison { get; }

    /// <summary>
    /// Optionale Arbeitskopie einer vorhandenen Archivdatei.
    /// </summary>
    public FileCopyPlan? WorkingCopy { get; }

    /// <summary>
    /// Bereits aufgelöste Trackmetadaten für Audio und Audiodeskription.
    /// </summary>
    public EpisodeTrackMetadata Metadata { get; }

    /// <summary>
    /// Zusätzliche Hinweise für GUI und Vorschau.
    /// </summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>
    /// Erzeugt einen Plan, der einen echten Mux-Lauf fachlich überspringt.
    /// </summary>
    /// <param name="mkvMergePath">Pfad zur verwendeten <c>mkvmerge.exe</c>.</param>
    /// <param name="outputFilePath">Pfad der bereits vollständigen Zieldatei.</param>
    /// <param name="title">Container-Titel der Episode.</param>
    /// <param name="skipReason">Fachliche Begründung für den Skip.</param>
    /// <param name="skipUsageSummary">Optional bereits berechnete Nutzungsübersicht für die GUI.</param>
    /// <param name="notes">Zusätzliche Hinweise für die GUI.</param>
    /// <returns>Skip-Plan ohne ausführbaren Mux-Aufruf.</returns>
    public static SeriesEpisodeMuxPlan CreateSkip(
        string mkvMergePath,
        string outputFilePath,
        string title,
        string skipReason,
        EpisodeUsageSummary? skipUsageSummary = null,
        IReadOnlyList<string>? notes = null)
    {
        return new SeriesEpisodeMuxPlan(mkvMergePath, outputFilePath, title, skipReason, skipUsageSummary, notes ?? []);
    }

    /// <summary>
    /// Baut aus dem Plan die vollständige mkvmerge-Argumentliste für den echten Prozessaufruf.
    /// </summary>
    /// <returns>Argumentliste für <c>mkvmerge.exe</c>.</returns>
    public IReadOnlyList<string> BuildArguments()
    {
        return SeriesEpisodeMuxArgumentBuilder.Build(this);
    }

    /// <summary>
    /// Liefert die Argumentliste als lesbare Vorschau mit einfachen Shell-Escapes.
    /// </summary>
    /// <returns>Mehrzeilige Darstellung der mkvmerge-Argumente.</returns>
    public string GetCommandLinePreview()
    {
        return SeriesEpisodeMuxPresentationBuilder.BuildCommandLinePreview(this);
    }

    /// <summary>
    /// Verdichtet den Plan zu einer kompakten Nutzungszusammenfassung für die GUI.
    /// </summary>
    /// <returns>Zusammenfassung von Archivaktion, Quellen und Zusatzspuren.</returns>
    public EpisodeUsageSummary BuildUsageSummary()
    {
        return SeriesEpisodeMuxPresentationBuilder.BuildUsageSummary(this);
    }

    /// <summary>
    /// Liefert die Nutzungszusammenfassung als flachen mehrzeiligen Textblock.
    /// </summary>
    /// <returns>Kompakter Text für Vorschau- und Statusbereiche.</returns>
    public string BuildCompactSummaryText()
    {
        return SeriesEpisodeMuxPresentationBuilder.BuildCompactSummaryText(this);
    }

    /// <summary>
    /// Liefert alle im Plan referenzierten Eingabedateien ohne Ausgabe- und Hilfspfade.
    /// </summary>
    /// <returns>Deduplicierte Liste aller Eingabedateien des Plans.</returns>
    public IReadOnlyList<string> GetReferencedInputFiles()
    {
        return SeriesEpisodeMuxPresentationBuilder.GetReferencedInputFiles(this);
    }

    /// <summary>
    /// Erzeugt eine ausführliche menschenlesbare Vorschau des Plans inklusive Hinweise.
    /// </summary>
    /// <returns>Mehrzeiliger Vorschautext für die GUI.</returns>
    public string BuildPreviewText()
    {
        return SeriesEpisodeMuxPresentationBuilder.BuildPreviewText(this);
    }

    internal string ResolveRuntimeFilePath(string filePath)
    {
        if (WorkingCopy is not null
            && string.Equals(filePath, WorkingCopy.SourceFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return WorkingCopy.DestinationFilePath;
        }

        return filePath;
    }
}
