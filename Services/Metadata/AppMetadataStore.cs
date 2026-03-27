using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Services.Metadata;

public sealed class AppMetadataStore
{
    private readonly AppSettingsStore _settingsStore;

    public AppMetadataStore()
        : this(new AppSettingsStore())
    {
    }

    public AppMetadataStore(AppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public AppMetadataSettings Load()
    {
        return _settingsStore.Load().Metadata?.Clone() ?? new AppMetadataSettings();
    }

    public void Save(AppMetadataSettings settings)
    {
        var normalizedSettings = settings?.Clone() ?? new AppMetadataSettings();
        _settingsStore.Update(combinedSettings => combinedSettings.Metadata = normalizedSettings.Clone());
    }

    public string SettingsFilePath => AppSettingsFileLocator.GetSettingsFilePath();
}

public sealed class AppMetadataSettings
{
    public string TvdbApiKey { get; set; } = string.Empty;

    public string TvdbPin { get; set; } = string.Empty;

    public List<SeriesMetadataMapping> SeriesMappings { get; set; } = [];

    public AppMetadataSettings Clone()
    {
        return new AppMetadataSettings
        {
            TvdbApiKey = TvdbApiKey,
            TvdbPin = TvdbPin,
            SeriesMappings = SeriesMappings.Select(mapping => mapping.Clone()).ToList()
        };
    }
}

public sealed class SeriesMetadataMapping
{
    public string LocalSeriesName { get; set; } = string.Empty;

    public int TvdbSeriesId { get; set; }

    public string TvdbSeriesName { get; set; } = string.Empty;

    public SeriesMetadataMapping Clone()
    {
        return new SeriesMetadataMapping
        {
            LocalSeriesName = LocalSeriesName,
            TvdbSeriesId = TvdbSeriesId,
            TvdbSeriesName = TvdbSeriesName
        };
    }
}
