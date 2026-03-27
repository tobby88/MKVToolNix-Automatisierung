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
        var composition = new AppCompositionRoot().Create();

        if (composition.SettingsLoadResult.HasWarning)
        {
            composition.DialogService.ShowWarning("Portable Daten", composition.SettingsLoadResult.WarningMessage!);
        }

        if (!composition.Services.Archive.IsArchiveAvailable())
        {
            composition.DialogService.ShowWarning("Serienbibliothek", composition.Services.Archive.BuildArchiveUnavailableWarningMessage());
        }

        return new MainWindow(composition.MainWindowViewModel);
    }
}
