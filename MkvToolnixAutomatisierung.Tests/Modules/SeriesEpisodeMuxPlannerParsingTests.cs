using System.IO;
using System.Text.RegularExpressions;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Modules;

public sealed class SeriesEpisodeMuxPlannerParsingTests
{
    [Fact]
    public void FindEpisodePattern_ParsesYearBasedSeasonNumbers()
    {
        var match = Assert.IsType<Match>(SeriesEpisodeMuxPlanner.FindEpisodePattern("Beispieltitel (S2001 / E03)"));
        Assert.True(match.Success);
        Assert.Equal("2001", match.Groups["season"].Value);
        Assert.Equal("03", match.Groups["episode"].Value);
    }

    [Fact]
    public void FindEpisodePattern_ParsesEpisodeRanges_FromArchiveStyleCode()
    {
        var match = Assert.IsType<Match>(SeriesEpisodeMuxPlanner.FindEpisodePattern("S2014E05-E06 - Rififi"));

        Assert.True(match.Success);
        Assert.Equal("2014", match.Groups["season"].Value);
        Assert.Equal("05-E06", match.Groups["episode"].Value);
    }

    [Fact]
    public void NormalizeEpisodeTitle_RemovesYearBasedSeasonMarker()
    {
        var normalizedTitle = SeriesEpisodeMuxPlanner.NormalizeEpisodeTitle("Beispieltitel (S2001 / E03)");
        Assert.Equal("Beispieltitel", normalizedTitle);
    }

    [Fact]
    public void NormalizeEpisodeTitle_RemovesLeadingArchiveStyleEpisodeRangeCode()
    {
        var normalizedTitle = SeriesEpisodeMuxPlanner.NormalizeEpisodeTitle("S2014E05-E06 - Rififi");

        Assert.Equal("Rififi", normalizedTitle);
    }

    [Fact]
    public void NormalizeEpisodeTitle_RemovesEditorialLabelsAndAudioDescription()
    {
        var normalizedTitle = SeriesEpisodeMuxPlanner.NormalizeEpisodeTitle(
            "Der Samstagskrimi - Beispieltitel - Neue Folge (Audiodeskription)");

        Assert.Equal("Beispieltitel", normalizedTitle);
    }

    [Fact]
    public void NormalizeEpisodeTitle_RemovesMitAudioDescriptionMarker()
    {
        var normalizedTitle = SeriesEpisodeMuxPlanner.NormalizeEpisodeTitle(
            "Rififi (2) ... es geht weiter (mit Audiodeskription)");

        Assert.Equal("Rififi (2) ... es geht weiter", normalizedTitle);
    }

    [Fact]
    public void NormalizeEpisodeTitle_RemovesHoerfassungMarker()
    {
        var normalizedTitle = SeriesEpisodeMuxPlanner.NormalizeEpisodeTitle("Findus zieht um (Hörfassung)");

        Assert.Equal("Findus zieht um", normalizedTitle);
    }

    [Fact]
    public void NormalizeEpisodeTitle_RemovesShortFilmEditorialLabel()
    {
        var normalizedTitle = SeriesEpisodeMuxPlanner.NormalizeEpisodeTitle("Goldenes Blut - Kurzfilm");

        Assert.Equal("Goldenes Blut", normalizedTitle);
    }

    [Fact]
    public void NormalizeEpisodeTitle_RemovesLeadingEpisodeLabel()
    {
        var normalizedTitle = SeriesEpisodeMuxPlanner.NormalizeEpisodeTitle("Folge 22: Die Waffe im Müll (S03/E10)");

        Assert.Equal("Die Waffe im Müll", normalizedTitle);
    }

