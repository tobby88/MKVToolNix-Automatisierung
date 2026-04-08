using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Composition;

/// <summary>
/// Erstellt alle langlebigen Einstellungs-Stores aus derselben zugrunde liegenden Settings-Datei.
/// </summary>
internal static class AppStoreCompositionModule
{
    /// <summary>
    /// Registriert die Store-Gruppe für Toolpfade, Archiv und TVDB-Zugangsdaten.
    /// </summary>
    public static void Register(AppServiceRegistry services)
    {
        services.AddSingleton<AppSettingsStore>(_ => new AppSettingsStore());
        services.AddSingleton<AppToolPathStore>(provider => new AppToolPathStore(provider.GetRequired<AppSettingsStore>()));
        services.AddSingleton<AppArchiveSettingsStore>(provider => new AppArchiveSettingsStore(provider.GetRequired<AppSettingsStore>()));
        services.AddSingleton<AppMetadataStore>(provider => new AppMetadataStore(provider.GetRequired<AppSettingsStore>()));
        services.AddSingleton<IAppMetadataStore>(provider => provider.GetRequired<AppMetadataStore>());
        services.AddSingleton<AppSettingsLoadResult>(provider => provider.GetRequired<AppSettingsStore>().LoadWithDiagnostics());
        services.AddSingleton<AppSettingStores>(provider => new AppSettingStores(
            provider.GetRequired<AppSettingsStore>(),
            provider.GetRequired<AppToolPathStore>(),
            provider.GetRequired<AppArchiveSettingsStore>(),
            provider.GetRequired<AppMetadataStore>()));
    }
}
