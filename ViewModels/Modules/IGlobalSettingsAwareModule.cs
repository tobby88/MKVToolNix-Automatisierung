namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Informiert Module darüber, dass selten geänderte globale Einstellungen zentral angepasst wurden.
/// </summary>
internal interface IGlobalSettingsAwareModule
{
    /// <summary>
    /// Reagiert auf Änderungen an zentral verwalteten Einstellungen wie TVDB- oder Emby-Zugangsdaten.
    /// </summary>
    void HandleGlobalSettingsChanged();
}
