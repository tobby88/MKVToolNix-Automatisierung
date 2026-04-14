using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Services.Emby;

/// <summary>
/// Liest und schreibt ausschließlich den Emby-Teil der gemeinsamen portablen App-Einstellungen.
/// </summary>
public sealed class AppEmbySettingsStore
{
    private readonly AppSettingsStore _settingsStore;

    /// <summary>
    /// Initialisiert den Emby-Settings-Store mit dem standardmäßigen portablen Settings-Backend.
    /// </summary>
    public AppEmbySettingsStore()
        : this(new AppSettingsStore())
    {
    }

    /// <summary>
    /// Initialisiert den Emby-Settings-Store mit einem expliziten kombinierten Settings-Backend.
    /// </summary>
    /// <param name="settingsStore">Store für die gemeinsame portable Settings-Datei.</param>
    public AppEmbySettingsStore(AppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    /// <summary>
    /// Lädt ausschließlich den Emby-Teil der kombinierten Einstellungen.
    /// </summary>
    /// <returns>Aktuelle Emby-Einstellungen oder Standardwerte.</returns>
    public AppEmbySettings Load()
    {
        return _settingsStore.Load().Emby?.Clone() ?? new AppEmbySettings();
    }

    /// <summary>
    /// Speichert ausschließlich den Emby-Teil der kombinierten Einstellungen.
    /// </summary>
    /// <param name="settings">Zu speichernde Emby-Einstellungen.</param>
    public void Save(AppEmbySettings settings)
    {
        var normalizedSettings = settings?.Clone() ?? new AppEmbySettings();
        _settingsStore.Update(combinedSettings => combinedSettings.Emby = normalizedSettings);
    }

    /// <summary>
    /// Pfad der zugrunde liegenden portablen Settings-Datei.
    /// </summary>
    public string SettingsFilePath => AppSettingsFileLocator.GetSettingsFilePath();
}

/// <summary>
/// Persistente Einstellungen für den optionalen Abgleich gegen einen lokalen Emby-Server.
/// </summary>
public sealed class AppEmbySettings
{
    /// <summary>
    /// Standardadresse des lokalen Emby-Servers in diesem Projekt.
    /// </summary>
    public const string DefaultServerUrl = "http://t-emby:8096";

    /// <summary>
    /// Basisadresse des Emby-Servers, z. B. <c>http://t-emby:8096</c>.
    /// </summary>
    public string ServerUrl { get; set; } = DefaultServerUrl;

    /// <summary>
    /// Emby-API-Key für serverseitige Scans, Item-Suche und gezielte Metadaten-Refreshs.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Maximale Wartezeit, in der das Modul nach einem Library-Scan nach neu sichtbaren Emby-Items sucht.
    /// </summary>
    public int ScanWaitTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Erzeugt eine Kopie der Emby-Einstellungen.
    /// </summary>
    /// <returns>Geklonter Einstellungssatz.</returns>
    public AppEmbySettings Clone()
    {
        return new AppEmbySettings
        {
            ServerUrl = string.IsNullOrWhiteSpace(ServerUrl) ? DefaultServerUrl : ServerUrl.Trim(),
            ApiKey = ApiKey?.Trim() ?? string.Empty,
            ScanWaitTimeoutSeconds = Math.Clamp(ScanWaitTimeoutSeconds, 5, 600)
        };
    }
}
