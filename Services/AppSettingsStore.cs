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

    public CombinedAppSettings Load()
    {
        lock (_sync)
        {
            EnsureLoaded();
            return _cachedSettings!.Clone();
        }
    }

    public AppSettingsLoadResult LoadWithDiagnostics()
    {
        lock (_sync)
        {
            EnsureLoaded();
            return new AppSettingsLoadResult(_cachedSettings!.Clone(), _cachedStatus, _cachedWarningMessage);
        }
    }

    public void Save(CombinedAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_sync)
        {
            SaveCore(Normalize(settings.Clone()));
        }
    }

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
