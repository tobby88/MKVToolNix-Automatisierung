using System.Text.Json;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Services;

public static class AppSettingsFileLocator
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static string GetSettingsFilePath()
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
        var directory = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return settingsPath;
    }

    public static CombinedAppSettings LoadCombinedSettings()
    {
        var settingsPath = GetSettingsFilePath();
        if (!File.Exists(settingsPath))
        {
            return new CombinedAppSettings();
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            return JsonSerializer.Deserialize<CombinedAppSettings>(json, SerializerOptions) ?? new CombinedAppSettings();
        }
        catch
        {
            return new CombinedAppSettings();
        }
    }

    public static void SaveCombinedSettings(CombinedAppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(GetSettingsFilePath(), json);
    }
}

public sealed class CombinedAppSettings
{
    public AppMetadataSettings Metadata { get; set; } = new();

    public AppToolPathSettings ToolPaths { get; set; } = new();
}
