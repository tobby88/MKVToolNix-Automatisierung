using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Bündelt die fachlichen Kernservices, die Einzelmodus und Batch gemeinsam für Erkennung, Planung und Ausgabepfade nutzen.
/// </summary>
internal sealed record SharedEpisodeModuleServices(
    SeriesEpisodeMuxService SeriesEpisodeMux,
    EpisodePlanCoordinator EpisodePlans,
    EpisodeOutputPathService OutputPaths,
    EpisodeCleanupFilePlanner CleanupFiles,
    EpisodeMetadataLookupService EpisodeMetadata);

/// <summary>
/// Bündelt nur die Services, die der Einzelmodus tatsächlich für Erkennung, Vorschau und Mux-Ausführung benötigt.
/// </summary>
internal sealed record SingleEpisodeModuleServices(
    SharedEpisodeModuleServices Shared,
    IEpisodeCleanupService Cleanup,
    IMuxWorkflowCoordinator MuxWorkflow)
{
    public SeriesEpisodeMuxService SeriesEpisodeMux => Shared.SeriesEpisodeMux;

    public EpisodePlanCoordinator EpisodePlans => Shared.EpisodePlans;

    public EpisodeOutputPathService OutputPaths => Shared.OutputPaths;

    public EpisodeCleanupFilePlanner CleanupFiles => Shared.CleanupFiles;

    public EpisodeMetadataLookupService EpisodeMetadata => Shared.EpisodeMetadata;
}

/// <summary>
/// Bündelt die batchspezifischen Services inklusive Scan, Batch-Workflow und Logging.
/// </summary>
internal sealed record BatchModuleServices(
    SharedEpisodeModuleServices Shared,
    BatchScanCoordinator BatchScan,
    SeriesArchiveService Archive,
    IFileCopyService FileCopy,
    IEpisodeCleanupService Cleanup,
    IMuxWorkflowCoordinator MuxWorkflow,
    BatchRunLogService BatchLogs)
{
    public EpisodePlanCoordinator EpisodePlans => Shared.EpisodePlans;

    public EpisodeOutputPathService OutputPaths => Shared.OutputPaths;

    public EpisodeCleanupFilePlanner CleanupFiles => Shared.CleanupFiles;

    public EpisodeMetadataLookupService EpisodeMetadata => Shared.EpisodeMetadata;
}

/// <summary>
/// Bündelt nur die globalen Shell-Services des Hauptfensters für Toolstatus und Archivkonfiguration.
/// </summary>
internal sealed record MainWindowModuleServices(
    SeriesArchiveService Archive,
    AppToolPathStore ToolPaths,
    IFfprobeLocator FfprobeLocator,
    IMkvToolNixLocator MkvToolNixLocator);
