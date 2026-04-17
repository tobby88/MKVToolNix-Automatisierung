using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Modules;

public sealed class ResolutionLabelTests
{
    [Theory]
    [InlineData(3840, "UHD")]
    [InlineData(1920, "FHD")]
    [InlineData(1280, "HD")]
    [InlineData(960, "qHD")]
    [InlineData(720, "SD")]
    public void FromWidth_MapsCommonMediathekWidths(int width, string expected)
    {
        Assert.Equal(expected, ResolutionLabel.FromWidth(width).Value);
    }
}
