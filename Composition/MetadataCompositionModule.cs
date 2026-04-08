using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Composition;

/// <summary>
/// Verdrahtet TVDB-Client und Metadaten-Lookup, damit UI und Batch dieselbe Metadatenlogik nutzen.
/// </summary>
internal static class MetadataCompositionModule
{
    /// <summary>
    /// Registriert die Metadaten-Services der Anwendung.
    /// </summary>
    public static void Register(AppServiceRegistry services)
    {
        services.AddSingleton<TvdbClient>(_ => new TvdbClient());
        services.AddSingleton<ITvdbClient>(provider => provider.GetRequired<TvdbClient>());
        services.AddSingleton<EpisodeMetadataLookupService>(provider => new EpisodeMetadataLookupService(
            provider.GetRequired<IAppMetadataStore>(),
            provider.GetRequired<ITvdbClient>()));
        services.AddSingleton<MetadataServices>(provider => new MetadataServices(provider.GetRequired<EpisodeMetadataLookupService>()));
    }
}
