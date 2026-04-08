using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Tests.TestInfrastructure;

/// <summary>
/// Baut eine minimale, aber konsistente Testumgebung fuer ViewModels auf.
/// </summary>
internal static class ViewModelTestContext
{
    /// <summary>
    /// Stellt sicher, dass fuer WPF-nahe ViewModels eine Application verfuegbar ist.
    /// </summary>
    public static void EnsureApplication()
    {
        if (Application.Current is null)
        {
            _ = new Application();
        }
    }

    /// <summary>
    /// Erstellt das Service-Bundle des Einzelmodus fuer ViewModel-Tests.
    /// </summary>
    /// <param name="episodeMetadata">Optionaler Metadatenservice fuer spezielle Tests.</param>
    /// <param name="fileCopy">Optionaler Datei-Kopierservice fuer Batch-Tests.</param>
    /// <param name="cleanup">Optionaler Cleanup-Service fuer Batch-Tests.</param>
    /// <param name="muxWorkflow">Optionaler Mux-Workflow fuer Batch-Tests.</param>
    /// <param name="batchLogs">Optionaler Batch-Log-Service fuer Logging-Tests.</param>
    /// <returns>Konsistent verdrahtetes Einzelmodus-Bundle fuer Tests.</returns>
    public static SingleEpisodeModuleServices CreateSingleEpisodeServices(
        EpisodeMetadataLookupService? episodeMetadata = null,
        IFileCopyService? fileCopy = null,
        IEpisodeCleanupService? cleanup = null,
        IMuxWorkflowCoordinator? muxWorkflow = null,
        BatchRunLogService? batchLogs = null)
    {
        var graph = CreateServiceGraph(episodeMetadata, fileCopy, cleanup, muxWorkflow, batchLogs);
        return new SingleEpisodeModuleServices(graph.Shared, graph.Cleanup, graph.MuxWorkflow);
    }

    /// <summary>
    /// Erstellt das Service-Bundle des Batch-Moduls fuer ViewModel-Tests.
    /// </summary>
    public static BatchModuleServices CreateBatchServices(
        EpisodeMetadataLookupService? episodeMetadata = null,
        IFileCopyService? fileCopy = null,
        IEpisodeCleanupService? cleanup = null,
        IMuxWorkflowCoordinator? muxWorkflow = null,
        BatchRunLogService? batchLogs = null)
    {
        var graph = CreateServiceGraph(episodeMetadata, fileCopy, cleanup, muxWorkflow, batchLogs);
        return new BatchModuleServices(
            graph.Shared,
            graph.BatchScan,
            graph.Archive,
            graph.FileCopy,
            graph.Cleanup,
            graph.MuxWorkflow,
            graph.BatchLogs);
    }

    /// <summary>
    /// Erstellt das globale Shell-Bundle des Hauptfensters fuer ViewModel-Tests.
    /// </summary>
    public static MainWindowModuleServices CreateMainWindowServices(
        AppToolPathStore? toolPathStore = null,
        SeriesArchiveService? archiveService = null,
        IFfprobeLocator? ffprobeLocator = null,
        IMkvToolNixLocator? mkvToolNixLocator = null)
    {
        var settingsStore = new AppSettingsStore();
        var effectiveToolPathStore = toolPathStore ?? new AppToolPathStore(settingsStore);
        var effectiveArchiveService = archiveService ?? new SeriesArchiveService(
            new MkvMergeProbeService(),
            new AppArchiveSettingsStore(settingsStore));

        return new MainWindowModuleServices(
            effectiveArchiveService,
            effectiveToolPathStore,
            ffprobeLocator ?? new MkvToolNixFreeFfprobeLocator(),
            mkvToolNixLocator ?? new MkvToolNixLocator(effectiveToolPathStore));
    }

    private static TestServiceGraph CreateServiceGraph(
        EpisodeMetadataLookupService? episodeMetadata,
        IFileCopyService? fileCopy,
        IEpisodeCleanupService? cleanup,
        IMuxWorkflowCoordinator? muxWorkflow,
        BatchRunLogService? batchLogs)
    {
        var settingsStore = new AppSettingsStore();
        var toolPathStore = new AppToolPathStore(settingsStore);
        var metadataStore = new AppMetadataStore(settingsStore);
        var archiveSettingsStore = new AppArchiveSettingsStore(settingsStore);
        var probeService = new MkvMergeProbeService();
        var archiveService = new SeriesArchiveService(probeService, archiveSettingsStore);
        var outputPaths = new EpisodeOutputPathService(archiveService);
        var metadata = episodeMetadata ?? new EpisodeMetadataLookupService(metadataStore, new TvdbClient());

        var seriesEpisodeMux = new SeriesEpisodeMuxService(
            new SeriesEpisodeMuxPlanner(
                new MkvToolNixLocator(toolPathStore),
                probeService,
                archiveService,
                new NullDurationProbe()),
            new MuxExecutionService(),
            new MkvMergeOutputParser());

        var effectiveFileCopy = fileCopy ?? new FileCopyService();
        var effectiveCleanup = cleanup ?? new EpisodeCleanupService();
        var effectiveMuxWorkflow = muxWorkflow ?? new MuxWorkflowCoordinator(
            seriesEpisodeMux,
            effectiveFileCopy,
            effectiveCleanup);

        var shared = new SharedEpisodeModuleServices(
            seriesEpisodeMux,
            new EpisodePlanCoordinator(seriesEpisodeMux),
            outputPaths,
            new EpisodeCleanupFilePlanner(outputPaths),
            metadata);

        return new TestServiceGraph(
            shared,
            new BatchScanCoordinator(seriesEpisodeMux, metadata, outputPaths),
            archiveService,
            effectiveFileCopy,
            effectiveCleanup,
            effectiveMuxWorkflow,
            batchLogs ?? new BatchRunLogService());
    }

    private sealed record TestServiceGraph(
        SharedEpisodeModuleServices Shared,
        BatchScanCoordinator BatchScan,
        SeriesArchiveService Archive,
        IFileCopyService FileCopy,
        IEpisodeCleanupService Cleanup,
        IMuxWorkflowCoordinator MuxWorkflow,
        BatchRunLogService BatchLogs);

    private sealed class MkvToolNixFreeFfprobeLocator : IFfprobeLocator
    {
        public string? TryFindFfprobePath()
        {
            return null;
        }
    }

    private sealed class NullDurationProbe : IMediaDurationProbe
    {
        public TimeSpan? TryReadDuration(string filePath)
        {
            return null;
        }
    }
}
