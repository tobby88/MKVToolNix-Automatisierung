using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Composition;

/// <summary>
/// Verdrahtet Mux-Planung, Archivlogik und Batch-Scan als gemeinsames fachliches Kernmodul.
/// </summary>
internal static class MuxCompositionModule
{
    /// <summary>
    /// Registriert alle Kernservices rund um Erkennung, Archivvergleich und Mux-Planung.
    /// </summary>
    public static void Register(AppServiceRegistry services)
    {
        services.AddSingleton<SeriesArchiveService>(provider => new SeriesArchiveService(
            provider.GetRequired<MkvMergeProbeService>(),
            provider.GetRequired<AppArchiveSettingsStore>()));
        services.AddSingleton<EpisodeOutputPathService>(provider => new EpisodeOutputPathService(provider.GetRequired<SeriesArchiveService>()));
        services.AddSingleton<FfprobeDurationProbe>(provider => new FfprobeDurationProbe(provider.GetRequired<FfprobeLocator>()));
        services.AddSingleton<IMediaDurationProbe>(provider => new PreferredMediaDurationProbe(
            provider.GetRequired<FfprobeDurationProbe>(),
            new WindowsMediaDurationProbe()));
        services.AddSingleton<SeriesEpisodeMuxPlanner>(provider => new SeriesEpisodeMuxPlanner(
            provider.GetRequired<MkvToolNixLocator>(),
            provider.GetRequired<MkvMergeProbeService>(),
            provider.GetRequired<SeriesArchiveService>(),
            provider.GetRequired<IMediaDurationProbe>()));
        services.AddSingleton<SeriesEpisodeMuxService>(provider => new SeriesEpisodeMuxService(
            provider.GetRequired<SeriesEpisodeMuxPlanner>(),
            new MuxExecutionService(),
            new MkvMergeOutputParser()));
        services.AddSingleton<EpisodePlanCoordinator>(provider => new EpisodePlanCoordinator(provider.GetRequired<SeriesEpisodeMuxService>()));
        services.AddSingleton<BatchScanCoordinator>(provider => new BatchScanCoordinator(
            provider.GetRequired<SeriesEpisodeMuxService>(),
            provider.GetRequired<EpisodeMetadataLookupService>(),
            provider.GetRequired<EpisodeOutputPathService>()));
        services.AddSingleton<EpisodeCleanupFilePlanner>(provider => new EpisodeCleanupFilePlanner(provider.GetRequired<EpisodeOutputPathService>()));
        services.AddSingleton<MuxDomainServices>(provider => new MuxDomainServices(
            provider.GetRequired<SeriesEpisodeMuxService>(),
            provider.GetRequired<EpisodePlanCoordinator>(),
            provider.GetRequired<BatchScanCoordinator>(),
            provider.GetRequired<SeriesArchiveService>(),
            provider.GetRequired<EpisodeOutputPathService>(),
            provider.GetRequired<EpisodeCleanupFilePlanner>()));
    }
}
