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
        return PortableAppStorage.SettingsFilePath;
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

public sealed class CombinedAppSettings
{
    public AppMetadataSettings? Metadata { get; set; } = new();

    public AppToolPathSettings? ToolPaths { get; set; } = new();

    public AppArchiveSettings? Archive { get; set; } = new();

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
