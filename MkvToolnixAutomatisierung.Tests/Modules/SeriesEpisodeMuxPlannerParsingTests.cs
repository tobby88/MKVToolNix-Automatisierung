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
        var method = typeof(SeriesEpisodeMuxPlanner).GetMethod(
            "NormalizeEpisodeTitle",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var normalizedTitle = Assert.IsType<string>(method!.Invoke(null, ["Beispieltitel (S2001 / E03)"]));
        Assert.Equal("Beispieltitel", normalizedTitle);
    }
}
