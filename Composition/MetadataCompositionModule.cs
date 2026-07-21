using Microsoft.Extensions.DependencyInjection;
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
    /// <param name="services">DI-Sammlung für TVDB-Client und Metadatenauflösung.</param>
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<TvdbClient>(_ => new TvdbClient());
        services.AddSingleton<ITvdbClient>(provider => provider.GetRequiredService<TvdbClient>());
        services.AddSingleton<EpisodeMetadataLookupService>(provider => new EpisodeMetadataLookupService(
            provider.GetRequiredService<IAppMetadataStore>(),
            provider.GetRequiredService<ITvdbClient>()));
        services.AddSingleton<MetadataServices>(provider => new MetadataServices(provider.GetRequiredService<EpisodeMetadataLookupService>()));
    }
}
