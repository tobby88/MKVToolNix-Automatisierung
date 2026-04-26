using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Services.Metadata;

/// <summary>
/// Liest und schreibt nur den Metadaten-Teil der kombinierten App-Einstellungen.
/// </summary>
internal interface IAppMetadataStore
{
    /// <summary>
    /// Lädt ausschließlich den Metadaten-Teil der kombinierten Einstellungen.
    /// </summary>
    AppMetadataSettings Load();

    /// <summary>
    /// Speichert ausschließlich den Metadaten-Teil der kombinierten Einstellungen.
    /// </summary>
    void Save(AppMetadataSettings settings);

    /// <summary>
    /// Aktualisiert ausschließlich den Metadaten-Teil atomar innerhalb des kombinierten Settings-Stores.
    /// </summary>
    void Update(Action<AppMetadataSettings> updateAction);

    /// <summary>
    /// Pfad der zugrunde liegenden portablen Settings-Datei.
    /// </summary>
    string SettingsFilePath { get; }
}

/// <summary>
/// Standardimplementierung für den Metadaten-Teil der portablen App-Einstellungen.
/// </summary>
internal sealed class AppMetadataStore : IAppMetadataStore
{
    private readonly AppSettingsStore _settingsStore;

    /// <summary>
    /// Initialisiert den Metadaten-Store mit dem standardmäßigen portablen Settings-Backend.
    /// </summary>
    public AppMetadataStore()
        : this(new AppSettingsStore())
    {
    }

    /// <summary>
    /// Initialisiert den Metadaten-Store mit einem expliziten kombinierten Settings-Backend.
    /// </summary>
    /// <param name="settingsStore">Store für die gemeinsame portable Settings-Datei.</param>
    public AppMetadataStore(AppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    /// <summary>
    /// Lädt ausschließlich den Metadaten-Teil der kombinierten Einstellungen.
    /// </summary>
    /// <returns>Aktuelle Metadaten-Einstellungen oder Standardwerte.</returns>
    public AppMetadataSettings Load()
    {
        return _settingsStore.Load().Metadata?.Clone() ?? new AppMetadataSettings();
    }

    /// <summary>
    /// Speichert ausschließlich den Metadaten-Teil der kombinierten Einstellungen.
    /// </summary>
    /// <param name="settings">Zu speichernde TVDB- und Mapping-Einstellungen.</param>
    public void Save(AppMetadataSettings settings)
    {
        var normalizedSettings = settings?.Clone() ?? new AppMetadataSettings();
        _settingsStore.Update(combinedSettings => combinedSettings.Metadata = normalizedSettings.Clone());
    }

    /// <summary>
    /// Aktualisiert den Metadaten-Teil innerhalb derselben Sperre wie die kombinierte Settings-Datei.
    /// </summary>
    /// <param name="updateAction">Mutation auf einem geklonten Metadaten-Einstellungssatz.</param>
    public void Update(Action<AppMetadataSettings> updateAction)
    {
        ArgumentNullException.ThrowIfNull(updateAction);

        _settingsStore.Update(combinedSettings =>
        {
            var metadataSettings = combinedSettings.Metadata?.Clone() ?? new AppMetadataSettings();
            updateAction(metadataSettings);
            combinedSettings.Metadata = metadataSettings.Clone();
        });
    }

    /// <summary>
    /// Pfad der zugrunde liegenden portablen Settings-Datei.
    /// </summary>
    public string SettingsFilePath => AppSettingsFileLocator.GetSettingsFilePath();
}

/// <summary>
/// Bevorzugte Strategie für den IMDb-Abgleich im Emby-Modul.
/// </summary>
public enum ImdbLookupMode
{
    /// <summary>
    /// Nutzt bevorzugt <c>imdbapi.dev</c> und fällt bei Provider-Ausfall auf die Browserhilfe zurück.
    /// </summary>
    Auto,

    /// <summary>
    /// Erzwingt den API-basierten IMDb-Abgleich ohne automatischen Browser-Fallback.
    /// </summary>
    ApiOnly,

    /// <summary>
    /// Nutzt ausschließlich die browsergestützte IMDb-Suche.
    /// </summary>
    BrowserOnly
}

/// <summary>
/// Persistente TVDB-Zugangsdaten plus lokale Zuordnungstabelle zwischen Dateinamen und TVDB-Serien.
/// </summary>
public sealed class AppMetadataSettings
{
    /// <summary>
    /// TVDB-API-Key für die v4-API.
    /// </summary>
    public string TvdbApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Optionaler TVDB-PIN für persönliche Accounts.
    /// </summary>
    public string TvdbPin { get; set; } = string.Empty;

    /// <summary>
    /// Bevorzugte Strategie für den IMDb-Abgleich im Emby-Modul.
    /// </summary>
    public ImdbLookupMode ImdbLookupMode { get; set; } = ImdbLookupMode.Auto;

    /// <summary>
    /// Lokale Zuordnung zwischen Seriennamen aus Dateinamen und TVDB-Serien.
    /// </summary>
    public List<SeriesMetadataMapping> SeriesMappings { get; set; } = [];

    /// <summary>
    /// Erzeugt eine tiefe Kopie der Metadaten-Einstellungen.
    /// </summary>
    /// <returns>Geklonter Einstellungssatz.</returns>
    public AppMetadataSettings Clone()
    {
        return new AppMetadataSettings
        {
            TvdbApiKey = TvdbApiKey?.Trim() ?? string.Empty,
            TvdbPin = TvdbPin?.Trim() ?? string.Empty,
            ImdbLookupMode = ImdbLookupMode,
            SeriesMappings = (SeriesMappings ?? [])
                .Where(mapping => mapping is not null)
                .Select(mapping => mapping.Clone())
                .ToList()
        };
    }
}

/// <summary>
/// Verknüpft einen lokal erkannten Seriennamen mit einer bevorzugten TVDB-Serie.
/// </summary>
public sealed class SeriesMetadataMapping
{
    /// <summary>
    /// Lokal erkannter Serienname.
    /// </summary>
    public string LocalSeriesName { get; set; } = string.Empty;

    /// <summary>
    /// Eindeutige TVDB-Serien-ID.
    /// </summary>
    public int TvdbSeriesId { get; set; }

    /// <summary>
    /// Anzeigename der gemappten TVDB-Serie.
    /// </summary>
    public string TvdbSeriesName { get; set; } = string.Empty;

    /// <summary>
    /// Ursprungssprache der TVDB-Serie, z. B. <c>swe</c> für Schwedisch oder <c>de</c> für Deutsch.
    /// Null oder leer, wenn die Sprache nicht bekannt ist (Altmappings bleiben rückwärtskompatibel).
    /// </summary>
    public string? OriginalLanguage { get; set; }

    /// <summary>
    /// Erzeugt eine Kopie eines einzelnen Mappings.
    /// </summary>
    /// <returns>Geklontes Serien-Mapping.</returns>
    public SeriesMetadataMapping Clone()
    {
        return new SeriesMetadataMapping
        {
            LocalSeriesName = LocalSeriesName?.Trim() ?? string.Empty,
            TvdbSeriesId = TvdbSeriesId,
            TvdbSeriesName = TvdbSeriesName?.Trim() ?? string.Empty,
            OriginalLanguage = string.IsNullOrWhiteSpace(OriginalLanguage) ? null : OriginalLanguage.Trim()
        };
    }
}
