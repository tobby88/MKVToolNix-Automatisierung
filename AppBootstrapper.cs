using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels;
using MkvToolnixAutomatisierung.ViewModels.Modules;

namespace MkvToolnixAutomatisierung;

/// <summary>
/// Baut das Hauptfenster auf und zeigt Startwarnungen an, bevor die eigentliche UI sichtbar wird.
/// </summary>
internal sealed class AppBootstrapper : IDisposable
{
    private AppComposition? _composition;

    /// <summary>
    /// Baut das Hauptfenster aus der verdrahteten App-Komposition und zeigt eventuelle Startwarnungen vor dem ersten UI-Frame an.
    /// </summary>
    /// <returns>Fertig initialisiertes Hauptfenster der Anwendung.</returns>
    public MainWindow CreateMainWindow()
    {
        _composition = new AppCompositionRoot().Create();

        if (_composition.SettingsLoadResult.HasWarning)
        {
            _composition.DialogService.ShowWarning("Portable Daten", _composition.SettingsLoadResult.WarningMessage!);
        }

        if (_composition.ManagedToolStartupResult.HasWarning)
        {
            _composition.DialogService.ShowWarning("Werkzeuge", _composition.ManagedToolStartupResult.WarningMessage!);
        }

        return new MainWindow(_composition.MainWindowViewModel);
    }

    /// <summary>
    /// Entsorgt die gehaltene App-Komposition samt Root-ServiceProvider, wenn der Bootstrapper die App wieder freigibt.
    /// </summary>
    public void Dispose()
    {
        _composition?.Dispose();
        _composition = null;
    }
}
