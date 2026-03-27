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

    public AppArchiveSettings Load()
    {
        return _settingsStore.Load().Archive?.Clone() ?? new AppArchiveSettings();
    }

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
    public string DefaultSeriesArchiveRootPath { get; set; } = SeriesArchiveService.DefaultArchiveRootDirectory;

    public AppArchiveSettings Clone()
    {
        return new AppArchiveSettings
        {
            DefaultSeriesArchiveRootPath = DefaultSeriesArchiveRootPath
        };
    }
}
