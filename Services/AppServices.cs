using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Services;

public sealed record AppServices(
    SeriesEpisodeMuxService SeriesEpisodeMux,
    SeriesArchiveService Archive,
    EpisodeOutputPathService OutputPaths,
    EpisodeCleanupFilePlanner CleanupFiles,
    EpisodeMetadataLookupService EpisodeMetadata,
    FileCopyService FileCopy,
    EpisodeCleanupService Cleanup,
    MuxWorkflowCoordinator MuxWorkflow);
