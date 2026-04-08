using System.Text.RegularExpressions;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
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
    public void NormalizeEpisodeTitle_RemovesShortFilmEditorialLabel()
    {
        var normalizedTitle = SeriesEpisodeMuxPlanner.NormalizeEpisodeTitle("Goldenes Blut - Kurzfilm");

        Assert.Equal("Goldenes Blut", normalizedTitle);
    }

    [Fact]
    public void NormalizeSeparators_RepairsMojibakeAndUnicodeDashes()
    {
        var mojibake = "Stra\u00C3\u0178e \u2013 Teil 1";
        var normalized = SeriesEpisodeMuxPlanner.NormalizeSeparators(mojibake);

        Assert.Equal("Stra\u00DFe - Teil 1", normalized);
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
}
