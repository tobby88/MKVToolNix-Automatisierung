using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class EpisodeFileNameHelperTests
{
    [Theory]
    [InlineData(@"C:\Temp\Serie - Audiodeskription.mp4", true)]
    [InlineData(@"C:\Temp\Serie - AD.mp4", true)]
    [InlineData(@"C:\Temp\Serie - Adventure.mp4", false)]
    public void LooksLikeAudioDescription_DetectsExpectedPatterns(string filePath, bool expected)
    {
        Assert.Equal(expected, EpisodeFileNameHelper.LooksLikeAudioDescription(filePath));
    }

    [Theory]
    [InlineData("1", "01")]
    [InlineData("2001", "2001")]
    [InlineData("xx", "xx")]
    public void NormalizeEpisodeNumber_HandlesStandardAndYearBasedValues(string value, string expected)
    {
        Assert.Equal(expected, EpisodeFileNameHelper.NormalizeEpisodeNumber(value));
    }

    [Fact]
    public void BuildEpisodeFileName_SanitizesInvalidCharacters()
    {
        var fileName = EpisodeFileNameHelper.BuildEpisodeFileName("Serie:Test", "01", "02", "Pilot?");

        Assert.Equal("Serie_Test - S01E02 - Pilot_.mkv", fileName);
    }
}
