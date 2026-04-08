using Microsoft.Extensions.DependencyInjection;

namespace MkvToolnixAutomatisierung.Composition;

/// <summary>
/// Registriert alle Kompositionsmodule der Anwendung in einer festen, nachvollziehbaren Reihenfolge.
/// </summary>
internal static class AppCompositionModuleCatalog
{
    /// <summary>
    /// Baut das vollständige Anwendungsmodell auf Registrierungsbasis auf.
    /// </summary>
    /// <param name="services">Zentrale DI-Sammlung, in die alle fachlichen und technischen Module ihre Registrierungen schreiben.</param>
    public static void RegisterAll(IServiceCollection services)
    {
        AppStoreCompositionModule.Register(services);
        ToolingCompositionModule.Register(services);
        MetadataCompositionModule.Register(services);
        MuxCompositionModule.Register(services);
        WorkflowCompositionModule.Register(services);
        UiCompositionModule.Register(services);
    }
}
