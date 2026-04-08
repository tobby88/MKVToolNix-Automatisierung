using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels;
using MkvToolnixAutomatisierung.ViewModels.Modules;

namespace MkvToolnixAutomatisierung;

/// <summary>
/// Zentraler Composition Root der App: hier werden alle langlebigen Services und ViewModels einmalig verdrahtet.
/// </summary>
internal sealed class AppCompositionRoot
{
    /// <summary>
    /// Erstellt die komplette Objektstruktur der Anwendung in fachlich gruppierten Schritten.
    /// </summary>
    /// <returns>Fertig verdrahtete Anwendungskomposition für den Bootstrapper.</returns>
    public AppComposition Create()
    {
        IUserDialogService dialogService = new UserDialogService();
        var stores = CreateStores();
        var settingsLoadResult = stores.Settings.LoadWithDiagnostics();
        var tooling = CreateTooling(stores);
        var metadata = CreateMetadataServices(stores);
        var muxServices = CreateMuxServices(stores, tooling, metadata);
        var workflow = CreateWorkflowServices(muxServices);
        var appServices = CreateAppServices(muxServices, metadata, workflow);
        var mainWindowViewModel = CreateMainWindowViewModel(appServices, dialogService, stores, tooling);

        return new AppComposition(dialogService, settingsLoadResult, mainWindowViewModel, appServices);
    }

    private static AppSettingStores CreateStores()
    {
        var settingsStore = new AppSettingsStore();
        return new AppSettingStores(
            settingsStore,
            new AppToolPathStore(settingsStore),
            new AppArchiveSettingsStore(settingsStore),
            new AppMetadataStore(settingsStore));
    }

    private static ToolingServices CreateTooling(AppSettingStores stores)
    {
        return new ToolingServices(
            new MkvToolNixLocator(stores.ToolPaths),
            new FfprobeLocator(stores.ToolPaths),
            new MkvMergeProbeService());
    }

    private static MuxDomainServices CreateMuxServices(
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

    private static MetadataServices CreateMetadataServices(AppSettingStores stores)
    {
        var tvdbClient = new TvdbClient();
        return new MetadataServices(new EpisodeMetadataLookupService(stores.Metadata, tvdbClient));
    }

    private static WorkflowServices CreateWorkflowServices(MuxDomainServices muxServices)
    {
        var fileCopyService = new FileCopyService();
        var cleanupService = new EpisodeCleanupService();
        return new WorkflowServices(
            fileCopyService,
            cleanupService,
            new MuxWorkflowCoordinator(muxServices.Mux, fileCopyService, cleanupService),
            new BatchRunLogService());
    }

    private static AppServices CreateAppServices(
        MuxDomainServices muxServices,
        MetadataServices metadata,
        WorkflowServices workflow)
    {
        return new AppServices(
            muxServices.Mux,
            muxServices.EpisodePlans,
            muxServices.BatchScan,
            muxServices.Archive,
            muxServices.OutputPaths,
            muxServices.CleanupFiles,
            metadata.Lookup,
            workflow.FileCopy,
            workflow.Cleanup,
            workflow.MuxWorkflow,
            workflow.BatchLogs);
    }

    private static MainWindowViewModel CreateMainWindowViewModel(
        AppServices appServices,
        IUserDialogService dialogService,
        AppSettingStores stores,
        ToolingServices tooling)
    {
        var singleEpisode = new SingleEpisodeMuxViewModel(appServices, dialogService);
        var batch = new BatchMuxViewModel(appServices, dialogService);

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
            appServices,
            dialogService,
            stores.ToolPaths,
            tooling.FfprobeLocator,
            tooling.MkvToolNixLocator);
    }

    private sealed record AppSettingStores(
        AppSettingsStore Settings,
        AppToolPathStore ToolPaths,
        AppArchiveSettingsStore Archive,
        AppMetadataStore Metadata);

    private sealed record ToolingServices(
        MkvToolNixLocator MkvToolNixLocator,
        FfprobeLocator FfprobeLocator,
        MkvMergeProbeService Probe);

    private sealed record MetadataServices(EpisodeMetadataLookupService Lookup);

    private sealed record MuxDomainServices(
        SeriesEpisodeMuxService Mux,
        EpisodePlanCoordinator EpisodePlans,
        BatchScanCoordinator BatchScan,
        SeriesArchiveService Archive,
        EpisodeOutputPathService OutputPaths,
        EpisodeCleanupFilePlanner CleanupFiles);

    private sealed record WorkflowServices(
        IFileCopyService FileCopy,
        IEpisodeCleanupService Cleanup,
        IMuxWorkflowCoordinator MuxWorkflow,
        BatchRunLogService BatchLogs);
}

/// <summary>
/// Bündelt das Ergebnis des Bootstrap-Vorgangs, damit Startcode und Fenstererzeugung nicht jede Abhängigkeit einzeln tragen müssen.
/// </summary>
internal sealed record AppComposition(
    IUserDialogService DialogService,
    AppSettingsLoadResult SettingsLoadResult,
    MainWindowViewModel MainWindowViewModel,
    AppServices Services);
