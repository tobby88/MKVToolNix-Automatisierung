using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Verwaltet die Serienbibliothek als bevorzugtes Ziel und entscheidet, wie bestehende Archiv-MKV-Dateien integriert werden.
/// </summary>
public sealed partial class SeriesArchiveService
{
    /// <summary>
    /// Standardwurzel der Serienbibliothek für dieses Projekt.
    /// </summary>
    public const string DefaultArchiveRootDirectory = @"Z:\Videos\Serien";

    private readonly MkvMergeProbeService _probeService;
    private readonly AppArchiveSettingsStore _archiveSettingsStore;

    /// <summary>
    /// Initialisiert den Archivdienst für Pfadvorschläge und bestehende Ziel-MKV-Integration.
    /// </summary>
    /// <param name="probeService">Liest Track- und Container-Metadaten vorhandener Archivdateien.</param>
    /// <param name="archiveSettingsStore">Persistenter Store für den konfigurierten Archivwurzelpfad.</param>
    public SeriesArchiveService(MkvMergeProbeService probeService, AppArchiveSettingsStore archiveSettingsStore)
    {
        _probeService = probeService;
        _archiveSettingsStore = archiveSettingsStore;
        ArchiveRootDirectory = NormalizeArchiveRootDirectory(_archiveSettingsStore.Load().DefaultSeriesArchiveRootPath);
    }

    /// <summary>
    /// Aktuell konfigurierter Wurzelpfad der Serienbibliothek.
    /// </summary>
    public string ArchiveRootDirectory { get; private set; }

    /// <summary>
    /// Baut den bevorzugten Ausgabepfad für eine Episode relativ zur Serienbibliothek oder zum Fallback-Ordner.
    /// </summary>
    /// <param name="fallbackDirectory">Verzeichnis, das genutzt wird, wenn die Serienbibliothek nicht erreichbar ist.</param>
    /// <param name="seriesName">Serienname der Episode.</param>
    /// <param name="seasonNumber">Normalisierte Staffelnummer oder Jahresstaffel.</param>
    /// <param name="episodeNumber">Normalisierte Episodennummer.</param>
    /// <param name="title">Episodentitel.</param>
    /// <returns>Vollständiger Zielpfad für die erzeugte MKV-Datei.</returns>
    public string BuildSuggestedOutputPath(string fallbackDirectory, string seriesName, string seasonNumber, string episodeNumber, string title)
    {
        if (!IsArchiveAvailable())
        {
            return EpisodeMetadataPath(fallbackDirectory, seriesName, seasonNumber, episodeNumber, title);
        }

        return BuildArchiveOutputPath(seriesName, seasonNumber, episodeNumber, title);
    }

    /// <summary>
    /// Baut den fachlich korrekten Bibliothekspfad unterhalb der konfigurierten Archivwurzel, ohne vorherige Erreichbarkeitspruefung.
    /// Dieser Pfad wird gebraucht, wenn der Benutzer explizit die Serienbibliothek als Zielwurzel ausgewaehlt hat und
    /// die Struktur auch bei einem voruebergehend negativen Reachability-Check stabil bleiben soll.
    /// </summary>
    /// <param name="seriesName">Serienname der Episode.</param>
    /// <param name="seasonNumber">Normalisierte Staffelnummer oder Jahresstaffel.</param>
    /// <param name="episodeNumber">Normalisierte Episodennummer.</param>
    /// <param name="title">Episodentitel.</param>
    /// <returns>Vollstaendiger Pfad innerhalb der konfigurierten Serienbibliothek.</returns>
    public string BuildArchiveOutputPath(string seriesName, string seasonNumber, string episodeNumber, string title)
    {
        var seasonFolderName = int.TryParse(seasonNumber, out var parsedSeason) && parsedSeason > 0
            ? $"Season {parsedSeason}"
            : "Season xx";

        var targetDirectory = Path.Combine(
            ArchiveRootDirectory,
            SanitizePathPart(seriesName),
            seasonFolderName);

        return Path.Combine(targetDirectory, EpisodeFileNameHelper.BuildEpisodeFileName(seriesName, seasonNumber, episodeNumber, title));
    }

