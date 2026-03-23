using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels;
using MkvToolnixAutomatisierung.ViewModels.Modules;

namespace MkvToolnixAutomatisierung;

internal sealed class AppBootstrapper
{
    public MainWindow CreateMainWindow()
    {
        var dialogService = new UserDialogService();
        var settingsLoadResult = AppSettingsFileLocator.LoadCombinedSettingsWithDiagnostics();
        var toolPathStore = new AppToolPathStore();
        var mkvToolNixLocator = new MkvToolNixLocator(toolPathStore);
        var ffprobeLocator = new FfprobeLocator(toolPathStore);
        var probeService = new MkvMergeProbeService();
        var archiveService = new SeriesArchiveService(probeService);
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
        var metadataStore = new AppMetadataStore();
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
            muxWorkflow);

        var singleEpisode = new SingleEpisodeMuxViewModel(appServices, dialogService);
        var batch = new BatchMuxViewModel(appServices, dialogService);
        var viewModel = new MainWindowViewModel(
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

        if (settingsLoadResult.HasWarning)
        {
            dialogService.ShowWarning("Portable Daten", settingsLoadResult.WarningMessage!);
        }

        return new MainWindow(viewModel);
    }
}
