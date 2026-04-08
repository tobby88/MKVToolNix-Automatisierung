using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Composition;

/// <summary>
/// Verdrahtet die ausführungsnahen Services für Arbeitskopien, Cleanup und Batch-Logs.
/// </summary>
internal static class WorkflowCompositionModule
{
    /// <summary>
    /// Erstellt die Workflow-Services für Mux-Ausführung und Nachbereitung.
    /// </summary>
    public static WorkflowServices Create(MuxDomainServices muxServices)
    {
        var fileCopyService = new FileCopyService();
        var cleanupService = new EpisodeCleanupService();
        return new WorkflowServices(
            fileCopyService,
            cleanupService,
            new MuxWorkflowCoordinator(muxServices.Mux, fileCopyService, cleanupService),
            new BatchRunLogService());
    }
}
