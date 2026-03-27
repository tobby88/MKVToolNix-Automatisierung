using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Hält die kombinierten App-Einstellungen im Speicher und serialisiert Zugriffe auf die JSON-Datei.
/// </summary>
public sealed class AppSettingsStore
{
    private readonly object _sync = new();
    private CombinedAppSettings? _cachedSettings;
    private AppSettingsLoadStatus _cachedStatus;
    private string? _cachedWarningMessage;
    private bool _isLoaded;

    /// <summary>
    /// Lädt die kombinierten App-Einstellungen aus dem portablen JSON-Speicher.
    /// </summary>
    /// <returns>Klon der aktuell geladenen Einstellungen.</returns>
    public CombinedAppSettings Load()
    {
        lock (_sync)
        {
            EnsureLoaded();
            return _cachedSettings!.Clone();
        }
    }

    /// <summary>
    /// Lädt die Einstellungen inklusive Diagnoseinformationen über Primär-, Backup- oder Fallback-Ladevorgänge.
    /// </summary>
    /// <returns>Einstellungen zusammen mit Ladequelle und möglicher Warnmeldung.</returns>
    public AppSettingsLoadResult LoadWithDiagnostics()
    {
        lock (_sync)
        {
            EnsureLoaded();
            return new AppSettingsLoadResult(_cachedSettings!.Clone(), _cachedStatus, _cachedWarningMessage);
        }
    }

    /// <summary>
    /// Persistiert einen vollständigen Satz App-Einstellungen und aktualisiert den In-Memory-Cache.
    /// </summary>
    /// <param name="settings">Zu speichernder Gesamtsatz an Einstellungen.</param>
    public void Save(CombinedAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_sync)
        {
            SaveCore(Normalize(settings.Clone()));
        }
    }

    /// <summary>
    /// Lädt die aktuellen Einstellungen, wendet eine Mutation an und speichert das Ergebnis atomar zurück.
    /// </summary>
    /// <param name="updateAction">Mutation, die auf einen geklonten Einstellungssatz angewendet wird.</param>
    public void Update(Action<CombinedAppSettings> updateAction)
    {
        ArgumentNullException.ThrowIfNull(updateAction);

        lock (_sync)
        {
            EnsureLoaded();
            var updatedSettings = _cachedSettings!.Clone();
            updateAction(updatedSettings);
            SaveCore(Normalize(updatedSettings));
        }
    }

    private void EnsureLoaded()
    {
        if (_isLoaded)
        {
            return;
        }

        var loadResult = AppSettingsFileLocator.LoadCombinedSettingsWithDiagnostics();
        _cachedSettings = Normalize(loadResult.Settings).Clone();
        _cachedStatus = loadResult.Status;
        _cachedWarningMessage = loadResult.WarningMessage;
        _isLoaded = true;
    }

    private void SaveCore(CombinedAppSettings settings)
    {
        AppSettingsFileLocator.SaveCombinedSettings(settings);
        _cachedSettings = settings.Clone();
        _cachedStatus = AppSettingsLoadStatus.LoadedPrimary;
        _cachedWarningMessage = null;
        _isLoaded = true;
    }

    private static CombinedAppSettings Normalize(CombinedAppSettings settings)
    {
        settings.Metadata ??= new AppMetadataSettings();
        settings.ToolPaths ??= new AppToolPathSettings();
        settings.Archive ??= new AppArchiveSettings();
        return settings;
    }
}
