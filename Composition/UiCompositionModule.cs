using Microsoft.Extensions.DependencyInjection;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels;
using MkvToolnixAutomatisierung.ViewModels.Modules;

namespace MkvToolnixAutomatisierung.Composition;

/// <summary>
/// Verdrahtet die ViewModels des Hauptfensters aus den zuvor aufgebauten Service-Modulen.
/// </summary>
internal static class UiCompositionModule
{
    /// <summary>
    /// Registriert Service-Bundles und ViewModels der Benutzeroberfläche.
    /// </summary>
    /// <param name="services">DI-Sammlung für UI-nahe Service-Bundles, Dialogdienste und Shell-ViewModels.</param>
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<IUserDialogService>(_ => new UserDialogService());
        services.AddSingleton<SharedEpisodeModuleServices>(provider => new SharedEpisodeModuleServices(
            provider.GetRequiredService<SeriesEpisodeMuxService>(),
            provider.GetRequiredService<EpisodePlanCoordinator>(),
            provider.GetRequiredService<EpisodeOutputPathService>(),
            provider.GetRequiredService<EpisodeCleanupFilePlanner>(),
            provider.GetRequiredService<EpisodeMetadataLookupService>()));
        services.AddSingleton<SingleEpisodeModuleServices>(provider => new SingleEpisodeModuleServices(
            provider.GetRequiredService<SharedEpisodeModuleServices>(),
            provider.GetRequiredService<IEpisodeCleanupService>(),
            provider.GetRequiredService<IMuxWorkflowCoordinator>()));
        services.AddSingleton<BatchModuleServices>(provider => new BatchModuleServices(
            provider.GetRequiredService<SharedEpisodeModuleServices>(),
            provider.GetRequiredService<BatchScanCoordinator>(),
            provider.GetRequiredService<SeriesArchiveService>(),
            provider.GetRequiredService<IFileCopyService>(),
            provider.GetRequiredService<IEpisodeCleanupService>(),
            provider.GetRequiredService<IMuxWorkflowCoordinator>(),
            provider.GetRequiredService<BatchRunLogService>()));
        services.AddSingleton<DownloadSortModuleServices>(provider => new DownloadSortModuleServices(
            provider.GetRequiredService<DownloadSortService>()));
        services.AddSingleton<MainWindowModuleServices>(provider => new MainWindowModuleServices(
            provider.GetRequiredService<SeriesArchiveService>(),
            provider.GetRequiredService<AppToolPathStore>(),
            provider.GetRequiredService<IFfprobeLocator>(),
            provider.GetRequiredService<IMkvToolNixLocator>()));
        services.AddSingleton<SingleEpisodeMuxViewModel>(provider => new SingleEpisodeMuxViewModel(
            provider.GetRequiredService<SingleEpisodeModuleServices>(),
            provider.GetRequiredService<IUserDialogService>()));
        services.AddSingleton<BatchMuxViewModel>(provider => new BatchMuxViewModel(
            provider.GetRequiredService<BatchModuleServices>(),
            provider.GetRequiredService<IUserDialogService>()));
        services.AddSingleton<DownloadSortViewModel>(provider => new DownloadSortViewModel(
            provider.GetRequiredService<DownloadSortModuleServices>(),
            provider.GetRequiredService<IUserDialogService>()));
        services.AddSingleton<MainWindowViewModel>(provider => new MainWindowViewModel(
            [
                new ModuleNavigationItem(
                    "Einzel-Mux",
                    "Eine Episode erkennen, prüfen und muxen",
                    provider.GetRequiredService<SingleEpisodeMuxViewModel>()),
                new ModuleNavigationItem(
                    "Batch-Mux",
                    "Ordner scannen und gesammelt muxen",
                    provider.GetRequiredService<BatchMuxViewModel>()),
                new ModuleNavigationItem(
                    "Einsortieren",
                    "MediathekView-Dateien in Serienordner einsortieren",
                    provider.GetRequiredService<DownloadSortViewModel>())
            ],
            provider.GetRequiredService<MainWindowModuleServices>(),
            provider.GetRequiredService<IUserDialogService>()));
    }
}
