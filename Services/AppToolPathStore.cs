namespace MkvToolnixAutomatisierung.Services;

public sealed class AppToolPathStore
{
    public AppToolPathSettings Load()
    {
        return AppSettingsFileLocator.LoadCombinedSettings().ToolPaths ?? new AppToolPathSettings();
    }

    public void Save(AppToolPathSettings settings)
    {
        var combinedSettings = AppSettingsFileLocator.LoadCombinedSettings();
        combinedSettings.ToolPaths = settings;
        AppSettingsFileLocator.SaveCombinedSettings(combinedSettings);
    }

    public string SettingsFilePath => AppSettingsFileLocator.GetSettingsFilePath();
}

public sealed class AppToolPathSettings
{
    public string FfprobePath { get; set; } = string.Empty;

    public string MkvToolNixDirectoryPath { get; set; } = string.Empty;
}
