using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services.Emby;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Bündelt die fachlichen Kernservices, die Einzelmodus und Batch gemeinsam für Erkennung, Planung und Ausgabepfade nutzen.
/// </summary>
/// <param name="SeriesEpisodeMux">Fassade für Detection, Vorschau und Mux-Ausführung.</param>
/// <param name="EpisodePlans">Koordinator für Mux-Pläne aus UI-Eingaben.</param>
/// <param name="OutputPaths">Dienst für Ausgabezielbildung und Archivziel-Erkennung.</param>
/// <param name="CleanupFiles">Planer für Cleanup-Dateilisten nach Einzel- oder Batch-Läufen.</param>
/// <param name="EpisodeMetadata">Metadatenservice für TVDB-Auflösung und Review-Automatik.</param>
/// <param name="DetectionWorkflow">Gemeinsamer Workflow für Dateierkennung und automatische Metadatenauflösung.</param>
internal sealed record SharedEpisodeModuleServices(
    SeriesEpisodeMuxService SeriesEpisodeMux,
    EpisodePlanCoordinator EpisodePlans,
    EpisodeOutputPathService OutputPaths,
    EpisodeCleanupFilePlanner CleanupFiles,
    EpisodeMetadataLookupService EpisodeMetadata,
    EpisodeDetectionWorkflow DetectionWorkflow);

/// <summary>
/// Bündelt nur die Services, die der Einzelmodus tatsächlich für Erkennung, Vorschau und Mux-Ausführung benötigt.
/// </summary>
/// <param name="Shared">Gemeinsame Kernservices des Episoden-Workflows.</param>
/// <param name="Cleanup">Cleanup-Dienst für Papierkorb-Operationen nach Einzelmuxing.</param>
/// <param name="MuxWorkflow">Koordinator für Arbeitskopie und echten Mux-Lauf im Einzelmodus.</param>
/// <param name="BatchLogs">Persistenzdienst für Log- und Metadatenartefakte neu erzeugter Ausgabedateien.</param>
internal sealed record SingleEpisodeModuleServices(
    SharedEpisodeModuleServices Shared,
    IEpisodeCleanupService Cleanup,
    IMuxWorkflowCoordinator MuxWorkflow,
    BatchRunLogService BatchLogs)
{
    /// <summary>
    /// Kurzgriff auf die Mux-Fassade des gemeinsam genutzten Service-Bundles.
    /// </summary>
    public SeriesEpisodeMuxService SeriesEpisodeMux => Shared.SeriesEpisodeMux;

    /// <summary>
    /// Kurzgriff auf die Planerzeugung des gemeinsam genutzten Service-Bundles.
    /// </summary>
    public EpisodePlanCoordinator EpisodePlans => Shared.EpisodePlans;

    /// <summary>
    /// Kurzgriff auf die automatische Ausgabezielbildung des gemeinsam genutzten Service-Bundles.
    /// </summary>
    public EpisodeOutputPathService OutputPaths => Shared.OutputPaths;

    /// <summary>
    /// Kurzgriff auf die Cleanup-Dateiplanung des gemeinsam genutzten Service-Bundles.
    /// </summary>
    public EpisodeCleanupFilePlanner CleanupFiles => Shared.CleanupFiles;

    /// <summary>
    /// Kurzgriff auf TVDB-/Metadatenlogik des gemeinsam genutzten Service-Bundles.
    /// </summary>
    public EpisodeMetadataLookupService EpisodeMetadata => Shared.EpisodeMetadata;

    /// <summary>
    /// Kurzgriff auf den gemeinsamen Detection-/Metadaten-Workflow.
    /// </summary>
    public EpisodeDetectionWorkflow DetectionWorkflow => Shared.DetectionWorkflow;
}

