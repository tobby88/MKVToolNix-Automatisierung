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
        var services = new AppServiceRegistry();
        AppCompositionModuleCatalog.RegisterAll(services);
        return services.GetRequired<AppComposition>();
    }
}

/// <summary>
/// Bündelt das Ergebnis des Bootstrap-Vorgangs, damit Startcode und Fenstererzeugung nicht jede Abhängigkeit einzeln tragen müssen.
/// </summary>
internal sealed record AppComposition(
    IUserDialogService DialogService,
    AppSettingsLoadResult SettingsLoadResult,
    MainWindowViewModel MainWindowViewModel);