    [Theory]
    [InlineData("Der Nachtalb - aus der Reihe _Die Toten vom Bodensee_", "Der Nachtalb")]
    [InlineData("Der Nachtalb - aus der Reihe \"Die Toten vom Bodensee\" (S2025/E04)", "Der Nachtalb")]
    [InlineData("Der Nachtalb - Aus der Reihe: Die Toten vom Bodensee", "Der Nachtalb")]
    [InlineData("Der Seelenkreis - aus der Krimireihe _Die Toten vom Bodensee_", "Der Seelenkreis")]
    public void NormalizeEpisodeTitle_RemovesSeriesEditorialSuffix(string rawTitle, string expectedTitle)
    {
        var normalizedTitle = SeriesEpisodeMuxPlanner.NormalizeEpisodeTitle(rawTitle);

        Assert.Equal(expectedTitle, normalizedTitle);
    }

    [Fact]
    public void NormalizeEpisodeTitle_RemovesBuettenwarderOpPlattEditorialPrefix()
    {
        var normalizedTitle = SeriesEpisodeMuxPlanner.NormalizeEpisodeTitle("Büttenwarder op Platt: Liebesnacht");

        Assert.Equal("Liebesnacht", normalizedTitle);
    }

    [Fact]
    public void NormalizeSeparators_RepairsMojibakeAndUnicodeDashes()
    {
        var mojibake = "Stra\u00C3\u0178e \u2013 Teil 1";
        var normalized = SeriesEpisodeMuxPlanner.NormalizeSeparators(mojibake);

        Assert.Equal("Stra\u00DFe - Teil 1", normalized);
    }

    [Fact]
    public void NormalizeSeparators_NormalizesTypographicApostrophes_AndEllipsis()
    {
        var normalized = SeriesEpisodeMuxPlanner.NormalizeSeparators("D’Welt … bleibt");

        Assert.Equal("D'Welt ... bleibt", normalized);
    }

    [Fact]
    public void NormalizeSeparators_PreservesInternalHyphensWithinWords()
    {
        var normalized = SeriesEpisodeMuxPlanner.NormalizeSeparators("Der Kroatien-Krimi - Teil 1");

        Assert.Equal("Der Kroatien-Krimi - Teil 1", normalized);
    }

    [Fact]
    public void NormalizeEpisodeTitle_RemovesTrailingPunctuationAfterCleanup()
    {
        var normalizedTitle = SeriesEpisodeMuxPlanner.NormalizeEpisodeTitle("Beispieltitel: ");

        Assert.Equal("Beispieltitel", normalizedTitle);
    }

