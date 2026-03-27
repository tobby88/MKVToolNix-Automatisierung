using System.IO;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

[Collection("PortableStorage")]
public sealed class EpisodeOutputPathServiceTests : IDisposable
{
    private readonly PortableStorageFixture _storageFixture;
    private readonly string _tempDirectory;

    public EpisodeOutputPathServiceTests(PortableStorageFixture storageFixture)
    {
        _storageFixture = storageFixture;
        _storageFixture.Reset();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void BuildOutputPath_RebasesArchiveRelativePath_WhenOverrideIsSet()
    {
        var archiveRoot = Path.Combine(_tempDirectory, "archive-root");
        var overrideRoot = Path.Combine(_tempDirectory, "custom-output");
        Directory.CreateDirectory(archiveRoot);
        var archiveService = new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(new AppSettingsStore()));
        archiveService.ConfigureArchiveRootDirectory(archiveRoot);
        var service = new EpisodeOutputPathService(archiveService);

        var outputPath = service.BuildOutputPath(
            Path.Combine(_tempDirectory, "fallback"),
            "Beispielserie",
            "02",
            "04",
            "Titel",
            outputRootOverride: overrideRoot);

        Assert.Equal(
            Path.Combine(overrideRoot, "Beispielserie", "Season 2", "Beispielserie - S02E04 - Titel.mkv"),
            outputPath);
    }

    [Fact]
    public void BuildOutputPath_UsesOnlyFileName_WhenArchiveIsUnavailableAndOverrideIsSet()
    {
        var overrideRoot = Path.Combine(_tempDirectory, "custom-output");
        var archiveService = new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(new AppSettingsStore()));
        archiveService.ConfigureArchiveRootDirectory(Path.Combine(_tempDirectory, "missing-archive"));
        var service = new EpisodeOutputPathService(archiveService);

        var outputPath = service.BuildOutputPath(
            Path.Combine(_tempDirectory, "fallback"),
            "Beispielserie",
            "01",
            "01",
            "Pilot",
            outputRootOverride: overrideRoot);

        Assert.Equal(
            Path.Combine(overrideRoot, "Beispielserie - S01E01 - Pilot.mkv"),
            outputPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
