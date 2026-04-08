using Microsoft.Extensions.DependencyInjection;
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
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<SeriesArchiveService>(provider => new SeriesArchiveService(
            provider.GetRequiredService<MkvMergeProbeService>(),
            provider.GetRequiredService<AppArchiveSettingsStore>()));
        services.AddSingleton<EpisodeOutputPathService>(provider => new EpisodeOutputPathService(provider.GetRequiredService<SeriesArchiveService>()));
        services.AddSingleton<FfprobeDurationProbe>(provider => new FfprobeDurationProbe(provider.GetRequiredService<FfprobeLocator>()));
        services.AddSingleton<IMediaDurationProbe>(provider => new PreferredMediaDurationProbe(
            provider.GetRequiredService<FfprobeDurationProbe>(),
            new WindowsMediaDurationProbe()));
        services.AddSingleton<SeriesEpisodeMuxPlanner>(provider => new SeriesEpisodeMuxPlanner(
            provider.GetRequiredService<MkvToolNixLocator>(),
            provider.GetRequiredService<MkvMergeProbeService>(),
            provider.GetRequiredService<SeriesArchiveService>(),
            provider.GetRequiredService<IMediaDurationProbe>()));
        services.AddSingleton<SeriesEpisodeMuxService>(provider => new SeriesEpisodeMuxService(
            provider.GetRequiredService<SeriesEpisodeMuxPlanner>(),
            new MuxExecutionService(),
            new MkvMergeOutputParser()));
        services.AddSingleton<EpisodePlanCoordinator>(provider => new EpisodePlanCoordinator(provider.GetRequiredService<SeriesEpisodeMuxService>()));
        services.AddSingleton<BatchScanCoordinator>(provider => new BatchScanCoordinator(
            provider.GetRequiredService<SeriesEpisodeMuxService>(),
            provider.GetRequiredService<EpisodeMetadataLookupService>(),
            provider.GetRequiredService<EpisodeOutputPathService>()));
        services.AddSingleton<EpisodeCleanupFilePlanner>(provider => new EpisodeCleanupFilePlanner(provider.GetRequiredService<EpisodeOutputPathService>()));
        services.AddSingleton<MuxDomainServices>(provider => new MuxDomainServices(
            provider.GetRequiredService<SeriesEpisodeMuxService>(),
            provider.GetRequiredService<EpisodePlanCoordinator>(),
            provider.GetRequiredService<BatchScanCoordinator>(),
            provider.GetRequiredService<SeriesArchiveService>(),
            provider.GetRequiredService<EpisodeOutputPathService>(),
            provider.GetRequiredService<EpisodeCleanupFilePlanner>()));
    }
}