    [Fact]
    public void CreateDirectoryDetectionContext_MergesSubtitleOnlySupplement_WithEquivalentEpisodeDespiteDifferentCode()
    {
        var planner = CreatePlanner();
        var tempDirectory = CreateTempDirectory();

        try
        {
            CreateEmptyFile(Path.Combine(tempDirectory, "Jenseits der Spree-Im Land der toten Träume (S05_E01)-1358865137.mp4"));
            CreateCompanionText(
                Path.Combine(tempDirectory, "Jenseits der Spree-Im Land der toten Träume (S05_E01)-1358865137.txt"),
                topic: "Jenseits der Spree",
                title: "Im Land der toten Träume (S05/E01)",
                duration: "00:58:48");
            CreateEmptyFile(Path.Combine(tempDirectory, "Jenseits der Spree-Im Land der toten Träume (Staffel 5, Folge 25)-0880795506.vtt"));
            CreateCompanionText(
                Path.Combine(tempDirectory, "Jenseits der Spree-Im Land der toten Träume (Staffel 5, Folge 25)-0880795506.txt"),
                topic: "Jenseits der Spree",
                title: "Im Land der toten Träume (Staffel 5, Folge 25)",
                duration: "00:58:48");

            var context = planner.CreateDirectoryDetectionContext(tempDirectory);

            var mainVideoFiles = Assert.Single(context.MainVideoFiles);
            Assert.EndsWith("Jenseits der Spree-Im Land der toten Träume (S05_E01)-1358865137.mp4", mainVideoFiles, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void CreateDirectoryDetectionContext_UsesSeriesPrefixFromTitle_WhenTopicIsGenericRubric()
    {
        var planner = CreatePlanner();
        var tempDirectory = CreateTempDirectory();

        try
        {
            var filePath = Path.Combine(tempDirectory, "Backstage-SOKO Leipzig_ Am Filmset mit den Krimi-Helden (S01_E04)-0868375784.mp4");
            CreateEmptyFile(filePath);
            CreateCompanionText(
                Path.Combine(tempDirectory, "Backstage-SOKO Leipzig_ Am Filmset mit den Krimi-Helden (S01_E04)-0868375784.txt"),
                topic: "Backstage",
                title: "SOKO Leipzig: Am Filmset mit den Krimi-Helden (S01/E04)",
                duration: "00:26:55");

            var context = planner.CreateDirectoryDetectionContext(tempDirectory);
            var seed = context.GetSelectedSeed(filePath);

            Assert.Equal("SOKO Leipzig", seed.Identity.SeriesName);
            Assert.Equal("Am Filmset mit den Krimi-Helden", seed.Identity.Title);
            Assert.Equal("01", seed.Identity.SeasonNumber);
            Assert.Equal("04", seed.Identity.EpisodeNumber);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void CreateDirectoryDetectionContext_CollectsEquivalentMetadataOnlyTxt_WithEpisodeSeed()
    {
        var planner = CreatePlanner();
        var tempDirectory = CreateTempDirectory();

        try
        {
            var mainVideoPath = Path.Combine(tempDirectory, "Der Staatsanwalt-Tod eines Rebellen (S18_E03)-2000000001.mp4");
            var mainTextPath = Path.Combine(tempDirectory, "Der Staatsanwalt-Tod eines Rebellen (S18_E03)-2000000001.txt");
            var metadataOnlyTextPath = Path.Combine(tempDirectory, "Der Staatsanwalt-Tod eines Rebellen (Staffel 18, Folge 3)-0544044864.txt");
            CreateEmptyFile(mainVideoPath);
            CreateCompanionText(
                mainTextPath,
                topic: "Der Staatsanwalt",
                title: "Tod eines Rebellen (S18/E03)",
                duration: "00:59:00");
            CreateCompanionText(
                metadataOnlyTextPath,
                topic: "Der Staatsanwalt",
                title: "Tod eines Rebellen (Staffel 18, Folge 3)",
                duration: "00:59:00");

            var context = planner.CreateDirectoryDetectionContext(tempDirectory);
            var episodeSeeds = context.GetEpisodeSeeds(context.GetSelectedSeed(mainVideoPath));

            Assert.Contains(episodeSeeds.MetadataOnlySeeds, seed => string.Equals(seed.FilePath, metadataOnlyTextPath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static SeriesEpisodeMuxPlanner CreatePlanner()
    {
        var settingsStore = new AppSettingsStore();
        var toolPathStore = new AppToolPathStore(settingsStore);
        var archiveSettingsStore = new AppArchiveSettingsStore(settingsStore);
        var probeService = new MkvMergeProbeService();
        var archiveService = new SeriesArchiveService(probeService, archiveSettingsStore);
        return new SeriesEpisodeMuxPlanner(
            new MkvToolNixLocator(toolPathStore),
            probeService,
            archiveService,
            new NullDurationProbe());
    }

    private static string CreateTempDirectory()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-planner-parsing-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        return tempDirectory;
    }

    private static void CreateEmptyFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "content");
    }

    private static void CreateCompanionText(
        string path,
        string topic,
        string title,
        string duration)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            string.Join(
                Environment.NewLine,
                [
                    $"Thema:       {topic}",
                    string.Empty,
                    $"Titel:       {title}",
                    string.Empty,
                    $"Dauer:       {duration}"
                ]));
    }

    private sealed class NullDurationProbe : IMediaDurationProbe
    {
        public TimeSpan? TryReadDuration(string filePath)
        {
            return null;
        }
    }
}
