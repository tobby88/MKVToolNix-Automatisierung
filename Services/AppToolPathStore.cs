using System.Text.Json;

namespace MkvToolnixAutomatisierung.Services;

public sealed class AppToolPathStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;

    public AppToolPathStore()
    {
        var rootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MkvToolnixAutomatisierung");
        Directory.CreateDirectory(rootDirectory);
        _settingsFilePath = Path.Combine(rootDirectory, "tool-paths.json");
    }

    public AppToolPathSettings Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return new AppToolPathSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<AppToolPathSettings>(json, SerializerOptions) ?? new AppToolPathSettings();
        }
        catch
        {
            return new AppToolPathSettings();
        }
    }

    public void Save(AppToolPathSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(_settingsFilePath, json);
    }
}

public sealed class AppToolPathSettings
{
    public string FfprobePath { get; set; } = string.Empty;

    public string MkvToolNixDirectoryPath { get; set; } = string.Empty;
}
