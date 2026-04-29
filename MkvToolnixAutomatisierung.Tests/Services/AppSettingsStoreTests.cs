using System.IO;
using System.Text.Json;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Emby;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

[Collection("PortableStorage")]
public sealed class AppSettingsStoreTests
{
    private readonly PortableStorageFixture _storageFixture;

    public AppSettingsStoreTests(PortableStorageFixture storageFixture)
    {
        _storageFixture = storageFixture;
        _storageFixture.Reset();
    }

    [Fact]
    public void Load_ReturnsDefaultsWhenNoSettingsFileExists()
    {
        var store = new AppSettingsStore();

        var settings = store.Load();

        Assert.NotNull(settings.Metadata);
        Assert.NotNull(settings.ToolPaths);
        Assert.NotNull(settings.Archive);
        Assert.NotNull(settings.Emby);
        Assert.Equal(string.Empty, settings.Metadata!.TvdbApiKey);
        Assert.Equal(string.Empty, settings.ToolPaths!.FfprobePath);
        Assert.Equal(SeriesArchiveService.DefaultArchiveRootDirectory, settings.Archive!.DefaultSeriesArchiveRootPath);
        Assert.Equal("http://t-emby:8096", settings.Emby!.ServerUrl);
    }

    [Fact]
    public void Save_PersistsSettingsToPortableSettingsFile()
    {
        var store = new AppSettingsStore();
        var settings = new CombinedAppSettings
        {
            Metadata = new AppMetadataSettings
            {
                TvdbApiKey = "abc",
                TvdbPin = "1234"
            },
            ToolPaths = new AppToolPathSettings
            {
                FfprobePath = @"C:\Tools\ffprobe.exe",
                MkvToolNixDirectoryPath = @"C:\Tools\MKVToolNix"
            },
            Archive = new AppArchiveSettings
            {
                DefaultSeriesArchiveRootPath = @"Z:\Serien"
            },
            Emby = new AppEmbySettings
            {
                ServerUrl = "http://emby.local:8096",
                ApiKey = "emby-key"
            }
        };

        store.Save(settings);

        Assert.True(File.Exists(PortableAppStorage.SettingsFilePath));
        var persisted = JsonSerializer.Deserialize<CombinedAppSettings>(
            File.ReadAllText(PortableAppStorage.SettingsFilePath),
            AppSettingsFileLocator.SerializerOptions);

        Assert.NotNull(persisted);
        Assert.Equal("abc", persisted!.Metadata!.TvdbApiKey);
        Assert.Equal(@"C:\Tools\ffprobe.exe", persisted.ToolPaths!.FfprobePath);
        Assert.Equal(@"Z:\Serien", persisted.Archive!.DefaultSeriesArchiveRootPath);
        Assert.Equal("http://emby.local:8096", persisted.Emby!.ServerUrl);
        Assert.Equal("emby-key", persisted.Emby.ApiKey);
    }

    [Fact]
    public void Update_UsesCachedStateAndWritesMergedSettings()
    {
        var store = new AppSettingsStore();
        store.Save(new CombinedAppSettings
        {
            Metadata = new AppMetadataSettings
            {
                TvdbApiKey = "initial-key"
            },
            ToolPaths = new AppToolPathSettings
            {
                MkvToolNixDirectoryPath = @"C:\Tools\MKVToolNix"
            }
        });

        File.WriteAllText(PortableAppStorage.SettingsFilePath, "{ invalid json");

        store.Update(settings =>
        {
            settings.Metadata!.TvdbPin = "9999";
            settings.Archive!.DefaultSeriesArchiveRootPath = @"Y:\Archiv";
            settings.Emby!.ApiKey = "emby-updated";
        });

        var reloaded = JsonSerializer.Deserialize<CombinedAppSettings>(
            File.ReadAllText(PortableAppStorage.SettingsFilePath),
            AppSettingsFileLocator.SerializerOptions);

        Assert.NotNull(reloaded);
        Assert.Equal("initial-key", reloaded!.Metadata!.TvdbApiKey);
        Assert.Equal("9999", reloaded.Metadata.TvdbPin);
        Assert.Equal(@"C:\Tools\MKVToolNix", reloaded.ToolPaths!.MkvToolNixDirectoryPath);
        Assert.Equal(@"Y:\Archiv", reloaded.Archive!.DefaultSeriesArchiveRootPath);
        Assert.Equal("emby-updated", reloaded.Emby!.ApiKey);
    }

