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
    public static void Register(AppServiceRegistry services)
    {
        services.AddSingleton<FileCopyService>(_ => new FileCopyService());
        services.AddSingleton<IFileCopyService>(provider => provider.GetRequired<FileCopyService>());
        services.AddSingleton<EpisodeCleanupService>(_ => new EpisodeCleanupService());
        services.AddSingleton<IEpisodeCleanupService>(provider => provider.GetRequired<EpisodeCleanupService>());
        services.AddSingleton<MuxWorkflowCoordinator>(provider => new MuxWorkflowCoordinator(
            provider.GetRequired<SeriesEpisodeMuxService>(),
            provider.GetRequired<IFileCopyService>(),
            provider.GetRequired<IEpisodeCleanupService>()));
        services.AddSingleton<IMuxWorkflowCoordinator>(provider => provider.GetRequired<MuxWorkflowCoordinator>());
        services.AddSingleton<BatchRunLogService>(_ => new BatchRunLogService());
        services.AddSingleton<WorkflowServices>(provider => new WorkflowServices(
            provider.GetRequired<IFileCopyService>(),
            provider.GetRequired<IEpisodeCleanupService>(),
            provider.GetRequired<IMuxWorkflowCoordinator>(),
            provider.GetRequired<BatchRunLogService>()));
    }
}
