using Microsoft.Extensions.DependencyInjection;
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
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<AppSettingsStore>(_ => new AppSettingsStore());
        services.AddSingleton<AppToolPathStore>(provider => new AppToolPathStore(provider.GetRequiredService<AppSettingsStore>()));
        services.AddSingleton<AppArchiveSettingsStore>(provider => new AppArchiveSettingsStore(provider.GetRequiredService<AppSettingsStore>()));
        services.AddSingleton<AppMetadataStore>(provider => new AppMetadataStore(provider.GetRequiredService<AppSettingsStore>()));
        services.AddSingleton<IAppMetadataStore>(provider => provider.GetRequiredService<AppMetadataStore>());
        services.AddSingleton<AppSettingsLoadResult>(provider => provider.GetRequiredService<AppSettingsStore>().LoadWithDiagnostics());
        services.AddSingleton<AppSettingStores>(provider => new AppSettingStores(
            provider.GetRequiredService<AppSettingsStore>(),
            provider.GetRequiredService<AppToolPathStore>(),
            provider.GetRequiredService<AppArchiveSettingsStore>(),
            provider.GetRequiredService<AppMetadataStore>()));
    }
}
