using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Services.Metadata;

public sealed class AppMetadataStore
{
    public AppMetadataSettings Load()
    {
        return AppSettingsFileLocator.LoadCombinedSettings().Metadata ?? new AppMetadataSettings();
    }

    public void Save(AppMetadataSettings settings)
    {
        var combinedSettings = AppSettingsFileLocator.LoadCombinedSettings();
        combinedSettings.Metadata = settings;
        AppSettingsFileLocator.SaveCombinedSettings(combinedSettings);
    }

    public string SettingsFilePath => AppSettingsFileLocator.GetSettingsFilePath();
}

public sealed class AppMetadataSettings
{
    public string TvdbApiKey { get; set; } = string.Empty;

    public string TvdbPin { get; set; } = string.Empty;

    public List<SeriesMetadataMapping> SeriesMappings { get; set; } = [];
}

public sealed class SeriesMetadataMapping
{
    public string LocalSeriesName { get; set; } = string.Empty;

    public int TvdbSeriesId { get; set; }

    public string TvdbSeriesName { get; set; } = string.Empty;
}
