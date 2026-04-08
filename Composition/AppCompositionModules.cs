using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Composition;

/// <summary>
/// Bündelt alle persistenten Einstellungs-Stores der App, damit Kompositionsmodule dieselbe Konfiguration konsistent weiterreichen.
/// </summary>
internal sealed record AppSettingStores(
    AppSettingsStore Settings,
    AppToolPathStore ToolPaths,
    AppArchiveSettingsStore Archive,
    AppMetadataStore Metadata);

/// <summary>
/// Bündelt Tool-Locatoren und Probe-Services, die für Erkennung und Toolstatus gemeinsam benötigt werden.
/// </summary>
internal sealed record ToolingServices(
    MkvToolNixLocator MkvToolNixLocator,
    FfprobeLocator FfprobeLocator,
    MkvMergeProbeService Probe);

/// <summary>
/// Bündelt die TVDB- und Metadaten-Services der Anwendung.
/// </summary>
internal sealed record MetadataServices(EpisodeMetadataLookupService Lookup);

/// <summary>
/// Bündelt die fachlichen Mux- und Archivservices, die Planung, Scan und Ausgabezielermittlung gemeinsam nutzen.
/// </summary>
internal sealed record MuxDomainServices(
    SeriesEpisodeMuxService Mux,
    EpisodePlanCoordinator EpisodePlans,
    BatchScanCoordinator BatchScan,
    SeriesArchiveService Archive,
    EpisodeOutputPathService OutputPaths,
    EpisodeCleanupFilePlanner CleanupFiles);

/// <summary>
/// Bündelt die ausführungsnahen Workflow-Services für Arbeitskopien, Cleanup und Batch-Logging.
/// </summary>
internal sealed record WorkflowServices(
    IFileCopyService FileCopy,
    IEpisodeCleanupService Cleanup,
    IMuxWorkflowCoordinator MuxWorkflow,
    BatchRunLogService BatchLogs);
