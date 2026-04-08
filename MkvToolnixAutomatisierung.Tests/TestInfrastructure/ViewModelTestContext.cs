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
    /// Erstellt ein leichtgewichtiges <see cref="AppServices"/>-Paket fuer ViewModel-Tests.
    /// </summary>
    /// <param name="episodeMetadata">Optionaler Metadatenservice fuer spezielle Tests.</param>
    /// <param name="fileCopy">Optionaler Datei-Kopierservice fuer Batch-Tests.</param>
    /// <param name="cleanup">Optionaler Cleanup-Service fuer Batch-Tests.</param>
    /// <param name="muxWorkflow">Optionaler Mux-Workflow fuer Batch-Tests.</param>
    /// <param name="batchLogs">Optionaler Batch-Log-Service fuer Logging-Tests.</param>
    /// <returns>Konsistent verdrahtete Service-Sammlung fuer Tests.</returns>
    public static AppServices CreateAppServices(
        EpisodeMetadataLookupService? episodeMetadata = null,
        IFileCopyService? fileCopy = null,
        IEpisodeCleanupService? cleanup = null,
        IMuxWorkflowCoordinator? muxWorkflow = null,
        BatchRunLogService? batchLogs = null)
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

        return new AppServices(
            SeriesEpisodeMux: seriesEpisodeMux,
            EpisodePlans: new EpisodePlanCoordinator(seriesEpisodeMux),
            BatchScan: new BatchScanCoordinator(seriesEpisodeMux, metadata, outputPaths),
            Archive: archiveService,
            OutputPaths: outputPaths,
            CleanupFiles: new EpisodeCleanupFilePlanner(outputPaths),
            EpisodeMetadata: metadata,
            FileCopy: effectiveFileCopy,
            Cleanup: effectiveCleanup,
            MuxWorkflow: effectiveMuxWorkflow,
            BatchLogs: batchLogs ?? new BatchRunLogService());
    }

    private sealed class NullDurationProbe : IMediaDurationProbe
    {
        public TimeSpan? TryReadDuration(string filePath)
        {
            return null;
        }
    }
}
