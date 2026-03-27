namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Kapselt den dauerhaft gespeicherten Standardpfad der Serienbibliothek.
/// </summary>
public sealed class AppArchiveSettingsStore
{
    private readonly AppSettingsStore _settingsStore;

    public AppArchiveSettingsStore()
        : this(new AppSettingsStore())
    {
    }

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
