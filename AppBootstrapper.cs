using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels;
using MkvToolnixAutomatisierung.ViewModels.Modules;

namespace MkvToolnixAutomatisierung;

/// <summary>
/// Baut das Hauptfenster auf und steuert den asynchronen App-Start inklusive späterer Startwarnungen.
/// </summary>
internal sealed class AppBootstrapper : IDisposable
{
    private AppComposition? _composition;

    /// <summary>
    /// Baut das Hauptfenster synchron aus der verdrahteten App-Komposition.
    /// </summary>
    /// <returns>Fertig initialisiertes Hauptfenster der Anwendung.</returns>
    public MainWindow CreateMainWindow()
    {
        return CreateMainWindowAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Baut das Hauptfenster asynchron, damit Werkzeug-Downloads nicht den ersten sichtbaren UI-Frame blockieren.
    /// </summary>
    /// <param name="progress">Optionaler Fortschrittskanal für einen vorgeschalteten Startdialog.</param>
    /// <param name="cancellationToken">Abbruchsignal für Startvorgänge mit Netzwerkzugriff.</param>
    /// <returns>Fertig initialisiertes Hauptfenster der Anwendung.</returns>
    public async Task<MainWindow> CreateMainWindowAsync(
        IProgress<ManagedToolStartupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _composition = await new AppCompositionRoot().CreateAsync(progress, cancellationToken);

        return new MainWindow(_composition.MainWindowViewModel);
    }

    /// <summary>
    /// Zeigt aufgesammelte Startwarnungen erst dann an, wenn bereits ein echtes Hauptfenster existiert.
    /// </summary>
    public void ShowStartupWarnings()
    {
        if (_composition?.SettingsLoadResult.HasWarning == true)
        {
            _composition.DialogService.ShowWarning("Portable Daten", _composition.SettingsLoadResult.WarningMessage!);
        }

        if (_composition?.ManagedToolStartupResult.HasWarning == true)
        {
            _composition.DialogService.ShowWarning("Werkzeuge", _composition.ManagedToolStartupResult.WarningMessage!);
        }
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
