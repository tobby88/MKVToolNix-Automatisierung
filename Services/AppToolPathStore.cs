namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Liest und schreibt ausschließlich den Toolpfad-Teil der kombinierten Einstellungen.
/// </summary>
public sealed class AppToolPathStore
{
    private readonly AppSettingsStore _settingsStore;

    /// <summary>
    /// Initialisiert den Toolpfad-Store mit dem standardmäßigen portablen Settings-Backend.
    /// </summary>
    public AppToolPathStore()
        : this(new AppSettingsStore())
    {
    }

    /// <summary>
    /// Initialisiert den Toolpfad-Store mit einem expliziten kombinierten Settings-Backend.
    /// </summary>
    /// <param name="settingsStore">Store für die gemeinsame portable Settings-Datei.</param>
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
    /// Persistenter Zustand der automatisch verwalteten MKVToolNix-Installation.
    /// </summary>
    public ManagedToolSettings ManagedMkvToolNix { get; set; } = new();

    /// <summary>
    /// Persistenter Zustand der automatisch verwalteten ffprobe-Installation.
    /// </summary>
    public ManagedToolSettings ManagedFfprobe { get; set; } = new();

    /// <summary>
    /// Optionaler manueller Override-Pfad zur ffprobe-Executable.
    /// </summary>
    public string FfprobePath { get; set; } = string.Empty;

    /// <summary>
    /// Optionaler manueller Override-Pfad zu einer MKVToolNix-Executable oder zu ihrem Installationsordner.
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
            ManagedMkvToolNix = ManagedMkvToolNix.Clone(),
            ManagedFfprobe = ManagedFfprobe.Clone(),
            FfprobePath = FfprobePath,
            MkvToolNixDirectoryPath = MkvToolNixDirectoryPath
        };
    }
}

/// <summary>
/// Beschreibt den persistierten Zustand eines automatisch verwalteten Werkzeugs.
/// </summary>
public sealed class ManagedToolSettings
{
    /// <summary>
    /// Gibt an, ob die Anwendung dieses Werkzeug selbst herunterladen und aktualisieren soll.
    /// </summary>
    public bool AutoManageEnabled { get; set; } = true;

    /// <summary>
    /// Installationspfad der aktuell verwalteten Version.
    /// </summary>
    public string InstalledPath { get; set; } = string.Empty;

    /// <summary>
    /// Vergleichbarer Versions- oder Revisionsschlüssel der aktuell verwalteten Version.
    /// </summary>
    public string InstalledVersion { get; set; } = string.Empty;

    /// <summary>
    /// Zeitpunkt der letzten erfolgreichen Online-Prüfung auf eine neue Version.
    /// </summary>
    public DateTimeOffset? LastCheckedUtc { get; set; }

    /// <summary>
    /// Erzeugt eine tiefe Kopie des gespeicherten Toolzustands.
    /// </summary>
    /// <returns>Geklontes Toolobjekt.</returns>
    public ManagedToolSettings Clone()
    {
        return new ManagedToolSettings
        {
            AutoManageEnabled = AutoManageEnabled,
            InstalledPath = InstalledPath,
            InstalledVersion = InstalledVersion,
            LastCheckedUtc = LastCheckedUtc
        };
    }
}
