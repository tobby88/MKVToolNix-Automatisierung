using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels;
using MkvToolnixAutomatisierung.ViewModels.Modules;

namespace MkvToolnixAutomatisierung;

/// <summary>
/// Baut das Hauptfenster auf und zeigt Startwarnungen an, bevor die eigentliche UI sichtbar wird.
/// </summary>
internal sealed class AppBootstrapper
{
    public MainWindow CreateMainWindow()
    {
        var composition = new AppCompositionRoot().Create();

        if (composition.SettingsLoadResult.HasWarning)
        {
            composition.DialogService.ShowWarning("Portable Daten", composition.SettingsLoadResult.WarningMessage!);
        }

        return new MainWindow(composition.MainWindowViewModel);
    }
}
