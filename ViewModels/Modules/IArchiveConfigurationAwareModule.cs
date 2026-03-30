namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Erlaubt dem Hauptfenster, aktive Module nach einer Änderung der globalen Archivkonfiguration gezielt nachzuziehen.
/// </summary>
public interface IArchiveConfigurationAwareModule
{
    /// <summary>
    /// Reagiert auf eine geänderte Archivwurzel, aktualisiert automatische Ziele und verwirft veraltete Vergleiche.
    /// </summary>
    void HandleArchiveConfigurationChanged();
}