    /// <summary>
    /// Prüft, ob ein Ausgabepfad innerhalb der konfigurierten Serienbibliothek liegt.
    /// </summary>
    /// <param name="outputFilePath">Zu prüfender Ausgabepfad.</param>
    /// <returns><see langword="true"/>, wenn der Pfad innerhalb der Archivwurzel liegt.</returns>
    public bool IsArchivePath(string outputFilePath)
    {
        return PathComparisonHelper.IsPathWithinRoot(outputFilePath, ArchiveRootDirectory);
    }

    /// <summary>
    /// Speichert einen neuen Standardpfad für die Serienbibliothek dauerhaft in den App-Einstellungen.
    /// </summary>
    /// <param name="archiveRootDirectory">Neuer Bibliothekswurzelpfad.</param>
    public void ConfigureArchiveRootDirectory(string archiveRootDirectory)
    {
        var normalizedPath = NormalizeArchiveRootDirectory(archiveRootDirectory);
        _archiveSettingsStore.Save(new AppArchiveSettings
        {
            DefaultSeriesArchiveRootPath = normalizedPath
        });
        ArchiveRootDirectory = normalizedPath;
    }

    /// <summary>
    /// Prüft, ob die konfigurierte Serienbibliothek aktuell erreichbar ist.
    /// </summary>
    /// <returns><see langword="true"/>, wenn das Zielverzeichnis existiert.</returns>
    public bool IsArchiveAvailable()
    {
        return Directory.Exists(ArchiveRootDirectory);
    }

    /// <summary>
    /// Erzeugt den UI-Hinweistext für den Fall, dass die Bibliothek aktuell nicht erreichbar ist.
    /// </summary>
    /// <returns>Mehrzeilige Warnmeldung mit dem konfigurierten Bibliothekspfad.</returns>
    public string BuildArchiveUnavailableWarningMessage()
    {
        return "Die konfigurierte Serienbibliothek ist aktuell nicht erreichbar:"
            + Environment.NewLine
            + ArchiveRootDirectory
            + Environment.NewLine
            + Environment.NewLine
            + "Automatische Ausgabepfade verwenden deshalb vorerst den jeweiligen Quellordner.";
    }

    private static string EpisodeMetadataPath(string fallbackDirectory, string seriesName, string seasonNumber, string episodeNumber, string title)
    {
        return Path.Combine(fallbackDirectory, EpisodeFileNameHelper.BuildEpisodeFileName(seriesName, seasonNumber, episodeNumber, title));
    }

    private static string NormalizeArchiveRootDirectory(string? archiveRootDirectory)
    {
        if (string.IsNullOrWhiteSpace(archiveRootDirectory))
        {
            return DefaultArchiveRootDirectory;
        }

        try
        {
            var fullPath = Path.GetFullPath(archiveRootDirectory.Trim());
            var rootPath = Path.GetPathRoot(fullPath);
            return string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return DefaultArchiveRootDirectory;
        }
    }

    private static string SanitizePathPart(string value)
    {
        return EpisodeFileNameHelper.SanitizePathSegment(value);
    }
}

