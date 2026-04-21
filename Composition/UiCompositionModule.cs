using Microsoft.Extensions.DependencyInjection;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Emby;
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
        services.AddSingleton<AppSettingsModuleServices>(provider => new AppSettingsModuleServices(
            provider.GetRequiredService<SeriesArchiveService>(),
            provider.GetRequiredService<AppToolPathStore>(),
            provider.GetRequiredService<IFfprobeLocator>(),
            provider.GetRequiredService<IMkvToolNixLocator>(),
            provider.GetRequiredService<EpisodeMetadataLookupService>(),
            provider.GetRequiredService<AppEmbySettingsStore>(),
            provider.GetRequiredService<EmbyMetadataSyncService>()));
        services.AddSingleton<IAppSettingsDialogService>(provider => new AppSettingsDialogService(
            provider.GetRequiredService<AppSettingsModuleServices>(),
            provider.GetRequiredService<IUserDialogService>()));
        services.AddSingleton<SharedEpisodeModuleServices>(provider => new SharedEpisodeModuleServices(
            provider.GetRequiredService<SeriesEpisodeMuxService>(),
            provider.GetRequiredService<EpisodePlanCoordinator>(),
            provider.GetRequiredService<EpisodeOutputPathService>(),
            provider.GetRequiredService<EpisodeCleanupFilePlanner>(),
            provider.GetRequiredService<EpisodeMetadataLookupService>()));
        services.AddSingleton<SingleEpisodeModuleServices>(provider => new SingleEpisodeModuleServices(
            provider.GetRequiredService<SharedEpisodeModuleServices>(),
            provider.GetRequiredService<IEpisodeCleanupService>(),
            provider.GetRequiredService<IMuxWorkflowCoordinator>(),
            provider.GetRequiredService<BatchRunLogService>()));
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
        services.AddSingleton<EmbyModuleServices>(provider => new EmbyModuleServices(
            provider.GetRequiredService<AppEmbySettingsStore>(),
            provider.GetRequiredService<AppArchiveSettingsStore>(),
            provider.GetRequiredService<EmbyMetadataSyncService>(),
            provider.GetRequiredService<EpisodeMetadataLookupService>(),
            provider.GetRequiredService<IAppSettingsDialogService>()));
        services.AddSingleton<MainWindowModuleServices>(provider => new MainWindowModuleServices(
            provider.GetRequiredService<SeriesArchiveService>(),
            provider.GetRequiredService<AppToolPathStore>(),
            provider.GetRequiredService<IFfprobeLocator>(),
            provider.GetRequiredService<IMkvToolNixLocator>(),
            provider.GetRequiredService<IAppSettingsDialogService>()));
        services.AddSingleton<SingleEpisodeMuxViewModel>(provider => new SingleEpisodeMuxViewModel(
            provider.GetRequiredService<SingleEpisodeModuleServices>(),
            provider.GetRequiredService<IUserDialogService>(),
            provider.GetRequiredService<IAppSettingsDialogService>()));
        services.AddSingleton<BatchMuxViewModel>(provider => new BatchMuxViewModel(
            provider.GetRequiredService<BatchModuleServices>(),
            provider.GetRequiredService<IUserDialogService>(),
            provider.GetRequiredService<IAppSettingsDialogService>()));
        services.AddSingleton<DownloadSortViewModel>(provider => new DownloadSortViewModel(
            provider.GetRequiredService<DownloadSortModuleServices>(),
            provider.GetRequiredService<IUserDialogService>()));
        services.AddSingleton<EmbySyncViewModel>(provider => new EmbySyncViewModel(
            provider.GetRequiredService<EmbyModuleServices>(),
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
                    provider.GetRequiredService<DownloadSortViewModel>()),
                new ModuleNavigationItem(
                    "Emby-Abgleich",
                    "Neue MKV-Dateien scannen und NFO-IDs abgleichen",
                    provider.GetRequiredService<EmbySyncViewModel>())
            ],
            provider.GetRequiredService<MainWindowModuleServices>()));
    }
}
