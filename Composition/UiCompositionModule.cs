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
    /// Erstellt das Shell-ViewModel samt Modulnavigation.
    /// </summary>
    public static MainWindowViewModel CreateMainWindowViewModel(
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
}
