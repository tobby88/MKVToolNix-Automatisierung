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

    /// <summary>
    /// Lädt ausschließlich den Toolpfad-Teil der kombinierten Einstellungen.
    /// </summary>
    /// <returns>Aktuelle Toolpfade oder Standardwerte.</returns>
    public AppToolPathSettings Load()
    {
        return _settingsStore.Load().ToolPaths?.Clone() ?? new AppToolPathSettings();
    }

    /// <summary>
    /// Speichert ausschließlich den Toolpfad-Teil der kombinierten Einstellungen.
    /// </summary>
    /// <param name="settings">Zu speichernde Toolpfade.</param>
    public void Save(AppToolPathSettings settings)
    {
        var normalizedSettings = settings?.Clone() ?? new AppToolPathSettings();
        _settingsStore.Update(combinedSettings => combinedSettings.ToolPaths = normalizedSettings.Clone());
    }

    /// <summary>
    /// Pfad der zugrunde liegenden portablen Settings-Datei.
    /// </summary>
    public string SettingsFilePath => AppSettingsFileLocator.GetSettingsFilePath();
}

/// <summary>
/// Persistente Pfade für externe Werkzeuge, die die App optional oder zwingend benötigt.
/// </summary>
public sealed class AppToolPathSettings
{
    /// <summary>
    /// Optionaler Pfad zur ffprobe-Executable.
    /// </summary>
    public string FfprobePath { get; set; } = string.Empty;

    /// <summary>
    /// Pfad zur mkvmerge-Executable oder zu ihrem Installationsordner.
    /// </summary>
    public string MkvToolNixDirectoryPath { get; set; } = string.Empty;

    /// <summary>
    /// Erzeugt eine Kopie der Toolpfad-Einstellungen.
    /// </summary>
    /// <returns>Geklonter Einstellungssatz.</returns>
    public AppToolPathSettings Clone()
    {
        return new AppToolPathSettings
        {
            FfprobePath = FfprobePath,
            MkvToolNixDirectoryPath = MkvToolNixDirectoryPath
        };
    }
}
