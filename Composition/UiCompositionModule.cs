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
    public static void Register(AppServiceRegistry services)
    {
        services.AddSingleton<IUserDialogService>(_ => new UserDialogService());
        services.AddSingleton<SharedEpisodeModuleServices>(provider => new SharedEpisodeModuleServices(
            provider.GetRequired<SeriesEpisodeMuxService>(),
            provider.GetRequired<EpisodePlanCoordinator>(),
            provider.GetRequired<EpisodeOutputPathService>(),
            provider.GetRequired<EpisodeCleanupFilePlanner>(),
            provider.GetRequired<EpisodeMetadataLookupService>()));
        services.AddSingleton<SingleEpisodeModuleServices>(provider => new SingleEpisodeModuleServices(
            provider.GetRequired<SharedEpisodeModuleServices>(),
            provider.GetRequired<IEpisodeCleanupService>(),
            provider.GetRequired<IMuxWorkflowCoordinator>()));
        services.AddSingleton<BatchModuleServices>(provider => new BatchModuleServices(
            provider.GetRequired<SharedEpisodeModuleServices>(),
            provider.GetRequired<BatchScanCoordinator>(),
            provider.GetRequired<SeriesArchiveService>(),
            provider.GetRequired<IFileCopyService>(),
            provider.GetRequired<IEpisodeCleanupService>(),
            provider.GetRequired<IMuxWorkflowCoordinator>(),
            provider.GetRequired<BatchRunLogService>()));
        services.AddSingleton<MainWindowModuleServices>(provider => new MainWindowModuleServices(
            provider.GetRequired<SeriesArchiveService>(),
            provider.GetRequired<AppToolPathStore>(),
            provider.GetRequired<IFfprobeLocator>(),
            provider.GetRequired<IMkvToolNixLocator>()));
        services.AddSingleton<SingleEpisodeMuxViewModel>(provider => new SingleEpisodeMuxViewModel(
            provider.GetRequired<SingleEpisodeModuleServices>(),
            provider.GetRequired<IUserDialogService>()));
        services.AddSingleton<BatchMuxViewModel>(provider => new BatchMuxViewModel(
            provider.GetRequired<BatchModuleServices>(),
            provider.GetRequired<IUserDialogService>()));
        services.AddSingleton<MainWindowViewModel>(provider => new MainWindowViewModel(
            [
                new ModuleNavigationItem(
                    "Einzelepisode",
                    "Erkennen, prüfen, muxen",
                    provider.GetRequired<SingleEpisodeMuxViewModel>()),
                new ModuleNavigationItem(
                    "Batch",
                    "Ordner scannen und gesammelt muxen",
                    provider.GetRequired<BatchMuxViewModel>())
            ],
            provider.GetRequired<MainWindowModuleServices>(),
            provider.GetRequired<IUserDialogService>()));
        services.AddSingleton<AppComposition>(provider => new AppComposition(
            provider.GetRequired<IUserDialogService>(),
            provider.GetRequired<AppSettingsLoadResult>(),
            provider.GetRequired<MainWindowViewModel>()));
    }
}
