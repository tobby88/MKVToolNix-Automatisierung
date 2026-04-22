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

    [Fact]
    public void BuildSuggestedOutputPath_UsesFallback_WhenStoredArchiveRootIsInvalid()
    {
        const string invalidArchiveRoot = "C:\\bad\0root";
        var settingsStore = new AppSettingsStore();
        var archiveSettingsStore = new AppArchiveSettingsStore(settingsStore);
        archiveSettingsStore.Save(new AppArchiveSettings
        {
            DefaultSeriesArchiveRootPath = invalidArchiveRoot
        });
        var service = new SeriesArchiveService(new MkvMergeProbeService(), archiveSettingsStore);

        var path = service.BuildSuggestedOutputPath(
            Path.Combine(_tempDirectory, "fallback"),
            "Beispielserie",
            "01",
            "03",
            "Pilot");

        Assert.Equal(invalidArchiveRoot.Trim(), service.ArchiveRootDirectory);
        Assert.StartsWith(Path.Combine(_tempDirectory, "fallback"), path, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Die konfigurierte Serienbibliothek ist ungültig:", service.BuildArchiveUnavailableWarningMessage(), StringComparison.Ordinal);
        Assert.False(service.IsArchivePath(Path.Combine(SeriesArchiveService.DefaultArchiveRootDirectory, "Beispielserie", "Season 1", "foo.mkv")));
    }

    [Fact]
    public void ConfigureArchiveRootDirectory_ThrowsForInvalidExplicitPath()
    {
        var service = CreateService();
        const string invalidArchiveRoot = "C:\\bad\0root";

        var exception = Assert.Throws<ArgumentException>(() => service.ConfigureArchiveRootDirectory(invalidArchiveRoot));

        Assert.Contains("Archivwurzelpfad", exception.Message, StringComparison.OrdinalIgnoreCase);
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
