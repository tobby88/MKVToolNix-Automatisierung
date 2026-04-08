using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Composition;

/// <summary>
/// Erstellt alle langlebigen Einstellungs-Stores aus derselben zugrunde liegenden Settings-Datei.
/// </summary>
internal static class AppStoreCompositionModule
{
    /// <summary>
    /// Baut die Store-Gruppe für Toolpfade, Archiv und TVDB-Zugangsdaten auf.
    /// </summary>
    public static AppSettingStores Create()
    {
        var settingsStore = new AppSettingsStore();
        return new AppSettingStores(
            settingsStore,
            new AppToolPathStore(settingsStore),
            new AppArchiveSettingsStore(settingsStore),
            new AppMetadataStore(settingsStore));
    }
}
