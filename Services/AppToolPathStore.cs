namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Liest und schreibt ausschließlich den Toolpfad-Teil der kombinierten Einstellungen.
/// </summary>
public sealed class AppToolPathStore
{
    private readonly AppSettingsStore _settingsStore;

    public AppToolPathStore()
        : this(new AppSettingsStore())
    {
    }

    public AppToolPathStore(AppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public AppToolPathSettings Load()
    {
        return _settingsStore.Load().ToolPaths?.Clone() ?? new AppToolPathSettings();
    }

    public void Save(AppToolPathSettings settings)
    {
        var normalizedSettings = settings?.Clone() ?? new AppToolPathSettings();
        _settingsStore.Update(combinedSettings => combinedSettings.ToolPaths = normalizedSettings.Clone());
    }

    public string SettingsFilePath => AppSettingsFileLocator.GetSettingsFilePath();
}

/// <summary>
/// Persistente Pfade für externe Werkzeuge, die die App optional oder zwingend benötigt.
/// </summary>
public sealed class AppToolPathSettings
{
    public string FfprobePath { get; set; } = string.Empty;

    public string MkvToolNixDirectoryPath { get; set; } = string.Empty;

    public AppToolPathSettings Clone()
    {
        return new AppToolPathSettings
        {
            FfprobePath = FfprobePath,
            MkvToolNixDirectoryPath = MkvToolNixDirectoryPath
        };
    }
}
