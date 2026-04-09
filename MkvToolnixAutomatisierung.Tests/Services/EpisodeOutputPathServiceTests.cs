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

    [Fact]
    public void BuildOutputPath_UsesArchiveStructure_WhenOverridePointsToArchiveRoot_EvenIfArchiveIsUnavailable()
    {
        var archiveRoot = Path.Combine(_tempDirectory, "missing-archive");
        var archiveService = new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(new AppSettingsStore()));
        archiveService.ConfigureArchiveRootDirectory(archiveRoot);
        var service = new EpisodeOutputPathService(archiveService);

        var outputPath = service.BuildOutputPath(
            Path.Combine(_tempDirectory, "fallback"),
            "Beispielserie",
            "01",
            "01",
            "Pilot",
            outputRootOverride: archiveRoot);

        Assert.Equal(
            Path.Combine(archiveRoot, "Beispielserie", "Season 1", "Beispielserie - S01E01 - Pilot.mkv"),
            outputPath);
    }

    [Fact]
    public void TryResolveExistingArchiveOutputPath_ReturnsExistingArchiveFile_WhenOverrideMatchesArchiveRoot()
    {
        var archiveRoot = Path.Combine(_tempDirectory, "archive-root");
        var archiveFilePath = Path.Combine(archiveRoot, "Beispielserie", "Season 1", "Beispielserie - S01E01 - Pilot.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(archiveFilePath)!);
        File.WriteAllText(archiveFilePath, "archive");

        var archiveService = new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(new AppSettingsStore()));
        archiveService.ConfigureArchiveRootDirectory(archiveRoot);
        var service = new EpisodeOutputPathService(archiveService);

        var resolvedPath = service.TryResolveExistingArchiveOutputPath(
            archiveRoot,
            "Beispielserie",
            "01",
            "01",
            "Pilot");

        Assert.Equal(archiveFilePath, resolvedPath);
    }

    [Fact]
    public void TryResolveExistingArchiveOutputPath_ReturnsExistingArchiveFile_ForEpisodeRanges()
    {
        var archiveRoot = Path.Combine(_tempDirectory, "archive-root");
        var archiveFilePath = Path.Combine(archiveRoot, "Beispielserie", "Season 2014", "Beispielserie - S2014E05-E06 - Rififi.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(archiveFilePath)!);
        File.WriteAllText(archiveFilePath, "archive");

        var archiveService = new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(new AppSettingsStore()));
        archiveService.ConfigureArchiveRootDirectory(archiveRoot);
        var service = new EpisodeOutputPathService(archiveService);

        var resolvedPath = service.TryResolveExistingArchiveOutputPath(
            archiveRoot,
            "Beispielserie",
            "2014",
            "05-E06",
            "Rififi");

        Assert.Equal(archiveFilePath, resolvedPath);
    }

    [Fact]
    public void TryResolveExistingArchiveOutputPath_FallsBackToUniqueTitleMatch_WhenExactCanonicalPathIsMissing()
    {
        var archiveRoot = Path.Combine(_tempDirectory, "archive-root");
        var archiveFilePath = Path.Combine(archiveRoot, "Beispielserie", "Season 4", "Beispielserie - S04E07 - Ostern.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(archiveFilePath)!);
        File.WriteAllText(archiveFilePath, "archive");

        var archiveService = new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(new AppSettingsStore()));
        archiveService.ConfigureArchiveRootDirectory(archiveRoot);
        var service = new EpisodeOutputPathService(archiveService);

        var resolvedPath = service.TryResolveExistingArchiveOutputPath(
            archiveRoot,
            "Beispielserie",
            "xx",
            "xx",
            "Ostern");

        Assert.Equal(archiveFilePath, resolvedPath);
    }

    [Fact]
    public void TryResolveExistingArchiveOutputPath_ReturnsNull_WhenTitleFallbackIsAmbiguous()
    {
        var archiveRoot = Path.Combine(_tempDirectory, "archive-root");
        var firstArchiveFilePath = Path.Combine(archiveRoot, "Beispielserie", "Season 4", "Beispielserie - S04E07 - Ostern.mkv");
        var secondArchiveFilePath = Path.Combine(archiveRoot, "Beispielserie", "Season 5", "Beispielserie - S05E01 - Ostern.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(firstArchiveFilePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(secondArchiveFilePath)!);
        File.WriteAllText(firstArchiveFilePath, "archive");
        File.WriteAllText(secondArchiveFilePath, "archive");

        var archiveService = new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(new AppSettingsStore()));
        archiveService.ConfigureArchiveRootDirectory(archiveRoot);
        var service = new EpisodeOutputPathService(archiveService);

        var resolvedPath = service.TryResolveExistingArchiveOutputPath(
            archiveRoot,
            "Beispielserie",
            "xx",
            "xx",
            "Ostern");

        Assert.Null(resolvedPath);
    }

    [Fact]
    public void BuildOutputRootOverrideHint_ReturnsMessage_WhenArchiveIsUnavailableAndOverrideIsSet()
    {
        var overrideRoot = Path.Combine(_tempDirectory, "custom-output");
        var archiveService = new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(new AppSettingsStore()));
        archiveService.ConfigureArchiveRootDirectory(Path.Combine(_tempDirectory, "missing-archive"));
        var service = new EpisodeOutputPathService(archiveService);

        var hint = service.BuildOutputRootOverrideHint(overrideRoot);

        Assert.NotNull(hint);
        Assert.Contains("bewusst flach", hint, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildOutputRootOverrideHint_ReturnsNull_WhenArchiveIsAvailable()
    {
        var archiveRoot = Path.Combine(_tempDirectory, "archive-root");
        var overrideRoot = Path.Combine(_tempDirectory, "custom-output");
        Directory.CreateDirectory(archiveRoot);
        var archiveService = new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(new AppSettingsStore()));
        archiveService.ConfigureArchiveRootDirectory(archiveRoot);
        var service = new EpisodeOutputPathService(archiveService);

        var hint = service.BuildOutputRootOverrideHint(overrideRoot);

        Assert.Null(hint);
    }

    [Fact]
    public void BuildOutputPath_SanitizesReservedSeriesFolderNames()
    {
        var archiveRoot = Path.Combine(_tempDirectory, "archive-root");
        Directory.CreateDirectory(archiveRoot);
        var archiveService = new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(new AppSettingsStore()));
        archiveService.ConfigureArchiveRootDirectory(archiveRoot);
        var service = new EpisodeOutputPathService(archiveService);

        var outputPath = service.BuildOutputPath(
            Path.Combine(_tempDirectory, "fallback"),
            "CON.",
            "01",
            "01",
            "Pilot");

        Assert.Equal(
            Path.Combine(archiveRoot, "CON_", "Season 1", "CON_. - S01E01 - Pilot.mkv"),
            outputPath);
    }

    [Fact]
    public void TryBuildArchiveDurationMismatchHint_ReturnsWarning_ForNearDoubleDuration()
    {
        var archiveRoot = Path.Combine(_tempDirectory, "archive-root");
        var sourceFilePath = Path.Combine(_tempDirectory, "source", "episode.mp4");
        var archiveFilePath = Path.Combine(archiveRoot, "Beispielserie", "Season 1", "Beispielserie - S01E01 - Pilot.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFilePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(archiveFilePath)!);
        File.WriteAllText(sourceFilePath, "source");
        File.WriteAllText(archiveFilePath, "archive");

        var archiveService = new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(new AppSettingsStore()));
        archiveService.ConfigureArchiveRootDirectory(archiveRoot);
        var service = new EpisodeOutputPathService(
            archiveService,
            new DictionaryDurationProbe(new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
            {
                [sourceFilePath] = TimeSpan.FromMinutes(43),
                [archiveFilePath] = TimeSpan.FromMinutes(86)
            }));

        var hint = service.TryBuildArchiveDurationMismatchHint(sourceFilePath, archiveFilePath);

        Assert.NotNull(hint);
        Assert.Contains("ungefähr 2-mal so lang", hint, StringComparison.Ordinal);
        Assert.Contains("Doppelfolge", hint, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuildArchiveDurationMismatchHint_ReturnsNull_WhenDurationsAreSimilar()
    {
        var archiveRoot = Path.Combine(_tempDirectory, "archive-root");
        var sourceFilePath = Path.Combine(_tempDirectory, "source", "episode.mp4");
        var archiveFilePath = Path.Combine(archiveRoot, "Beispielserie", "Season 1", "Beispielserie - S01E01 - Pilot.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFilePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(archiveFilePath)!);
        File.WriteAllText(sourceFilePath, "source");
        File.WriteAllText(archiveFilePath, "archive");

        var archiveService = new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(new AppSettingsStore()));
        archiveService.ConfigureArchiveRootDirectory(archiveRoot);
        var service = new EpisodeOutputPathService(
            archiveService,
            new DictionaryDurationProbe(new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
            {
                [sourceFilePath] = TimeSpan.FromMinutes(43),
                [archiveFilePath] = TimeSpan.FromMinutes(44)
            }));

        var hint = service.TryBuildArchiveDurationMismatchHint(sourceFilePath, archiveFilePath);

        Assert.Null(hint);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private sealed class DictionaryDurationProbe : IMediaDurationProbe
    {
        private readonly IReadOnlyDictionary<string, TimeSpan> _durations;

        public DictionaryDurationProbe(IReadOnlyDictionary<string, TimeSpan> durations)
        {
            _durations = durations;
        }

        public TimeSpan? TryReadDuration(string filePath)
        {
            return _durations.TryGetValue(filePath, out var duration)
                ? duration
                : null;
        }
    }
}
