using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class EpisodeFileNameHelperTests
{
    [Theory]
    [InlineData(@"C:\Temp\Serie - Audiodeskription.mp4", true)]
    [InlineData(@"C:\Temp\Serie - Pilot (Audiodes.mp4", true)]
    [InlineData(@"C:\Temp\Serie - Pilot (Audiodeskription und UT).mp4", true)]
    [InlineData(@"C:\Temp\Serie - AD.mp4", true)]
    [InlineData(@"C:\Temp\Serie - Pilot (Hörfassung).mp4", true)]
    [InlineData(@"C:\Temp\Serie - Pilot (Hoerfassung).mp4", true)]
    [InlineData(@"C:\Temp\Serie - Audiodesign.mp4", false)]
    [InlineData(@"C:\Temp\Serie - Adventure.mp4", false)]
    public void LooksLikeAudioDescription_DetectsExpectedPatterns(string filePath, bool expected)
    {
        Assert.Equal(expected, EpisodeFileNameHelper.LooksLikeAudioDescription(filePath));
    }

    [Theory]
    [InlineData("1", "01")]
    [InlineData("2001", "2001")]
    [InlineData("5-6", "05-E06")]
    [InlineData("05-E06", "05-E06")]
    [InlineData("xx", "xx")]
    public void NormalizeEpisodeNumber_HandlesSingleAndRangeValues(string value, string expected)
    {
        Assert.Equal(expected, EpisodeFileNameHelper.NormalizeEpisodeNumber(value));
    }

    [Theory]
    [InlineData("1", "01")]
    [InlineData("2001", "2001")]
    [InlineData("xx", "xx")]
    public void NormalizeSeasonNumber_HandlesStandardAndYearBasedValues(string value, string expected)
    {
        Assert.Equal(expected, EpisodeFileNameHelper.NormalizeSeasonNumber(value));
    }

    [Fact]
    public void BuildEpisodeFileName_SanitizesInvalidCharacters()
    {
        var fileName = EpisodeFileNameHelper.BuildEpisodeFileName("Serie:Test", "01", "02", "Pilot?");

        Assert.Equal("Serie - Test - S01E02 - Pilot.mkv", fileName);
    }

    [Fact]
    public void BuildEpisodeFileName_NormalizesTypographicDashes()
    {
        var fileName = EpisodeFileNameHelper.BuildEpisodeFileName("Serie", "01", "02", "A – B");

        Assert.Equal("Serie - S01E02 - A - B.mkv", fileName);
    }

    [Fact]
    public void BuildEpisodeFileName_NormalizesTypographicApostrophes_AndEllipsis()
    {
        var fileName = EpisodeFileNameHelper.BuildEpisodeFileName("München Mord", "01", "02", "D’Welt … bleibt");

        Assert.Equal("München Mord - S01E02 - D'Welt ... bleibt.mkv", fileName);
    }

    [Fact]
    public void BuildEpisodeFileName_ReplacesTitleColonWithReadableSeparator()
    {
        var fileName = EpisodeFileNameHelper.BuildEpisodeFileName("Neues aus Büttenwarder", "2014", "05", "Olympische Rekorde (1): Rekord");

        Assert.Equal("Neues aus Büttenwarder - S2014E05 - Olympische Rekorde (1) - Rekord.mkv", fileName);
    }

    [Fact]
    public void BuildEpisodeFileName_FormatsDoubleEpisodes()
    {
        var fileName = EpisodeFileNameHelper.BuildEpisodeFileName("Serie", "2014", "05-E06", "Rififi");

        Assert.Equal("Serie - S2014E05-E06 - Rififi.mkv", fileName);
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
    public void BuildEpisodeFileName_PreservesTrailingEllipsisBeforeExtension()
    {
        var fileName = EpisodeFileNameHelper.BuildEpisodeFileName("Der Alte", "10", "07", "Gigolo ist tot...");

        Assert.Equal("Der Alte - S10E07 - Gigolo ist tot....mkv", fileName);
    }

    [Fact]
    public void SanitizeFileName_RemovesTrailingDotsAndSpaces_WhenNoExtensionIsPresent()
    {
        var fileName = EpisodeFileNameHelper.SanitizeFileName("Pilot. ");

        Assert.Equal("Pilot", fileName);
    }
}
