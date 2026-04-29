using MkvToolnixAutomatisierung.Services.Emby;
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
            var normalizedSettings = settings.Clone();
            Normalize(normalizedSettings);
            SaveCore(normalizedSettings);
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
            Normalize(updatedSettings);
            SaveCore(updatedSettings, preserveCachedWarning: true);
        }
    }

    private void EnsureLoaded()
    {
        if (_isLoaded)
        {
            return;
        }

        var loadResult = AppSettingsFileLocator.LoadCombinedSettingsWithDiagnostics();
        var normalizedSettings = loadResult.Settings.Clone();
        var hasLegacyToolPathCleanup = Normalize(normalizedSettings);
        var warningMessage = loadResult.WarningMessage;
        if (hasLegacyToolPathCleanup && ShouldPersistNormalizedSettings(loadResult.Status))
        {
            try
            {
                AppSettingsFileLocator.SaveCombinedSettings(normalizedSettings.Clone());
            }
            catch (Exception exception)
            {
                warningMessage = CombineWarningMessages(
                    warningMessage,
                    $"Bereinigte Legacy-Werkzeugpfade konnten nicht gespeichert werden: {exception.Message}");
            }
        }

        _cachedSettings = normalizedSettings.Clone();
        _cachedStatus = loadResult.Status;
        _cachedWarningMessage = warningMessage;
        _isLoaded = true;
    }

    private void SaveCore(CombinedAppSettings settings, bool preserveCachedWarning = false)
    {
        var warningMessage = preserveCachedWarning ? _cachedWarningMessage : null;
        AppSettingsFileLocator.SaveCombinedSettings(settings);
        _cachedSettings = settings.Clone();
        _cachedStatus = AppSettingsLoadStatus.LoadedPrimary;
        _cachedWarningMessage = warningMessage;
        _isLoaded = true;
    }

    private static bool Normalize(CombinedAppSettings settings)
    {
        var changed = false;

        if (settings.Metadata is null)
        {
            settings.Metadata = new AppMetadataSettings();
            changed = true;
        }

        if (settings.ToolPaths is null)
        {
            settings.ToolPaths = new AppToolPathSettings();
            changed = true;
        }

        if (settings.Archive is null)
        {
            settings.Archive = new AppArchiveSettings();
            changed = true;
        }

        if (settings.Emby is null)
        {
            settings.Emby = new AppEmbySettings();
            changed = true;
        }

        changed |= ManagedToolResolution.NormalizeLegacyDownloadOverrides(settings.ToolPaths);

        return changed;
    }

    private static bool ShouldPersistNormalizedSettings(AppSettingsLoadStatus status)
    {
        return status is AppSettingsLoadStatus.LoadedPrimary or AppSettingsLoadStatus.LoadedBackup;
    }

    private static string? CombineWarningMessages(params string?[] warningMessages)
    {
        var messages = warningMessages
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(message => message!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return messages.Length == 0
            ? null
            : string.Join(Environment.NewLine + Environment.NewLine, messages);
    }
}
