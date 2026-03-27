using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels;
using MkvToolnixAutomatisierung.ViewModels.Modules;

namespace MkvToolnixAutomatisierung;

internal sealed class AppCompositionRoot
{
    public AppComposition Create()
    {
        var settingsStore = new AppSettingsStore();
        var dialogService = new UserDialogService();
        var settingsLoadResult = settingsStore.LoadWithDiagnostics();
        var toolPathStore = new AppToolPathStore(settingsStore);
        var archiveSettingsStore = new AppArchiveSettingsStore(settingsStore);
        var metadataStore = new AppMetadataStore(settingsStore);
        var mkvToolNixLocator = new MkvToolNixLocator(toolPathStore);
        var ffprobeLocator = new FfprobeLocator(toolPathStore);
        var probeService = new MkvMergeProbeService();
        var archiveService = new SeriesArchiveService(probeService, archiveSettingsStore);
        var outputPathService = new EpisodeOutputPathService(archiveService);
        var cleanupFilePlanner = new EpisodeCleanupFilePlanner(outputPathService);
        var ffprobeDurationProbe = new FfprobeDurationProbe(ffprobeLocator);
        var durationProbe = new PreferredMediaDurationProbe(
            ffprobeDurationProbe,
            new WindowsMediaDurationProbe());
        var planner = new SeriesEpisodeMuxPlanner(mkvToolNixLocator, probeService, archiveService, durationProbe);
        var executionService = new MuxExecutionService();
        var outputParser = new MkvMergeOutputParser();
        var muxService = new SeriesEpisodeMuxService(planner, executionService, outputParser);
        var episodePlanCoordinator = new EpisodePlanCoordinator(muxService);
        var fileCopyService = new FileCopyService();
        var cleanupService = new EpisodeCleanupService();
        var muxWorkflow = new MuxWorkflowCoordinator(muxService, fileCopyService, cleanupService);
        var batchLogService = new BatchRunLogService();
        var tvdbClient = new TvdbClient();
        var metadataLookupService = new EpisodeMetadataLookupService(metadataStore, tvdbClient);
        var batchScanCoordinator = new BatchScanCoordinator(muxService, metadataLookupService, outputPathService);
        var appServices = new AppServices(
            muxService,
            episodePlanCoordinator,
            batchScanCoordinator,
            archiveService,
            outputPathService,
            cleanupFilePlanner,
            metadataLookupService,
            fileCopyService,
            cleanupService,
            muxWorkflow,
            batchLogService);

        var singleEpisode = new SingleEpisodeMuxViewModel(appServices, dialogService);
        var batch = new BatchMuxViewModel(appServices, dialogService);
        var mainWindowViewModel = new MainWindowViewModel(
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
            toolPathStore,
            ffprobeLocator,
            mkvToolNixLocator);

        return new AppComposition(dialogService, settingsLoadResult, mainWindowViewModel, appServices);
    }
}

internal sealed record AppComposition(
    UserDialogService DialogService,
    AppSettingsLoadResult SettingsLoadResult,
    MainWindowViewModel MainWindowViewModel,
    AppServices Services);
