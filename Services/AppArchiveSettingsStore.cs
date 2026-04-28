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
    /// Bewusst abgelehnte Archivpflege-Vorschläge. Ein Eintrag gilt nur für dieselbe Datei,
    /// dieselbe Änderungsart und genau dasselbe Ist-/Soll-Paar; neue Regeln bleiben dadurch sichtbar.
    /// </summary>
    public List<ArchiveMaintenanceSuppressedChange> SuppressedMaintenanceChanges { get; set; } = [];

    /// <summary>
    /// Erzeugt eine Kopie der Archiv-Einstellungen.
    /// </summary>
    /// <returns>Geklonter Einstellungssatz.</returns>
    public AppArchiveSettings Clone()
    {
        return new AppArchiveSettings
        {
            DefaultSeriesArchiveRootPath = DefaultSeriesArchiveRootPath,
            SuppressedMaintenanceChanges = (SuppressedMaintenanceChanges ?? [])
                .Select(change => change.Clone())
                .ToList()
        };
    }
}

/// <summary>
/// Persistierte Ablehnung eines einzelnen Archivpflege-Vorschlags.
/// </summary>
public sealed class ArchiveMaintenanceSuppressedChange
{
    /// <summary>
    /// MKV-Pfad, für den der Vorschlag abgelehnt wurde.
    /// </summary>
    public string MediaFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Fachliche Änderungsart, z. B. Dateiname oder MKV-Titel.
    /// </summary>
    public string ChangeKind { get; set; } = string.Empty;

    /// <summary>
    /// Ist-Wert beim Ablehnen.
    /// </summary>
    public string CurrentValue { get; set; } = string.Empty;

    /// <summary>
    /// Damals vorgeschlagener Soll-Wert.
    /// </summary>
    public string SuggestedValue { get; set; } = string.Empty;

    /// <summary>
    /// Erzeugt eine Kopie des Ablehnungseintrags.
    /// </summary>
    /// <returns>Geklonter Eintrag.</returns>
    public ArchiveMaintenanceSuppressedChange Clone()
    {
        return new ArchiveMaintenanceSuppressedChange
        {
            MediaFilePath = MediaFilePath,
            ChangeKind = ChangeKind,
            CurrentValue = CurrentValue,
            SuggestedValue = SuggestedValue
        };
    }
}
