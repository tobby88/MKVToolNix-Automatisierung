using System.Text;
using System.Text.Json;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Kapselt die portable Dateiablage für Einstellungen inklusive Backup- und Korruptions-Fallback.
/// </summary>
public static class AppSettingsFileLocator
{
    /// <summary>
    /// Gemeinsame Serialisierungsoptionen für die portable Settings-Datei.
    /// </summary>
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static readonly UTF8Encoding Utf8Encoding = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Liefert den Pfad der primären portablen Settings-Datei.
    /// </summary>
    /// <returns>Absoluter Pfad zu <c>Data\settings.json</c>.</returns>
    public static string GetSettingsFilePath()
    {
        return PortableAppStorage.SettingsFilePath;
    }

    /// <summary>
    /// Lädt die kombinierten Einstellungen ohne zusätzliche Diagnoseinformationen.
    /// </summary>
    /// <returns>Geladener oder auf Standardwerte normalisierter Einstellungssatz.</returns>
    public static CombinedAppSettings LoadCombinedSettings()
    {
        return LoadCombinedSettingsInternal(captureCorruptSnapshots: false).Settings;
    }

    /// <summary>
    /// Lädt die kombinierten Einstellungen samt Status- und Warninformationen.
    /// </summary>
    /// <returns>Ergebnisobjekt mit Einstellungen, Ladequelle und optionalen Warnungen.</returns>
    public static AppSettingsLoadResult LoadCombinedSettingsWithDiagnostics()
    {
        return LoadCombinedSettingsInternal(captureCorruptSnapshots: true);
    }

    /// <summary>
    /// Speichert die kombinierten Einstellungen atomar inklusive Backup der bisherigen Primärdatei.
    /// </summary>
    /// <param name="settings">Zu speichernder Einstellungssatz.</param>
    public static void SaveCombinedSettings(CombinedAppSettings settings)
    {
        var settingsPath = GetSettingsFilePath();
        var backupPath = GetBackupFilePath();
        var temporaryPath = settingsPath + ".tmp";
        PortableAppStorage.EnsureDataDirectoryForSave();
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(temporaryPath, json, Utf8Encoding);

        if (File.Exists(settingsPath))
        {
            File.Copy(settingsPath, backupPath, overwrite: true);
        }

        File.Move(temporaryPath, settingsPath, overwrite: true);
    }

    private static string GetBackupFilePath()
    {
        return PortableAppStorage.SettingsBackupFilePath;
    }

    private static AppSettingsLoadResult LoadCombinedSettingsInternal(bool captureCorruptSnapshots)
    {
        var storageWarningMessage = PortableAppStorage.PrepareForStartup();
        var primaryAttempt = TryLoadSettings(GetSettingsFilePath());
        if (primaryAttempt.Success)
        {
            return new AppSettingsLoadResult(
                primaryAttempt.Settings!,
                AppSettingsLoadStatus.LoadedPrimary,
                storageWarningMessage);
        }

        var backupAttempt = TryLoadSettings(GetBackupFilePath());
        if (backupAttempt.Success)
        {
            return new AppSettingsLoadResult(
                backupAttempt.Settings!,
                AppSettingsLoadStatus.LoadedBackup,
                CombineWarningMessages(
                    storageWarningMessage,
                    BuildBackupWarningMessage(primaryAttempt, backupAttempt, captureCorruptSnapshots)));
        }

        var status = primaryAttempt.Exists || backupAttempt.Exists
            ? AppSettingsLoadStatus.LoadedDefaultsAfterFailure
            : AppSettingsLoadStatus.LoadedDefaultsNoFile;

        return new AppSettingsLoadResult(
            new CombinedAppSettings(),
            status,
            status == AppSettingsLoadStatus.LoadedDefaultsAfterFailure
                ? CombineWarningMessages(
                    storageWarningMessage,
                    BuildDefaultsWarningMessage(primaryAttempt, backupAttempt, captureCorruptSnapshots))
                : storageWarningMessage);
    }

    private static AppSettingsLoadAttempt TryLoadSettings(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new AppSettingsLoadAttempt(filePath, Exists: false, Success: false, Settings: null, ErrorMessage: null);
        }

