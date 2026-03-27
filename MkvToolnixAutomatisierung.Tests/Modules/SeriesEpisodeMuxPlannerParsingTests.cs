using System.Reflection;
using System.Text.RegularExpressions;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Modules;

public sealed class SeriesEpisodeMuxPlannerParsingTests
{
    [Fact]
    public void FindEpisodePattern_ParsesYearBasedSeasonNumbers()
    {
        var method = typeof(SeriesEpisodeMuxPlanner).GetMethod(
            "FindEpisodePattern",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var match = Assert.IsType<Match>(method!.Invoke(null, ["Beispieltitel (S2001 / E03)"]));
        Assert.True(match.Success);
        Assert.Equal("2001", match.Groups["season"].Value);
        Assert.Equal("03", match.Groups["episode"].Value);
    }

    [Fact]
    public void NormalizeEpisodeTitle_RemovesYearBasedSeasonMarker()
    {
        var normalizedTitle = InvokePrivateStringMethod("NormalizeEpisodeTitle", "Beispieltitel (S2001 / E03)");
        Assert.Equal("Beispieltitel", normalizedTitle);
    }

    [Fact]
    public void NormalizeEpisodeTitle_RemovesEditorialLabelsAndAudioDescription()
    {
        var normalizedTitle = InvokePrivateStringMethod(
            "NormalizeEpisodeTitle",
            "Der Samstagskrimi - Beispieltitel - Neue Folge (Audiodeskription)");

        Assert.Equal("Beispieltitel", normalizedTitle);
    }

    [Fact]
    public void NormalizeSeparators_RepairsMojibakeAndUnicodeDashes()
    {
        var mojibake = "Stra\u00C3\u0178e \u2013 Teil 1";
        var normalized = InvokePrivateStringMethod("NormalizeSeparators", mojibake);

        Assert.Equal("Stra\u00DFe - Teil 1", normalized);
    }

    [Fact]
    public void NormalizeEpisodeTitle_RemovesTrailingPunctuationAfterCleanup()
    {
        var normalizedTitle = InvokePrivateStringMethod("NormalizeEpisodeTitle", "Beispieltitel: ");

        Assert.Equal("Beispieltitel", normalizedTitle);
    }

    private static string InvokePrivateStringMethod(string methodName, string input)
    {
        var method = typeof(SeriesEpisodeMuxPlanner).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return Assert.IsType<string>(method!.Invoke(null, [input]));
    }
}
