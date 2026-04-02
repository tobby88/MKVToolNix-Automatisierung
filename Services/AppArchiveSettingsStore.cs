namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Kapselt den dauerhaft gespeicherten Standardpfad der Serienbibliothek.
/// </summary>
public sealed class AppArchiveSettingsStore
{
    private readonly AppSettingsStore _settingsStore;

    /// <summary>
    /// Initialisiert den Archiv-Settings-Store mit dem standardmäßigen portablen Settings-Backend.
    /// </summary>
    public AppArchiveSettingsStore()
        : this(new AppSettingsStore())
    {
    }

    /// <summary>
    /// Initialisiert den Archiv-Settings-Store mit einem expliziten kombinierten Settings-Backend.
    /// </summary>
    /// <param name="settingsStore">Store für die gemeinsame portable Settings-Datei.</param>
    public AppArchiveSettingsStore(AppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    /// <summary>
    /// Lädt ausschließlich den Archivteil der kombinierten Einstellungen.
    /// </summary>
    /// <returns>Aktuelle Archiv-Einstellungen oder Standardwerte.</returns>
    public AppArchiveSettings Load()
    {
        return _settingsStore.Load().Archive?.Clone() ?? new AppArchiveSettings();
    }

    /// <summary>
    /// Speichert ausschließlich den Archivteil der kombinierten Einstellungen.
    /// </summary>
    /// <param name="settings">Zu speichernde Archiv-Einstellungen.</param>
    public void Save(AppArchiveSettings settings)
    {
        var normalizedSettings = settings?.Clone() ?? new AppArchiveSettings();
        _settingsStore.Update(combinedSettings => combinedSettings.Archive = normalizedSettings.Clone());
    }
}

/// <summary>
/// Persistente Einstellungen rund um die Archiv-/Bibliotheksintegration.
/// </summary>
public sealed class AppArchiveSettings
{
    /// <summary>
    /// Standardwurzel der Serienbibliothek.
    /// </summary>
    public string DefaultSeriesArchiveRootPath { get; set; } = SeriesArchiveService.DefaultArchiveRootDirectory;

    /// <summary>
    /// Erzeugt eine Kopie der Archiv-Einstellungen.
    /// </summary>
    /// <returns>Geklonter Einstellungssatz.</returns>
    public AppArchiveSettings Clone()
    {
        return new AppArchiveSettings
        {
            DefaultSeriesArchiveRootPath = DefaultSeriesArchiveRootPath
        };
    }
}
