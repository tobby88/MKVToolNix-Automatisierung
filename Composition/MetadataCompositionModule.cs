using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Composition;

/// <summary>
/// Verdrahtet TVDB-Client und Metadaten-Lookup, damit UI und Batch dieselbe Metadatenlogik nutzen.
/// </summary>
internal static class MetadataCompositionModule
{
    /// <summary>
    /// Erstellt die Metadaten-Services der Anwendung.
    /// </summary>
    public static MetadataServices Create(AppSettingStores stores)
    {
        var tvdbClient = new TvdbClient();
        return new MetadataServices(new EpisodeMetadataLookupService(stores.Metadata, tvdbClient));
    }
}
