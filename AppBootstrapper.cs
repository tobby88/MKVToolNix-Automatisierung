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

    public MainWindow CreateMainWindow()
    {
        _composition = new AppCompositionRoot().Create();

        if (_composition.SettingsLoadResult.HasWarning)
        {
            _composition.DialogService.ShowWarning("Portable Daten", _composition.SettingsLoadResult.WarningMessage!);
        }

        return new MainWindow(_composition.MainWindowViewModel);
    }

    public void Dispose()
    {
        _composition?.Dispose();
        _composition = null;
    }
}
