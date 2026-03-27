using System.IO;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

[Collection("PortableStorage")]
public sealed class SeriesArchiveServiceTests : IDisposable
{
    private readonly PortableStorageFixture _storageFixture;
    private readonly string _tempDirectory;

    public SeriesArchiveServiceTests(PortableStorageFixture storageFixture)
    {
        _storageFixture = storageFixture;
        _storageFixture.Reset();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void BuildSuggestedOutputPath_UsesFallbackDirectory_WhenArchiveIsUnavailable()
    {
        var service = CreateService();
        service.ConfigureArchiveRootDirectory(Path.Combine(_tempDirectory, "missing-archive"));

        var path = service.BuildSuggestedOutputPath(
            Path.Combine(_tempDirectory, "fallback"),
            "Beispielserie",
            "01",
            "03",
            "Pilot");

        Assert.StartsWith(Path.Combine(_tempDirectory, "fallback"), path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("Beispielserie - S01E03 - Pilot.mkv", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSuggestedOutputPath_UsesArchiveRoot_WhenArchiveExists()
    {
        var archiveRoot = Path.Combine(_tempDirectory, "archive-root");
        Directory.CreateDirectory(archiveRoot);
        var service = CreateService();
        service.ConfigureArchiveRootDirectory(archiveRoot);

        var path = service.BuildSuggestedOutputPath(
            Path.Combine(_tempDirectory, "fallback"),
            "Beispielserie",
            "2001",
            "03",
            "Pilot");

        Assert.Equal(
            Path.Combine(archiveRoot, "Beispielserie", "Season 2001", "Beispielserie - S2001E03 - Pilot.mkv"),
            path);
    }

    [Fact]
    public void ConfigureArchiveRootDirectory_PersistsNormalizedPath()
    {
        var archiveRoot = Path.Combine(_tempDirectory, "archive-root");
        Directory.CreateDirectory(archiveRoot);
        var service = CreateService();

        service.ConfigureArchiveRootDirectory(archiveRoot + Path.DirectorySeparatorChar);

        var reloadedSettings = new AppArchiveSettingsStore(new AppSettingsStore()).Load();
        Assert.Equal(archiveRoot, service.ArchiveRootDirectory);
        Assert.Equal(archiveRoot, reloadedSettings.DefaultSeriesArchiveRootPath);
    }

    [Fact]
    public void BuildArchiveUnavailableWarningMessage_IncludesConfiguredPath()
    {
        var configuredPath = Path.Combine(_tempDirectory, "offline-archive");
        var service = CreateService();
        service.ConfigureArchiveRootDirectory(configuredPath);

        var message = service.BuildArchiveUnavailableWarningMessage();

        Assert.Contains(configuredPath, message);
        Assert.Contains("Quellordner", message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private static SeriesArchiveService CreateService()
    {
        return new SeriesArchiveService(
            new MkvMergeProbeService(),
            new AppArchiveSettingsStore(new AppSettingsStore()));
    }
}
