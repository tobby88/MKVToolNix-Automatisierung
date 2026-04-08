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
    /// <returns>Fertig verdrahtete Anwendungskomposition für den Bootstrapper.</returns>
    public AppComposition Create()
    {
        var services = new ServiceCollection();
        AppCompositionModuleCatalog.RegisterAll(services);
        var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        return new AppComposition(
            serviceProvider,
            serviceProvider.GetRequiredService<IUserDialogService>(),
            serviceProvider.GetRequiredService<AppSettingsLoadResult>(),
            serviceProvider.GetRequiredService<MainWindowViewModel>());
    }
}

/// <summary>
/// Bündelt das Ergebnis des Bootstrap-Vorgangs, damit Startcode und Fenstererzeugung nicht jede Abhängigkeit einzeln tragen müssen.
/// </summary>
internal sealed class AppComposition : IDisposable
{
    public AppComposition(
        ServiceProvider serviceProvider,
        IUserDialogService dialogService,
        AppSettingsLoadResult settingsLoadResult,
        MainWindowViewModel mainWindowViewModel)
    {
        ServiceProvider = serviceProvider;
        DialogService = dialogService;
        SettingsLoadResult = settingsLoadResult;
        MainWindowViewModel = mainWindowViewModel;
    }

    /// <summary>
    /// Hält den Root-Provider über die App-Laufzeit am Leben und entsorgt künftige IDisposable-Singletons kontrolliert beim Shutdown.
    /// </summary>
    public ServiceProvider ServiceProvider { get; }

    public IUserDialogService DialogService { get; }

    public AppSettingsLoadResult SettingsLoadResult { get; }

    public MainWindowViewModel MainWindowViewModel { get; }

    public void Dispose()
    {
        ServiceProvider.Dispose();
    }
}
