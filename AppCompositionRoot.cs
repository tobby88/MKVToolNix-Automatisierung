using Microsoft.Extensions.DependencyInjection;
using MkvToolnixAutomatisierung.Composition;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.ViewModels;

namespace MkvToolnixAutomatisierung;

/// <summary>
/// Zentraler Composition Root der App: hier werden alle langlebigen Services und ViewModels einmalig verdrahtet.
/// </summary>
internal sealed class AppCompositionRoot
{
    /// <summary>
    /// Erstellt die komplette Objektstruktur der Anwendung in fachlich gruppierten Schritten.
    /// </summary>
    /// <remarks>
    /// Der Root baut bewusst einen validierenden <see cref="ServiceProvider"/>, damit fehlende Registrierungen
    /// oder unauflösbare Konstruktoren bereits beim Start der App statt erst tief im UI-Ablauf auffallen.
    /// </remarks>
    /// <returns>Fertig verdrahtete Anwendungskomposition für den Bootstrapper.</returns>
    public AppComposition Create()
    {
        return CreateAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Erstellt die komplette Objektstruktur der Anwendung asynchron und meldet sichtbaren Startfortschritt.
    /// </summary>
    /// <param name="progress">Optionaler Fortschrittskanal für Werkzeugprüfung und Erstversorgung.</param>
    /// <param name="cancellationToken">Abbruchsignal für Startvorgänge mit Netzwerkzugriff.</param>
    /// <returns>Fertig verdrahtete Anwendungskomposition für den Bootstrapper.</returns>
    public async Task<AppComposition> CreateAsync(
        IProgress<ManagedToolStartupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var services = new ServiceCollection();
        AppCompositionModuleCatalog.RegisterAll(services);
        var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        try
        {
            var managedToolStartupResult = await serviceProvider
                .GetRequiredService<ManagedToolInstallerService>()
                .EnsureManagedToolsAsync(progress, cancellationToken)
                .ConfigureAwait(false);

            return CreateComposition(serviceProvider, managedToolStartupResult);
        }
        catch
        {
            serviceProvider.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Löst die Startdienste aus einem bereits gebauten Root-Provider auf und kapselt den Fehlerpfad einschließlich Dispose.
    /// </summary>
    /// <param name="serviceProvider">Vollständig gebauter Root-Provider der Anwendung.</param>
    /// <param name="managedToolStartupResult">Vorher bereits ermitteltes Ergebnis der Werkzeugprüfung.</param>
    /// <returns>Fertig aufgelöste Anwendungs-Komposition.</returns>
    internal static AppComposition CreateComposition(
        ServiceProvider serviceProvider,
        ManagedToolStartupResult? managedToolStartupResult = null)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        try
        {
            return new AppComposition(
                serviceProvider,
                serviceProvider.GetRequiredService<IUserDialogService>(),
                serviceProvider.GetRequiredService<AppSettingsLoadResult>(),
                managedToolStartupResult ?? new ManagedToolStartupResult([]),
                serviceProvider.GetRequiredService<MainWindowViewModel>());
        }
        catch
        {
            serviceProvider.Dispose();
            throw;
        }
    }
}

/// <summary>
/// Bündelt das Ergebnis des Bootstrap-Vorgangs, damit Startcode und Fenstererzeugung nicht jede Abhängigkeit einzeln tragen müssen.
/// </summary>
internal sealed class AppComposition : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    /// <summary>
    /// Initialisiert eine vollständig aufgelöste App-Komposition einschließlich Root-ServiceProvider.
    /// </summary>
    /// <param name="serviceProvider">Gebauter Root-Provider, dessen Singleton-Lebensdauer an die App gebunden bleibt.</param>
    /// <param name="dialogService">Globaler Dialogdienst für Startwarnungen und spätere UI-Interaktion.</param>
    /// <param name="settingsLoadResult">Diagnoseobjekt des initialen Settings-Ladevorgangs.</param>
    /// <param name="managedToolStartupResult">Ergebnis der automatischen Toolversorgung vor dem ersten UI-Frame.</param>
    /// <param name="mainWindowViewModel">Vollständig verdrahtetes Shell-ViewModel des Hauptfensters.</param>
    public AppComposition(
        ServiceProvider serviceProvider,
        IUserDialogService dialogService,
        AppSettingsLoadResult settingsLoadResult,
        ManagedToolStartupResult managedToolStartupResult,
        MainWindowViewModel mainWindowViewModel)
    {
        _serviceProvider = serviceProvider;
        DialogService = dialogService;
        SettingsLoadResult = settingsLoadResult;
        ManagedToolStartupResult = managedToolStartupResult;
        MainWindowViewModel = mainWindowViewModel;
    }

    /// <summary>
    /// Hält den Root-Provider über die App-Laufzeit am Leben und entsorgt künftige IDisposable-Singletons kontrolliert beim Shutdown.
    /// </summary>
    public IUserDialogService DialogService { get; }

    /// <summary>
    /// Ergebnis des initialen Ladens der portablen Einstellungen inklusive möglicher Warnmeldung für den Startdialog.
    /// </summary>
    public AppSettingsLoadResult SettingsLoadResult { get; }

    /// <summary>
    /// Ergebnis der automatischen Toolprüfung und gegebenenfalls erfolgten Toolaktualisierung beim Start.
    /// </summary>
    public ManagedToolStartupResult ManagedToolStartupResult { get; }

    /// <summary>
    /// Zentrales Shell-ViewModel, das Modulnavigation und globale Tool-/Archivkonfiguration steuert.
    /// </summary>
    public MainWindowViewModel MainWindowViewModel { get; }

    /// <summary>
    /// Entsorgt den Root-Provider der App und damit alle daran gebundenen disposablen Singletons.
    /// </summary>
    public void Dispose()
    {
        _serviceProvider.Dispose();
    }
}
