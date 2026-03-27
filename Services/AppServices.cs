using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Bündelt die gemeinsam genutzten Fachservices, damit ViewModels mit einer stabilen Service-Oberfläche arbeiten können.
/// </summary>
public sealed record AppServices(
    SeriesEpisodeMuxService SeriesEpisodeMux,
    EpisodePlanCoordinator EpisodePlans,
    BatchScanCoordinator BatchScan,
    SeriesArchiveService Archive,
    EpisodeOutputPathService OutputPaths,
    EpisodeCleanupFilePlanner CleanupFiles,
    EpisodeMetadataLookupService EpisodeMetadata,
    FileCopyService FileCopy,
    EpisodeCleanupService Cleanup,
    MuxWorkflowCoordinator MuxWorkflow,
    BatchRunLogService BatchLogs);
