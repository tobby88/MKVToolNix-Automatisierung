using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.ViewModels;
using MkvToolnixAutomatisierung.ViewModels.Modules;

namespace MkvToolnixAutomatisierung.Composition;

/// <summary>
/// Verdrahtet die ViewModels des Hauptfensters aus den zuvor aufgebauten Service-Modulen.
/// </summary>
internal static class UiCompositionModule
{
    /// <summary>
    /// Erstellt die geteilten Fachservices, die Einzelmodus und Batch gemeinsam verwenden.
    /// </summary>
    public static SharedEpisodeModuleServices CreateSharedEpisodeServices(
        MuxDomainServices muxServices,
        MetadataServices metadata)
    {
        return new SharedEpisodeModuleServices(
            muxServices.Mux,
            muxServices.EpisodePlans,
            muxServices.OutputPaths,
            muxServices.CleanupFiles,
            metadata.Lookup);
    }

    /// <summary>
    /// Erstellt das Service-Bundle des Einzelmodus.
    /// </summary>
    public static SingleEpisodeModuleServices CreateSingleEpisodeServices(
        SharedEpisodeModuleServices shared,
        WorkflowServices workflow)
    {
        return new SingleEpisodeModuleServices(shared, workflow.Cleanup, workflow.MuxWorkflow);
    }

    /// <summary>
    /// Erstellt das Service-Bundle des Batch-Moduls.
    /// </summary>
    public static BatchModuleServices CreateBatchServices(
        SharedEpisodeModuleServices shared,
        MuxDomainServices muxServices,
        WorkflowServices workflow)
    {
        return new BatchModuleServices(
            shared,
            muxServices.BatchScan,
            muxServices.Archive,
            workflow.FileCopy,
            workflow.Cleanup,
            workflow.MuxWorkflow,
            workflow.BatchLogs);
    }

    /// <summary>
    /// Erstellt das globale Service-Bundle des Hauptfensters.
    /// </summary>
    public static MainWindowModuleServices CreateMainWindowServices(
        MuxDomainServices muxServices,
        AppSettingStores stores,
        ToolingServices tooling)
    {
        return new MainWindowModuleServices(
            muxServices.Archive,
            stores.ToolPaths,
            tooling.FfprobeLocator,
            tooling.MkvToolNixLocator);
    }

    /// <summary>
    /// Erstellt das Shell-ViewModel samt Modulnavigation.
    /// </summary>
    public static MainWindowViewModel CreateMainWindowViewModel(
        SingleEpisodeModuleServices singleEpisodeServices,
        BatchModuleServices batchServices,
        MainWindowModuleServices mainWindowServices,
        IUserDialogService dialogService)
    {
        var singleEpisode = new SingleEpisodeMuxViewModel(singleEpisodeServices, dialogService);
        var batch = new BatchMuxViewModel(batchServices, dialogService);

        return new MainWindowViewModel(
            [
                new ModuleNavigationItem(
                    "Einzelepisode",
                    "Erkennen, prüfen, muxen",
                    singleEpisode),
                new ModuleNavigationItem(
                    "Batch",
                    "Ordner scannen und gesammelt muxen",
                    batch)
            ],
            mainWindowServices,
            dialogService);
    }
}
