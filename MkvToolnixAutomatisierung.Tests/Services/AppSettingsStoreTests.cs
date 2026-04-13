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
}