/// <summary>
/// Bündelt die batchspezifischen Services inklusive Scan, Batch-Workflow und Logging.
/// </summary>
/// <param name="Shared">Gemeinsame Kernservices des Episoden-Workflows.</param>
/// <param name="BatchScan">Batch-Scan- und Gruppierungsdienst.</param>
/// <param name="Archive">Archivdienst für Zielstatus und Bibliotheksintegration.</param>
/// <param name="FileCopy">Dateikopierdienst für Arbeitskopien im Batch.</param>
/// <param name="Cleanup">Cleanup-Dienst für Done-/Papierkorb-Operationen im Batch.</param>
/// <param name="MuxWorkflow">Koordinator für Arbeitskopie und Mux-Ausführung im Batch.</param>
/// <param name="BatchLogs">Persistenzdienst für Batch-Protokolle und neue Ausgabedateien.</param>
internal sealed record BatchModuleServices(
    SharedEpisodeModuleServices Shared,
    BatchScanCoordinator BatchScan,
    SeriesArchiveService Archive,
    IFileCopyService FileCopy,
    IEpisodeCleanupService Cleanup,
    IMuxWorkflowCoordinator MuxWorkflow,
    BatchRunLogService BatchLogs)
{
    /// <summary>
    /// Kurzgriff auf die Planerzeugung des gemeinsam genutzten Service-Bundles.
    /// </summary>
    public EpisodePlanCoordinator EpisodePlans => Shared.EpisodePlans;

    /// <summary>
    /// Kurzgriff auf automatische Ausgabezielbildung und Archivziel-Erkennung.
    /// </summary>
    public EpisodeOutputPathService OutputPaths => Shared.OutputPaths;

    /// <summary>
    /// Kurzgriff auf Cleanup-Dateiplanung für Scan- und Batch-Ergebnisse.
    /// </summary>
    public EpisodeCleanupFilePlanner CleanupFiles => Shared.CleanupFiles;

    /// <summary>
    /// Kurzgriff auf TVDB-/Metadatenlogik des gemeinsam genutzten Service-Bundles.
    /// </summary>
    public EpisodeMetadataLookupService EpisodeMetadata => Shared.EpisodeMetadata;

    /// <summary>
    /// Kurzgriff auf den gemeinsamen Detection-/Metadaten-Workflow.
    /// </summary>
    public EpisodeDetectionWorkflow DetectionWorkflow => Shared.DetectionWorkflow;
}

/// <summary>
/// Bündelt die Services für die nachgelagerte Pflege bereits vorhandener Archiv-MKV-Dateien.
/// </summary>
/// <param name="ArchiveMaintenance">Fachservice für Scan, Headeranalyse und freigegebene Korrekturen.</param>
/// <param name="ArchiveSettings">Persistenter Store für die Serienbibliothek als Startordner.</param>
/// <param name="EpisodeMetadata">TVDB-Such- und Mappinglogik für manuelle Korrekturen einzelner Archivdateien.</param>
/// <param name="ImdbLookup">Optionaler IMDb-Provider über die freie REST-API.</param>
/// <param name="SettingsDialog">Zentraler Einstellungsdialog für TVDB-Zugangsdaten und gemeinsame App-Konfiguration.</param>
internal sealed record ArchiveMaintenanceModuleServices(
    ArchiveMaintenanceService ArchiveMaintenance,
    AppArchiveSettingsStore ArchiveSettings,
    EpisodeMetadataLookupService EpisodeMetadata,
    ImdbLookupService ImdbLookup,
    IAppSettingsDialogService SettingsDialog);

/// <summary>
/// Bündelt nur die für das Einsortieren loser MediathekView-Downloads benötigten Services.
/// </summary>
/// <param name="DownloadSort">Fachservice für Scan, Ordnervereinheitlichung und Verschiebungen.</param>
internal sealed record DownloadSortModuleServices(
    DownloadSortService DownloadSort);

