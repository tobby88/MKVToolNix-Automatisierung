using System.Text.Json;

namespace MkvToolnixAutomatisierung.Services.Metadata;

public sealed class AppMetadataStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;

    public AppMetadataStore()
    {
        var rootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MkvToolnixAutomatisierung");
        Directory.CreateDirectory(rootDirectory);
        _settingsFilePath = Path.Combine(rootDirectory, "metadata-settings.json");
    }

    public AppMetadataSettings Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return new AppMetadataSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<AppMetadataSettings>(json, SerializerOptions) ?? new AppMetadataSettings();
        }
        catch
        {
            return new AppMetadataSettings();
        }
    }

    public void Save(AppMetadataSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(_settingsFilePath, json);
    }

    public string SettingsFilePath => _settingsFilePath;
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
