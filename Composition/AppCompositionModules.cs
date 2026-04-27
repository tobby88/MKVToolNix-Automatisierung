using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Emby;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Composition;

/// <summary>
/// Bündelt alle persistenten Einstellungs-Stores der App, damit Kompositionsmodule dieselbe Konfiguration konsistent weiterreichen.
/// </summary>
/// <param name="Settings">Zentraler kombinierter Settings-Store der portablen Anwendung.</param>
/// <param name="ToolPaths">Teilstore für Pfade zu mkvmerge und ffprobe.</param>
/// <param name="Archive">Teilstore für die Standard-Serienbibliothek.</param>
/// <param name="Metadata">Teilstore für TVDB-Zugangsdaten und Mappings.</param>
/// <param name="Emby">Teilstore für Emby-Adresse und API-Key.</param>
internal sealed record AppSettingStores(
    AppSettingsStore Settings,
    AppToolPathStore ToolPaths,
    AppArchiveSettingsStore Archive,
    AppMetadataStore Metadata,
    AppEmbySettingsStore Emby);

/// <summary>
/// Bündelt Tool-Locatoren und Probe-Services, die für Erkennung und Toolstatus gemeinsam benötigt werden.
/// </summary>
/// <param name="MkvToolNixLocator">Locator für die aktuelle <c>mkvmerge.exe</c>.</param>
/// <param name="FfprobeLocator">Locator für die aktuelle <c>ffprobe.exe</c>.</param>
/// <param name="Probe">Gemeinsamer mkvmerge-Probe-Service für Track- und Metadatenabfragen.</param>
internal sealed record ToolingServices(
    MkvToolNixLocator MkvToolNixLocator,
    FfprobeLocator FfprobeLocator,
    MkvMergeProbeService Probe);

/// <summary>
/// Bündelt die TVDB- und Metadaten-Services der Anwendung.
/// </summary>
/// <param name="Lookup">Fassade für automatische Metadatenauflösung und manuelle TVDB-Nacharbeit.</param>
internal sealed record MetadataServices(EpisodeMetadataLookupService Lookup);

/// <summary>
/// Bündelt die fachlichen Mux- und Archivservices, die Planung, Scan und Ausgabezielermittlung gemeinsam nutzen.
/// </summary>
/// <param name="Mux">Fassade für Detection, Planerzeugung und mkvmerge-Ausführung.</param>
/// <param name="EpisodePlans">Koordinator für Planerzeugung aus UI-Eingaben.</param>
/// <param name="BatchScan">Batch-spezifische Scan- und Gruppierungslogik.</param>
/// <param name="DetectionWorkflow">Gemeinsamer Workflow für Dateierkennung und automatische Metadatenauflösung.</param>
/// <param name="Archive">Archivdienst für Bibliotheksintegration und Wiederverwendungsentscheidungen.</param>
/// <param name="OutputPaths">Dienst für automatische Zielpfadbildung und Archivziel-Erkennung.</param>
/// <param name="CleanupFiles">Planer für Cleanup- und Done-Dateilisten.</param>
internal sealed record MuxDomainServices(
    SeriesEpisodeMuxService Mux,
    EpisodePlanCoordinator EpisodePlans,
    BatchScanCoordinator BatchScan,
    EpisodeDetectionWorkflow DetectionWorkflow,
    SeriesArchiveService Archive,
    EpisodeOutputPathService OutputPaths,
    EpisodeCleanupFilePlanner CleanupFiles);

/// <summary>
/// Bündelt die ausführungsnahen Workflow-Services für Arbeitskopien, Cleanup und Batch-Logging.
/// </summary>
/// <param name="FileCopy">Dateikopierdienst für Arbeitskopien.</param>
/// <param name="Cleanup">Dienst für Papierkorb-/Move-Aufräumlogik.</param>
/// <param name="MuxWorkflow">Koordinator für Arbeitskopie, Mux und temporäre Nachbereitung.</param>
/// <param name="BatchLogs">Persistenzdienst für Batch-Protokolle und Dateilisten.</param>
/// <param name="DownloadSort">Dienst für das Einsortieren loser MediathekView-Downloads.</param>
/// <param name="EmbyMetadataSync">Dienst für den Emby-/NFO-Provider-ID-Abgleich nach neuen Mux-Läufen.</param>
internal sealed record WorkflowServices(
    IFileCopyService FileCopy,
    IEpisodeCleanupService Cleanup,
    IMuxWorkflowCoordinator MuxWorkflow,
    BatchRunLogService BatchLogs,
    DownloadSortService DownloadSort,
    EmbyMetadataSyncService EmbyMetadataSync);
