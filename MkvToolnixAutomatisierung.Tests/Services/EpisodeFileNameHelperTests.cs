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

    [Fact]
    public void SanitizeFileName_NormalizesReservedNames_AndTrailingSeparators()
    {
        var fileName = EpisodeFileNameHelper.SanitizeFileName("CON .txt");

        Assert.Equal("CON_.txt", fileName);
    }

    [Fact]
    public void SanitizeFileName_NormalizesReservedNames_CaseInsensitively()
    {
        var fileName = EpisodeFileNameHelper.SanitizeFileName("con.txt");

        Assert.Equal("con_.txt", fileName);
    }

    [Fact]
    public void SanitizePathSegment_NormalizesReservedNames_CaseInsensitively()
    {
        var segment = EpisodeFileNameHelper.SanitizePathSegment("lPt1. ");

        Assert.Equal("lPt1_", segment);
    }

    [Fact]
    public void SanitizePathSegment_NormalizesReservedNames_WithExtension()
    {
        var segment = EpisodeFileNameHelper.SanitizePathSegment("CON.txt");

        Assert.Equal("CON_.txt", segment);
    }

    [Fact]
    public void BuildEpisodeFileName_RemovesTrailingDotsAndSpaces_FromTitleStem()
    {
        var fileName = EpisodeFileNameHelper.BuildEpisodeFileName("Serie", "01", "02", "Pilot. ");

        Assert.Equal("Serie - S01E02 - Pilot.mkv", fileName);
    }
}
