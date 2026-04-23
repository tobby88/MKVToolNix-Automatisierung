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
    public void BuildOutputPath_UsesSpecialsFolder_ForSeasonZero()
    {
        var archiveRoot = Path.Combine(_tempDirectory, "archive-root");
        Directory.CreateDirectory(archiveRoot);
        var archiveService = new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(new AppSettingsStore()));
        archiveService.ConfigureArchiveRootDirectory(archiveRoot);
        var service = new EpisodeOutputPathService(archiveService);

        var outputPath = service.BuildOutputPath(
            Path.Combine(_tempDirectory, "fallback"),
            "Beispielserie",
            "00",
            "07",
            "Sonderfolge");

        Assert.Equal(
            Path.Combine(archiveRoot, "Beispielserie", "Specials", "Beispielserie - S00E07 - Sonderfolge.mkv"),
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
    public void TryResolveExistingSpecialArchiveMatch_ReturnsSpecialsFile_AndMetadataFromArchiveName()
    {
        var archiveRoot = Path.Combine(_tempDirectory, "archive-root");
        var archiveFilePath = Path.Combine(
            archiveRoot,
            "Die Heiland - Wir sind Anwalt",
            "Specials",
            "Die Heiland - Wir sind Anwalt - S00E08 - Adas Song - mit Anna Fischer.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(archiveFilePath)!);
        File.WriteAllText(archiveFilePath, "archive");

        var archiveService = new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(new AppSettingsStore()));
        archiveService.ConfigureArchiveRootDirectory(archiveRoot);
        var service = new EpisodeOutputPathService(archiveService);

        var match = service.TryResolveExistingSpecialArchiveMatch(
            archiveRoot,
            ["Die Heiland", "Die Heiland - Wir sind Anwalt"],
            "Extra_ Adas Song - mit Anna Fischer",
            originalLanguage: "deu");

        Assert.NotNull(match);
        Assert.Equal(archiveFilePath, match!.OutputPath);
        Assert.Equal("Die Heiland - Wir sind Anwalt", match.SeriesName);
        Assert.Equal("00", match.SeasonNumber);
        Assert.Equal("08", match.EpisodeNumber);
        Assert.Equal("Adas Song - mit Anna Fischer", match.Title);
        Assert.Equal("deu", match.OriginalLanguage);
    }

    [Fact]
    public void TryResolveExistingSpecialArchiveMatch_ReturnsTitleOnlyTrailerFile()
    {
        var archiveRoot = Path.Combine(_tempDirectory, "archive-root");
        var archiveFilePath = Path.Combine(
            archiveRoot,
            "Pettersson und Findus",
            "Trailers",
            "Findus zieht um Trailer.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(archiveFilePath)!);
        File.WriteAllText(archiveFilePath, "archive");

        var archiveService = new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(new AppSettingsStore()));
        archiveService.ConfigureArchiveRootDirectory(archiveRoot);
        var service = new EpisodeOutputPathService(archiveService);

        var match = service.TryResolveExistingSpecialArchiveMatch(
            archiveRoot,
            ["Pettersson und Findus"],
            "Findus zieht um Trailer");

        Assert.NotNull(match);
        Assert.Equal(archiveFilePath, match!.OutputPath);
        Assert.Equal("Pettersson und Findus", match.SeriesName);
        Assert.Equal("xx", match.SeasonNumber);
        Assert.Equal("xx", match.EpisodeNumber);
        Assert.Equal("Findus zieht um Trailer", match.Title);
    }

    [Fact]
    public void TryResolveExistingSpecialArchiveMatch_ReturnsNull_WhenBestSpecialMatchIsAmbiguous()
    {
        var archiveRoot = Path.Combine(_tempDirectory, "archive-root");
        var firstArchiveFilePath = Path.Combine(
            archiveRoot,
            "Beispielserie",
            "Specials",
            "Beispielserie - S00E01 - Blick hinter die Kulissen.mkv");
        var secondArchiveFilePath = Path.Combine(
            archiveRoot,
            "Beispielserie",
            "Season 0",
            "Beispielserie - S00E02 - Blick hinter die Kulissen.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(firstArchiveFilePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(secondArchiveFilePath)!);
        File.WriteAllText(firstArchiveFilePath, "archive");
        File.WriteAllText(secondArchiveFilePath, "archive");

        var archiveService = new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(new AppSettingsStore()));
        archiveService.ConfigureArchiveRootDirectory(archiveRoot);
        var service = new EpisodeOutputPathService(archiveService);

        var match = service.TryResolveExistingSpecialArchiveMatch(
            archiveRoot,
            ["Beispielserie"],
            "Blick hinter die Kulissen");

        Assert.Null(match);
    }

    [Fact]
    public void TryResolveExistingSpecialArchiveMatch_IgnoresNormalSeasonFolders()
    {
        var archiveRoot = Path.Combine(_tempDirectory, "archive-root");
        var archiveFilePath = Path.Combine(
            archiveRoot,
            "Beispielserie",
            "Season 1",
            "Beispielserie - S01E01 - Blick hinter die Kulissen.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(archiveFilePath)!);
        File.WriteAllText(archiveFilePath, "archive");

        var archiveService = new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(new AppSettingsStore()));
        archiveService.ConfigureArchiveRootDirectory(archiveRoot);
        var service = new EpisodeOutputPathService(archiveService);

        var match = service.TryResolveExistingSpecialArchiveMatch(
            archiveRoot,
            ["Beispielserie"],
            "Blick hinter die Kulissen");

        Assert.Null(match);
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


    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