        try
        {
            var json = File.ReadAllText(filePath, Utf8Encoding);
            var settings = JsonSerializer.Deserialize<CombinedAppSettings>(json, SerializerOptions) ?? new CombinedAppSettings();
            settings.Metadata ??= new AppMetadataSettings();
            settings.ToolPaths ??= new AppToolPathSettings();
            settings.Archive ??= new AppArchiveSettings();
            return new AppSettingsLoadAttempt(filePath, Exists: true, Success: true, Settings: settings, ErrorMessage: null);
        }
        catch (Exception ex)
        {
            return new AppSettingsLoadAttempt(filePath, Exists: true, Success: false, Settings: null, ErrorMessage: ex.Message);
        }
    }

    private static string BuildBackupWarningMessage(
        AppSettingsLoadAttempt primaryAttempt,
        AppSettingsLoadAttempt backupAttempt,
        bool captureCorruptSnapshots)
    {
        var lines = new List<string>
        {
            "Die normalen Einstellungen konnten nicht gelesen werden.",
            $"Die Sicherung '{Path.GetFileName(backupAttempt.FilePath)}' wurde geladen."
        };

        AppendErrorDetails(lines, primaryAttempt);
        AppendCorruptSnapshotDetails(lines, primaryAttempt, captureCorruptSnapshots);
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildDefaultsWarningMessage(
        AppSettingsLoadAttempt primaryAttempt,
        AppSettingsLoadAttempt backupAttempt,
        bool captureCorruptSnapshots)
    {
        var lines = new List<string>
        {
            "Die Einstellungen konnten nicht gelesen werden.",
            "Die App startet deshalb mit Standardwerten."
        };

        AppendErrorDetails(lines, primaryAttempt);
        AppendErrorDetails(lines, backupAttempt);
        AppendCorruptSnapshotDetails(lines, primaryAttempt, captureCorruptSnapshots);
        AppendCorruptSnapshotDetails(lines, backupAttempt, captureCorruptSnapshots);
        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendErrorDetails(List<string> lines, AppSettingsLoadAttempt attempt)
    {
        if (!attempt.Exists || string.IsNullOrWhiteSpace(attempt.ErrorMessage))
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add($"{Path.GetFileName(attempt.FilePath)}: {attempt.ErrorMessage}");
    }

    private static void AppendCorruptSnapshotDetails(List<string> lines, AppSettingsLoadAttempt attempt, bool captureCorruptSnapshots)
    {
        if (!captureCorruptSnapshots || !attempt.Exists)
        {
            return;
        }

        var snapshotPath = CreateCorruptSnapshot(attempt.FilePath);
        if (string.IsNullOrWhiteSpace(snapshotPath))
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add("Defekte Datei gesichert unter:");
        lines.Add(snapshotPath);
    }

    private static string? CreateCorruptSnapshot(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var snapshotPath = Path.Combine(directory, $"{fileName}.corrupt-{timestamp}{extension}");
            var suffix = 2;

            while (File.Exists(snapshotPath))
            {
                snapshotPath = Path.Combine(directory, $"{fileName}.corrupt-{timestamp}-{suffix}{extension}");
                suffix++;
            }

            File.Copy(filePath, snapshotPath, overwrite: false);
            return snapshotPath;
        }
        catch
        {
            return null;
        }
    }

    private static string? CombineWarningMessages(params string?[] warningMessages)
    {
        var messages = warningMessages
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(message => message!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return messages.Count == 0
            ? null
            : string.Join(Environment.NewLine + Environment.NewLine, messages);
    }
}

/// <summary>
/// Vereint alle dauerhaft gespeicherten App-Bereiche in einem gemeinsamen JSON-Dokument.
/// </summary>
public sealed class CombinedAppSettings
{
    /// <summary>
    /// Persistente TVDB-Zugangsdaten und Serien-Mappings.
    /// </summary>
    public AppMetadataSettings? Metadata { get; set; } = new();

    /// <summary>
    /// Persistente Toolpfade für externe Abhängigkeiten.
    /// </summary>
    public AppToolPathSettings? ToolPaths { get; set; } = new();

    /// <summary>
    /// Persistente Archiv-/Bibliothekseinstellungen.
    /// </summary>
    public AppArchiveSettings? Archive { get; set; } = new();

    /// <summary>
    /// Erzeugt eine tiefe Kopie des kombinierten Einstellungssatzes.
    /// </summary>
    /// <returns>Neues Objekt mit geklonten Teilbereichen.</returns>
    public CombinedAppSettings Clone()
    {
        return new CombinedAppSettings
        {
            Metadata = Metadata?.Clone() ?? new AppMetadataSettings(),
            ToolPaths = ToolPaths?.Clone() ?? new AppToolPathSettings(),
            Archive = Archive?.Clone() ?? new AppArchiveSettings()
        };
    }
}

/// <summary>
/// Beschreibt, aus welcher Quelle die App-Einstellungen erfolgreich geladen wurden.
/// </summary>
public enum AppSettingsLoadStatus
{
    LoadedPrimary,
    LoadedBackup,
    LoadedDefaultsNoFile,
    LoadedDefaultsAfterFailure
}

/// <summary>
/// Liefert geladene Einstellungen zusammen mit Diagnoseinformationen für den Startdialog.
/// </summary>
public sealed record AppSettingsLoadResult(
    CombinedAppSettings Settings,
    AppSettingsLoadStatus Status,
    string? WarningMessage = null)
{
    /// <summary>
    /// Kennzeichnet, ob zum Laden eine Warnung an den Aufrufer weitergegeben werden sollte.
    /// </summary>
    public bool HasWarning => !string.IsNullOrWhiteSpace(WarningMessage);
}

/// <summary>
/// Interner Zwischenschritt eines einzelnen Ladeversuchs für Primär- oder Backup-Datei.
/// </summary>
internal sealed record AppSettingsLoadAttempt(
    string FilePath,
    bool Exists,
    bool Success,
    CombinedAppSettings? Settings,
    string? ErrorMessage);
