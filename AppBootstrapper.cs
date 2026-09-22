using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using System.Threading;
using System.Windows.Threading;

namespace MkvToolnixAutomatisierung;

/// <summary>
/// Baut das Hauptfenster auf und steuert den asynchronen App-Start inklusive späterer Startwarnungen.
/// </summary>
internal sealed class AppBootstrapper : IDisposable
{
    private AppComposition? _composition;
    private bool _isCreating;
    private bool _isDisposed;

    /// <summary>
    /// Baut das Hauptfenster synchron aus der verdrahteten App-Komposition.
    /// </summary>
    /// <returns>Fertig initialisiertes Hauptfenster der Anwendung.</returns>
    public MainWindow CreateMainWindow()
    {
        if (SynchronizationContext.Current is DispatcherSynchronizationContext
            || System.Windows.Application.Current?.Dispatcher.CheckAccess() == true)
        {
            throw new InvalidOperationException("CreateMainWindow() darf nicht auf dem UI-Thread verwendet werden. Verwende stattdessen CreateMainWindowAsync().");
        }

        return CreateMainWindowAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Baut das Hauptfenster asynchron, damit Downloads verwalteter Startressourcen nicht den ersten sichtbaren UI-Frame blockieren.
    /// </summary>
    /// <param name="progress">Optionaler Fortschrittskanal für einen vorgeschalteten Startdialog.</param>
    /// <param name="cancellationToken">Abbruchsignal für Startvorgänge mit Netzwerkzugriff.</param>
    /// <returns>Fertig initialisiertes Hauptfenster der Anwendung.</returns>
    public async Task<MainWindow> CreateMainWindowAsync(
        IProgress<ManagedToolStartupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_isCreating || _composition is not null)
        {
            throw new InvalidOperationException("Das Hauptfenster wird bereits erstellt oder wurde bereits erstellt.");
        }

        if (SynchronizationContext.Current is not DispatcherSynchronizationContext)
        {
            throw new InvalidOperationException("CreateMainWindowAsync() muss auf dem WPF-Dispatcher aufgerufen werden.");
        }

        _isCreating = true;
        AppComposition? composition = null;
        try
        {
            composition = await new AppCompositionRoot().CreateAsync(progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            var window = new MainWindow(composition.MainWindowViewModel);
            _composition = composition;
            return window;
        }
        catch
        {
            composition?.Dispose();
            throw;
        }
        finally
        {
            _isCreating = false;
        }
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
            _composition.DialogService.ShowWarning("Startressourcen", _composition.ManagedToolStartupResult.WarningMessage!);
        }
    }

    /// <summary>
    /// Entsorgt die gehaltene App-Komposition samt Root-ServiceProvider, wenn der Bootstrapper die App wieder freigibt.
    /// </summary>
    public void Dispose()
    {
        _isDisposed = true;
        _composition?.Dispose();
        _composition = null;
    }
}
