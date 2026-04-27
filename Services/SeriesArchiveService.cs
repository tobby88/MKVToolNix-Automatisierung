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

    private static readonly TimeSpan AudioDescriptionDurationProbeTimeout = TimeSpan.FromSeconds(5);
    private readonly MkvMergeProbeService _probeService;
    private readonly AppArchiveSettingsStore _archiveSettingsStore;
    private readonly FfprobeDurationProbe? _ffprobeDurationProbe;
    private bool _hasValidArchiveRootConfiguration;

    /// <summary>
    /// Initialisiert den Archivdienst für Pfadvorschläge und bestehende Ziel-MKV-Integration.
    /// </summary>
    /// <param name="probeService">Liest Track- und Container-Metadaten vorhandener Archivdateien.</param>
    /// <param name="archiveSettingsStore">Persistenter Store für den konfigurierten Archivwurzelpfad.</param>
    /// <param name="ffprobeDurationProbe">Optionaler präziser Laufzeit-Fallback für Quellen, deren Trackdauer <c>mkvmerge</c> nicht meldet.</param>
    public SeriesArchiveService(
        MkvMergeProbeService probeService,
        AppArchiveSettingsStore archiveSettingsStore,
        FfprobeDurationProbe? ffprobeDurationProbe = null)
    {
        _probeService = probeService;
        _archiveSettingsStore = archiveSettingsStore;
        _ffprobeDurationProbe = ffprobeDurationProbe;
        var archiveRootState = ResolveLoadedArchiveRootDirectory(_archiveSettingsStore.Load().DefaultSeriesArchiveRootPath);
        ArchiveRootDirectory = archiveRootState.Path;
        _hasValidArchiveRootConfiguration = archiveRootState.IsValid;
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
        EnsureArchiveRootConfigurationIsValid();
        var normalizedSeasonNumber = EpisodeFileNameHelper.NormalizeSeasonNumber(seasonNumber);
        var seasonFolderName = normalizedSeasonNumber == "00"
            ? "Specials"
            : int.TryParse(normalizedSeasonNumber, out var parsedSeason) && parsedSeason > 0
                ? $"Season {parsedSeason}"
                : "Season xx";

        var targetDirectory = Path.Combine(
            ArchiveRootDirectory,
            EpisodeFileNameHelper.SanitizePathSegment(seriesName),
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
        return _hasValidArchiveRootConfiguration
            && PathComparisonHelper.IsPathWithinRoot(outputFilePath, ArchiveRootDirectory);
    }

    /// <summary>
    /// Speichert einen neuen Standardpfad für die Serienbibliothek dauerhaft in den App-Einstellungen.
    /// </summary>
    /// <param name="archiveRootDirectory">Neuer Bibliothekswurzelpfad.</param>
    public void ConfigureArchiveRootDirectory(string archiveRootDirectory)
    {
        var normalizedPath = NormalizeArchiveRootDirectoryForSettings(archiveRootDirectory);
        _archiveSettingsStore.Save(new AppArchiveSettings
        {
            DefaultSeriesArchiveRootPath = normalizedPath
        });
        ApplyArchiveRootDirectoryForCurrentSession(normalizedPath);
    }

    /// <summary>
    /// Aktualisiert nur den laufenden Archivdienst, nachdem ein anderer Codepfad die Settings bereits atomar gespeichert hat.
    /// </summary>
    /// <param name="archiveRootDirectory">Bereits gespeicherter oder zu normalisierender Archivwurzelpfad.</param>
    internal void ApplyArchiveRootDirectoryForCurrentSession(string archiveRootDirectory)
    {
        ArchiveRootDirectory = NormalizeArchiveRootDirectoryForSettings(archiveRootDirectory);
        _hasValidArchiveRootConfiguration = true;
    }

    /// <summary>
    /// Prüft, ob die konfigurierte Serienbibliothek aktuell erreichbar ist.
    /// </summary>
    /// <returns><see langword="true"/>, wenn das Zielverzeichnis existiert.</returns>
    public bool IsArchiveAvailable()
    {
        return _hasValidArchiveRootConfiguration && Directory.Exists(ArchiveRootDirectory);
    }

    /// <summary>
    /// Erzeugt den UI-Hinweistext für den Fall, dass die Bibliothek aktuell nicht erreichbar ist.
    /// </summary>
    /// <returns>Mehrzeilige Warnmeldung mit dem konfigurierten Bibliothekspfad.</returns>
    public string BuildArchiveUnavailableWarningMessage()
    {
        return (_hasValidArchiveRootConfiguration
                ? "Die konfigurierte Serienbibliothek ist aktuell nicht erreichbar:"
                : "Die konfigurierte Serienbibliothek ist ungültig:")
            + Environment.NewLine
            + ArchiveRootDirectory
            + Environment.NewLine
            + Environment.NewLine
            + (_hasValidArchiveRootConfiguration
                ? "Automatische Ausgabepfade verwenden deshalb vorerst den jeweiligen Quellordner."
                : "Bitte den Archivpfad in den Einstellungen korrigieren. Automatische Ausgabepfade verwenden deshalb vorerst den jeweiligen Quellordner.");
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

        if (TryNormalizeArchiveRootDirectory(archiveRootDirectory, out var normalizedPath))
        {
            return normalizedPath;
        }

        throw new ArgumentException("Der konfigurierte Archivwurzelpfad ist ungültig.", nameof(archiveRootDirectory));
    }

    /// <summary>
    /// Normalisiert einen Archivwurzelpfad für persistente Einstellungen und UI-Speicherung.
    /// </summary>
    internal static string NormalizeArchiveRootDirectoryForSettings(string? archiveRootDirectory)
    {
        return NormalizeArchiveRootDirectory(archiveRootDirectory);
    }

    /// <summary>
    /// Lädt einen bereits gespeicherten Archivpfad robust, ohne bei Altlasten aus der Konfiguration
    /// den kompletten App-Start zu blockieren.
    /// </summary>
    private static (string Path, bool IsValid) ResolveLoadedArchiveRootDirectory(string? archiveRootDirectory)
    {
        if (string.IsNullOrWhiteSpace(archiveRootDirectory))
        {
            return (DefaultArchiveRootDirectory, true);
        }

        return TryNormalizeArchiveRootDirectory(archiveRootDirectory, out var normalizedPath)
            ? (normalizedPath, true)
            : (archiveRootDirectory.Trim(), false);
    }

    /// <summary>
    /// Versucht, einen expliziten Archivwurzelpfad in eine vergleichbare Vollform zu überführen.
    /// </summary>
    private static bool TryNormalizeArchiveRootDirectory(string archiveRootDirectory, out string normalizedPath)
    {
        try
        {
            var fullPath = Path.GetFullPath(archiveRootDirectory.Trim());
            var rootPath = Path.GetPathRoot(fullPath);
            normalizedPath = string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return true;
        }
        catch
        {
            normalizedPath = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// Verhindert, dass eine bereits als ungültig erkannte Archivkonfiguration stillschweigend
    /// für neue Bibliothekspfade verwendet wird.
    /// </summary>
    private void EnsureArchiveRootConfigurationIsValid()
    {
        if (!_hasValidArchiveRootConfiguration)
        {
            throw new InvalidOperationException(
                $"Der konfigurierte Archivwurzelpfad ist ungültig und muss zuerst korrigiert werden: {ArchiveRootDirectory}");
        }
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
    ContainerTitleEditOperation? ContainerTitleEdit,
    IReadOnlyList<TrackHeaderEditOperation> TrackHeaderEdits,
    IReadOnlyList<string> Notes)
{
    /// <summary>
    /// Bereits vollständig aufgelöste AD-Spuren aus einer vorhandenen Archivdatei.
    /// Bleibt leer, wenn eine frische AD-Datei verwendet oder keine AD übernommen wird.
    /// </summary>
    public IReadOnlyList<AudioDescriptionSourcePlan> AudioDescriptionSources { get; init; } = [];

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
            ContainerTitleEdit: null,
            TrackHeaderEdits: [],
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
            ContainerTitleEdit: null,
            TrackHeaderEdits: [],
            Notes: notes);
    }
}

