using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Composition;

/// <summary>
/// Verdrahtet Mux-Planung, Archivlogik und Batch-Scan als gemeinsames fachliches Kernmodul.
/// </summary>
internal static class MuxCompositionModule
{
    /// <summary>
    /// Erstellt alle Kernservices rund um Erkennung, Archivvergleich und Mux-Planung.
    /// </summary>
    public static MuxDomainServices Create(
        AppSettingStores stores,
        ToolingServices tooling,
        MetadataServices metadata)
    {
        var archiveService = new SeriesArchiveService(tooling.Probe, stores.Archive);
        var outputPathService = new EpisodeOutputPathService(archiveService);
        var ffprobeDurationProbe = new FfprobeDurationProbe(tooling.FfprobeLocator);
        var durationProbe = new PreferredMediaDurationProbe(
            ffprobeDurationProbe,
            new WindowsMediaDurationProbe());
        var planner = new SeriesEpisodeMuxPlanner(
            tooling.MkvToolNixLocator,
            tooling.Probe,
            archiveService,
            durationProbe);
        var muxService = new SeriesEpisodeMuxService(
            planner,
            new MuxExecutionService(),
            new MkvMergeOutputParser());

        return new MuxDomainServices(
            muxService,
            new EpisodePlanCoordinator(muxService),
            new BatchScanCoordinator(
                muxService,
                metadata.Lookup,
                outputPathService),
            archiveService,
            outputPathService,
            new EpisodeCleanupFilePlanner(outputPathService));
    }
}
