using Microsoft.Extensions.DependencyInjection;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Composition;

/// <summary>
/// Verdrahtet die ausführungsnahen Services für Arbeitskopien, Cleanup und Batch-Logs.
/// </summary>
internal static class WorkflowCompositionModule
{
    /// <summary>
    /// Registriert die Workflow-Services für Mux-Ausführung und Nachbereitung.
    /// </summary>
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<FileCopyService>(_ => new FileCopyService());
        services.AddSingleton<IFileCopyService>(provider => provider.GetRequiredService<FileCopyService>());
        services.AddSingleton<EpisodeCleanupService>(_ => new EpisodeCleanupService());
        services.AddSingleton<IEpisodeCleanupService>(provider => provider.GetRequiredService<EpisodeCleanupService>());
        services.AddSingleton<MuxWorkflowCoordinator>(provider => new MuxWorkflowCoordinator(
            provider.GetRequiredService<SeriesEpisodeMuxService>(),
            provider.GetRequiredService<IFileCopyService>(),
            provider.GetRequiredService<IEpisodeCleanupService>()));
        services.AddSingleton<IMuxWorkflowCoordinator>(provider => provider.GetRequiredService<MuxWorkflowCoordinator>());
        services.AddSingleton<BatchRunLogService>(_ => new BatchRunLogService());
        services.AddSingleton<WorkflowServices>(provider => new WorkflowServices(
            provider.GetRequiredService<IFileCopyService>(),
            provider.GetRequiredService<IEpisodeCleanupService>(),
            provider.GetRequiredService<IMuxWorkflowCoordinator>(),
            provider.GetRequiredService<BatchRunLogService>()));
    }
}