    [Fact]
    public void Update_PreservesLoadWarningAfterBackupRecovery()
    {
        PortableAppStorage.EnsureDataDirectoryForSave();
        File.WriteAllText(PortableAppStorage.SettingsFilePath, "{ invalid json");
        File.WriteAllText(
            PortableAppStorage.SettingsBackupFilePath,
            JsonSerializer.Serialize(
                new CombinedAppSettings
                {
                    ToolPaths = new AppToolPathSettings
                    {
                        FfprobePath = @"C:\Tools\ffprobe.exe"
                    }
                },
                AppSettingsFileLocator.SerializerOptions));

        var store = new AppSettingsStore();
        var initialLoad = store.LoadWithDiagnostics();
        Assert.True(initialLoad.HasWarning);
        Assert.Equal(AppSettingsLoadStatus.LoadedBackup, initialLoad.Status);

        new AppToolPathStore(store).Save(new AppToolPathSettings
        {
            FfprobePath = @"C:\Tools\ffprobe-new.exe"
        });

        var afterToolStateSave = store.LoadWithDiagnostics();
        Assert.True(afterToolStateSave.HasWarning);
        Assert.Contains("settings.json.bak", afterToolStateSave.WarningMessage, StringComparison.Ordinal);
        Assert.Equal(AppSettingsLoadStatus.LoadedPrimary, afterToolStateSave.Status);
        Assert.Equal(@"C:\Tools\ffprobe-new.exe", afterToolStateSave.Settings.ToolPaths!.FfprobePath);
    }

    [Fact]
    public void Load_ClearsLegacyDownloadOverridesWhenAutoManageIsEnabled()
    {
        var userProfileDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-store-legacy", Guid.NewGuid().ToString("N"));
        var downloadsDirectory = Path.Combine(userProfileDirectory, "Downloads");
        var mkvToolNixDirectory = Path.Combine(downloadsDirectory, "mkvtoolnix-64-bit-999.0", "mkvtoolnix");
        var ffprobePath = Path.Combine(downloadsDirectory, "ffmpeg", "bin", "ffprobe.exe");
        var originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        var originalHome = Environment.GetEnvironmentVariable("HOME");

        try
        {
            Environment.SetEnvironmentVariable("USERPROFILE", userProfileDirectory);
            Environment.SetEnvironmentVariable("HOME", userProfileDirectory);

            Directory.CreateDirectory(mkvToolNixDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(ffprobePath)!);

            AppSettingsFileLocator.SaveCombinedSettings(new CombinedAppSettings
            {
                ToolPaths = new AppToolPathSettings
                {
                    MkvToolNixDirectoryPath = mkvToolNixDirectory,
                    FfprobePath = ffprobePath,
                    ManagedMkvToolNix = new ManagedToolSettings
                    {
                        AutoManageEnabled = true
                    },
                    ManagedFfprobe = new ManagedToolSettings
                    {
                        AutoManageEnabled = true
                    }
                }
            });

            var loadedSettings = new AppSettingsStore().Load();
            var persistedSettings = AppSettingsFileLocator.LoadCombinedSettingsWithDiagnostics().Settings;

            Assert.Equal(string.Empty, loadedSettings.ToolPaths!.MkvToolNixDirectoryPath);
            Assert.Equal(string.Empty, loadedSettings.ToolPaths.FfprobePath);
            Assert.Equal(string.Empty, persistedSettings.ToolPaths!.MkvToolNixDirectoryPath);
            Assert.Equal(string.Empty, persistedSettings.ToolPaths.FfprobePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("USERPROFILE", originalUserProfile);
            Environment.SetEnvironmentVariable("HOME", originalHome);
            if (Directory.Exists(userProfileDirectory))
            {
                Directory.Delete(userProfileDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Load_PreservesManualOverridesOutsideDownloadsWhenAutoManageIsEnabled()
    {
        var userProfileDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-store-manual", Guid.NewGuid().ToString("N"));
        var manualToolDirectory = Path.Combine(userProfileDirectory, "Tools");
        var mkvToolNixDirectory = Path.Combine(manualToolDirectory, "mkvtoolnix");
        var ffprobePath = Path.Combine(manualToolDirectory, "ffprobe.exe");
        var originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        var originalHome = Environment.GetEnvironmentVariable("HOME");

        try
        {
            Environment.SetEnvironmentVariable("USERPROFILE", userProfileDirectory);
            Environment.SetEnvironmentVariable("HOME", userProfileDirectory);

            Directory.CreateDirectory(mkvToolNixDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(ffprobePath)!);

            AppSettingsFileLocator.SaveCombinedSettings(new CombinedAppSettings
            {
                ToolPaths = new AppToolPathSettings
                {
                    MkvToolNixDirectoryPath = mkvToolNixDirectory,
                    FfprobePath = ffprobePath,
                    ManagedMkvToolNix = new ManagedToolSettings
                    {
                        AutoManageEnabled = true
                    },
                    ManagedFfprobe = new ManagedToolSettings
                    {
                        AutoManageEnabled = true
                    }
                }
            });

            var loadedSettings = new AppSettingsStore().Load();

            Assert.Equal(mkvToolNixDirectory, loadedSettings.ToolPaths!.MkvToolNixDirectoryPath);
            Assert.Equal(ffprobePath, loadedSettings.ToolPaths.FfprobePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("USERPROFILE", originalUserProfile);
            Environment.SetEnvironmentVariable("HOME", originalHome);
            if (Directory.Exists(userProfileDirectory))
            {
                Directory.Delete(userProfileDirectory, recursive: true);
            }
        }
    }
}