/// <summary>
/// Bündelt den ersten Workflow-Schritt rund um MediathekView-Downloads.
/// </summary>
/// <param name="MediathekView">Starter für die installierte oder portable MediathekView-Anwendung.</param>
/// <param name="SettingsDialog">Zentraler Einstellungsdialog für den optionalen MediathekView-Pfad.</param>
internal sealed record DownloadModuleServices(
    IMediathekViewLauncher MediathekView,
    IAppSettingsDialogService SettingsDialog);

/// <summary>
/// Bündelt nur die Services für den nachgelagerten Emby-/NFO-Abgleich.
/// </summary>
/// <param name="Settings">Persistenter Store für Emby-Adresse und API-Key.</param>
/// <param name="ArchiveSettings">Persistenter Store für den Standardpfad der Serienbibliothek.</param>
/// <param name="Sync">Fachservice für JSON-Metadatenreport-Import, NFO-Provider-IDs und Emby-API-Aktionen.</param>
/// <param name="EpisodeMetadata">TVDB-Such- und Mappinglogik für manuelle Korrekturen einzelner Emby-Zeilen.</param>
/// <param name="ImdbLookup">Optionaler IMDb-Provider über die freie REST-API.</param>
/// <param name="SettingsDialog">Zentraler Einstellungsdialog für Emby-Zugangsdaten und gemeinsame App-Konfiguration.</param>
internal sealed record EmbyModuleServices(
    AppEmbySettingsStore Settings,
    AppArchiveSettingsStore ArchiveSettings,
    EmbyMetadataSyncService Sync,
    EpisodeMetadataLookupService EpisodeMetadata,
    ImdbLookupService ImdbLookup,
    IAppSettingsDialogService SettingsDialog);

/// <summary>
/// Bündelt die Stores und Infrastruktur für den zentralen Einstellungsdialog.
/// </summary>
/// <param name="Settings">Gemeinsamer Settings-Store, damit mehrere Einstellungsbereiche atomar gespeichert werden.</param>
/// <param name="Archive">Archivdienst für die live wirksame Serienwurzel.</param>
/// <param name="ToolPaths">Persistenter Store für ffprobe- und MKVToolNix-Pfade.</param>
/// <param name="EpisodeMetadata">TVDB-Settings- und Mappingstore samt Lookup-Logik.</param>
/// <param name="EmbySettings">Persistenter Store für Emby-Serveradresse und API-Key.</param>
/// <param name="EmbySync">Fachservice für Emby-Verbindungstest und nachgelagerten NFO-/Refresh-Abgleich.</param>
/// <param name="ManagedTools">Installer für direkt nach dem Speichern aktivierte automatische Werkzeugverwaltung.</param>
internal sealed record AppSettingsModuleServices(
    AppSettingsStore Settings,
    SeriesArchiveService Archive,
    AppToolPathStore ToolPaths,
    EpisodeMetadataLookupService EpisodeMetadata,
    AppEmbySettingsStore EmbySettings,
    EmbyMetadataSyncService EmbySync,
    IManagedToolInstallerService ManagedTools);

/// <summary>
/// Bündelt nur die globalen Shell-Services des Hauptfensters für Toolstatus und Archivkonfiguration.
/// </summary>
/// <param name="Archive">Archivdienst für globale Bibliothekskonfiguration und Verfügbarkeitsstatus.</param>
/// <param name="ToolPaths">Persistenter Store für MKVToolNix- und ffprobe-Pfade.</param>
/// <param name="FfprobeLocator">Locator für die aktuell nutzbare <c>ffprobe.exe</c>.</param>
/// <param name="MkvToolNixLocator">Locator für die aktuell nutzbare <c>mkvmerge.exe</c>.</param>
/// <param name="SettingsDialog">Zentraler Einstellungsdialog für selten geänderte App-Konfiguration.</param>
internal sealed record MainWindowModuleServices(
    SeriesArchiveService Archive,
    AppToolPathStore ToolPaths,
    IFfprobeLocator FfprobeLocator,
    IMkvToolNixLocator MkvToolNixLocator,
    IAppSettingsDialogService SettingsDialog);
