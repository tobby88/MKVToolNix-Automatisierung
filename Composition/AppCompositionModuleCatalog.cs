namespace MkvToolnixAutomatisierung.Composition;

/// <summary>
/// Registriert alle Kompositionsmodule der Anwendung in einer festen, nachvollziehbaren Reihenfolge.
/// </summary>
internal static class AppCompositionModuleCatalog
{
    /// <summary>
    /// Baut das vollständige Anwendungsmodell auf Registrierungsbasis auf.
    /// </summary>
    public static void RegisterAll(AppServiceRegistry services)
    {
        AppStoreCompositionModule.Register(services);
        ToolingCompositionModule.Register(services);
        MetadataCompositionModule.Register(services);
        MuxCompositionModule.Register(services);
        WorkflowCompositionModule.Register(services);
        UiCompositionModule.Register(services);
    }
}
