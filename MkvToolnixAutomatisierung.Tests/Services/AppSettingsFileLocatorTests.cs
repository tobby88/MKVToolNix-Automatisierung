using System.IO;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

[Collection("PortableStorage")]
public sealed class AppSettingsFileLocatorTests
{
    private readonly PortableStorageFixture _storageFixture;

    public AppSettingsFileLocatorTests(PortableStorageFixture storageFixture)
    {
        _storageFixture = storageFixture;
        _storageFixture.Reset();
    }

    [Fact]
    public void LoadCombinedSettingsWithDiagnostics_LoadsBackupAndCreatesCorruptSnapshot_WhenPrimaryIsBroken()
    {
        AppSettingsFileLocator.SaveCombinedSettings(new CombinedAppSettings
        {
            Metadata = new AppMetadataSettings
            {
                TvdbApiKey = "backup-key"
            }
        });

        AppSettingsFileLocator.SaveCombinedSettings(new CombinedAppSettings
        {
            Metadata = new AppMetadataSettings
            {
                TvdbApiKey = "current-key"
            }
        });

        File.WriteAllText(PortableAppStorage.SettingsFilePath, "{ invalid json");

        var result = AppSettingsFileLocator.LoadCombinedSettingsWithDiagnostics();

        Assert.Equal(AppSettingsLoadStatus.LoadedBackup, result.Status);
        Assert.Equal("backup-key", result.Settings.Metadata!.TvdbApiKey);
        Assert.True(result.HasWarning);
        Assert.Contains("settings.json.bak", result.WarningMessage);
        Assert.True(Directory.EnumerateFiles(PortableAppStorage.DataDirectory, "settings.corrupt-*.json").Any());
    }

    [Fact]
    public void LoadCombinedSettingsWithDiagnostics_ReturnsDefaults_WhenPrimaryAndBackupAreBroken()
    {
        PortableAppStorage.EnsureDataDirectoryForSave();
        File.WriteAllText(PortableAppStorage.SettingsFilePath, "{ invalid primary");
        File.WriteAllText(PortableAppStorage.SettingsBackupFilePath, "{ invalid backup");

        var result = AppSettingsFileLocator.LoadCombinedSettingsWithDiagnostics();

        Assert.Equal(AppSettingsLoadStatus.LoadedDefaultsAfterFailure, result.Status);
        Assert.Equal(string.Empty, result.Settings.Metadata!.TvdbApiKey);
        Assert.True(result.HasWarning);
        Assert.True(Directory.EnumerateFiles(PortableAppStorage.DataDirectory, "settings.corrupt-*.json").Count() >= 1);
    }

    [Fact]
    public void SaveCombinedSettings_DoesNotLeaveTemporaryFilesBehind()
    {
        AppSettingsFileLocator.SaveCombinedSettings(new CombinedAppSettings
        {
            Metadata = new AppMetadataSettings
            {
                TvdbApiKey = "abc"
            }
        });

        Assert.True(File.Exists(PortableAppStorage.SettingsFilePath));
        Assert.Empty(Directory.EnumerateFiles(PortableAppStorage.DataDirectory, "settings.json.tmp-*"));
    }

    [Fact]
    public void SaveCombinedSettings_PreservesLastGoodBackup_WhenPrimaryIsCorrupt()
    {
        AppSettingsFileLocator.SaveCombinedSettings(new CombinedAppSettings
        {
            Metadata = new AppMetadataSettings
            {
                TvdbApiKey = "backup-key"
            }
        });

        AppSettingsFileLocator.SaveCombinedSettings(new CombinedAppSettings
        {
            Metadata = new AppMetadataSettings
            {
                TvdbApiKey = "current-key"
            }
        });

        File.WriteAllText(PortableAppStorage.SettingsFilePath, "{ invalid primary");

        AppSettingsFileLocator.SaveCombinedSettings(new CombinedAppSettings
        {
            Metadata = new AppMetadataSettings
            {
                TvdbApiKey = "recovered-key"
            }
        });

        var backup = AppSettingsFileLocator.LoadCombinedSettingsWithDiagnostics();

        File.WriteAllText(PortableAppStorage.SettingsFilePath, "{ invalid primary again");
        var recoveredFromBackup = AppSettingsFileLocator.LoadCombinedSettingsWithDiagnostics();

        Assert.Equal("recovered-key", backup.Settings.Metadata!.TvdbApiKey);
        Assert.Equal(AppSettingsLoadStatus.LoadedBackup, recoveredFromBackup.Status);
        Assert.Equal("backup-key", recoveredFromBackup.Settings.Metadata!.TvdbApiKey);
    }

    [Fact]
    public void LoadCombinedSettingsWithDiagnostics_WritesBundledReadme_WhenMissing()
    {
        if (File.Exists(PortableAppStorage.ReadmeFilePath))
        {
            File.Delete(PortableAppStorage.ReadmeFilePath);
        }

        var result = AppSettingsFileLocator.LoadCombinedSettingsWithDiagnostics();

        Assert.Equal(AppSettingsLoadStatus.LoadedDefaultsNoFile, result.Status);
        Assert.True(File.Exists(PortableAppStorage.ReadmeFilePath));
        Assert.Contains(
            "MKVToolNix-Automatisierung",
            File.ReadAllText(PortableAppStorage.ReadmeFilePath),
            StringComparison.Ordinal);
    }
}