/// <summary>
/// Beschreibt, wie eine bereits vorhandene Archivdatei in einen neuen Mux-Plan eingebunden werden soll.
/// </summary>
public sealed record ArchiveIntegrationDecision(
    string OutputFilePath,
    bool SkipMux,
    string? SkipReason,
    EpisodeUsageSummary? SkipUsageSummary,
    FileCopyPlan? WorkingCopy,
    string PrimarySourcePath,
    IReadOnlyList<VideoTrackSelection> VideoSelections,
    IReadOnlyList<int>? RetainedAudioTrackIds,
    IReadOnlyList<int>? PrimarySubtitleTrackIds,
    IReadOnlyList<int>? PrimarySourceAttachmentIds,
    bool IncludePrimaryAttachments,
    string? AttachmentSourcePath,
    IReadOnlyList<int>? AttachmentSourceAttachmentIds,
    IReadOnlyList<string> AdditionalVideoPaths,
    string? AudioDescriptionFilePath,
    int? AudioDescriptionTrackId,
    IReadOnlyList<SubtitleFile> SubtitleFiles,
    IReadOnlyList<string> AttachmentFilePaths,
    bool FallbackToRequestAttachments,
    IReadOnlyList<string> PreservedAttachmentNames,
    ArchiveUsageComparison UsageComparison,
    IReadOnlyList<string> Notes)
{
    /// <summary>
    /// Erstellt eine Archiventscheidung für ein komplett neues Ziel ohne bestehende Archivdatei.
    /// </summary>
    /// <param name="outputPath">Zielpfad der neu zu erzeugenden MKV-Datei.</param>
    /// <returns>Standardentscheidung für einen frischen Mux-Lauf.</returns>
    public static ArchiveIntegrationDecision CreateForFreshTarget(string outputPath)
    {
        return new ArchiveIntegrationDecision(
            outputPath,
            SkipMux: false,
            SkipReason: null,
            SkipUsageSummary: null,
            WorkingCopy: null,
            PrimarySourcePath: string.Empty,
            VideoSelections: [],
            RetainedAudioTrackIds: null,
            PrimarySubtitleTrackIds: null,
            PrimarySourceAttachmentIds: null,
            IncludePrimaryAttachments: false,
            AttachmentSourcePath: null,
            AttachmentSourceAttachmentIds: null,
            AdditionalVideoPaths: [],
            AudioDescriptionFilePath: null,
            AudioDescriptionTrackId: null,
            SubtitleFiles: [],
            AttachmentFilePaths: [],
            FallbackToRequestAttachments: true,
            PreservedAttachmentNames: [],
            UsageComparison: ArchiveUsageComparison.Empty,
            Notes: []);
    }

    /// <summary>
    /// Erstellt eine Archiventscheidung, die den eigentlichen Mux-Lauf vollständig überspringt.
    /// </summary>
    /// <param name="outputPath">Pfad der bereits vollständigen Zieldatei.</param>
    /// <param name="skipReason">Fachliche Begründung für das Überspringen.</param>
    /// <param name="skipUsageSummary">Optional bereits aufgelöste Nutzungsübersicht für die GUI.</param>
    /// <param name="notes">Zusätzliche Hinweise für UI und Vorschau.</param>
    /// <returns>Entscheidung für einen No-Op-Lauf.</returns>
    public static ArchiveIntegrationDecision CreateSkip(
        string outputPath,
        string skipReason,
        EpisodeUsageSummary? skipUsageSummary,
        IReadOnlyList<string> notes)
    {
        return new ArchiveIntegrationDecision(
            outputPath,
            SkipMux: true,
            SkipReason: skipReason,
            SkipUsageSummary: skipUsageSummary,
            WorkingCopy: null,
            PrimarySourcePath: string.Empty,
            VideoSelections: [],
            RetainedAudioTrackIds: null,
            PrimarySubtitleTrackIds: null,
            PrimarySourceAttachmentIds: null,
            IncludePrimaryAttachments: false,
            AttachmentSourcePath: null,
            AttachmentSourceAttachmentIds: null,
            AdditionalVideoPaths: [],
            AudioDescriptionFilePath: null,
            AudioDescriptionTrackId: null,
            SubtitleFiles: [],
            AttachmentFilePaths: [],
            FallbackToRequestAttachments: false,
            PreservedAttachmentNames: [],
            UsageComparison: ArchiveUsageComparison.Empty,
            Notes: notes);
    }
}

