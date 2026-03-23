using System.Text;
using System.Text.Json;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Services;

public static class AppSettingsFileLocator
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };
    private static readonly UTF8Encoding Utf8Encoding = new(encoderShouldEmitUTF8Identifier: false);

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
        return LoadCombinedSettingsInternal(captureCorruptSnapshots: false).Settings;
    }

    public static AppSettingsLoadResult LoadCombinedSettingsWithDiagnostics()
    {
        return LoadCombinedSettingsInternal(captureCorruptSnapshots: true);
    }

    public static void SaveCombinedSettings(CombinedAppSettings settings)
    {
        var settingsPath = GetSettingsFilePath();
        var backupPath = GetBackupFilePath();
        var temporaryPath = settingsPath + ".tmp";
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
        return GetSettingsFilePath() + ".bak";
    }

    private static AppSettingsLoadResult LoadCombinedSettingsInternal(bool captureCorruptSnapshots)
    {
        var settingsPath = GetSettingsFilePath();
        var primaryAttempt = TryLoadSettings(settingsPath);
        if (primaryAttempt.Success)
        {
            return new AppSettingsLoadResult(primaryAttempt.Settings!, AppSettingsLoadStatus.LoadedPrimary);
        }

        var backupPath = GetBackupFilePath();
        var backupAttempt = TryLoadSettings(backupPath);
        if (backupAttempt.Success)
        {
            var warningMessage = primaryAttempt.Exists
                ? BuildBackupWarningMessage(primaryAttempt, captureCorruptSnapshots)
                : null;

            return new AppSettingsLoadResult(
                backupAttempt.Settings!,
                AppSettingsLoadStatus.LoadedBackup,
                warningMessage);
        }

        var status = primaryAttempt.Exists || backupAttempt.Exists
            ? AppSettingsLoadStatus.LoadedDefaultsAfterFailure
            : AppSettingsLoadStatus.LoadedDefaultsNoFile;

        return new AppSettingsLoadResult(
            new CombinedAppSettings(),
            status,
            status == AppSettingsLoadStatus.LoadedDefaultsAfterFailure
                ? BuildDefaultsWarningMessage(primaryAttempt, backupAttempt, captureCorruptSnapshots)
                : null);
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
            return new AppSettingsLoadAttempt(filePath, Exists: true, Success: true, Settings: settings, ErrorMessage: null);
        }
        catch (Exception ex)
        {
            return new AppSettingsLoadAttempt(filePath, Exists: true, Success: false, Settings: null, ErrorMessage: ex.Message);
        }
    }

    private static string BuildBackupWarningMessage(AppSettingsLoadAttempt primaryAttempt, bool captureCorruptSnapshots)
    {
        var lines = new List<string>
        {
            $"Die Einstellungen aus '{Path.GetFileName(primaryAttempt.FilePath)}' konnten nicht gelesen werden.",
            "Die Sicherung 'settings.json.bak' wurde geladen."
        };

        AppendErrorDetails(lines, primaryAttempt);
        AppendCorruptSnapshotDetails(lines, primaryAttempt.FilePath, captureCorruptSnapshots);
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildDefaultsWarningMessage(
        AppSettingsLoadAttempt primaryAttempt,
        AppSettingsLoadAttempt backupAttempt,
        bool captureCorruptSnapshots)
    {
        var lines = new List<string>
        {
            "Die Einstellungen konnten weder aus 'settings.json' noch aus der Sicherung geladen werden.",
            "Die App startet deshalb mit Standardwerten."
        };

        AppendErrorDetails(lines, primaryAttempt);
        AppendErrorDetails(lines, backupAttempt);
        AppendCorruptSnapshotDetails(lines, primaryAttempt.FilePath, captureCorruptSnapshots);
        AppendCorruptSnapshotDetails(lines, backupAttempt.FilePath, captureCorruptSnapshots);
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

    private static void AppendCorruptSnapshotDetails(List<string> lines, string filePath, bool captureCorruptSnapshots)
    {
        if (!captureCorruptSnapshots)
        {
            return;
        }

        var snapshotPath = CreateCorruptSnapshot(filePath);
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
}

public sealed class CombinedAppSettings
{
    public AppMetadataSettings? Metadata { get; set; } = new();

    public AppToolPathSettings? ToolPaths { get; set; } = new();
}

public enum AppSettingsLoadStatus
{
    LoadedPrimary,
    LoadedBackup,
    LoadedDefaultsNoFile,
    LoadedDefaultsAfterFailure
}

public sealed record AppSettingsLoadResult(
    CombinedAppSettings Settings,
    AppSettingsLoadStatus Status,
    string? WarningMessage = null)
{
    public bool HasWarning => !string.IsNullOrWhiteSpace(WarningMessage);
}

internal sealed record AppSettingsLoadAttempt(
    string FilePath,
    bool Exists,
    bool Success,
    CombinedAppSettings? Settings,
    string? ErrorMessage);
