using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class MediaLanguageHelperTests
{
    [Theory]
    [InlineData("eng", "ger", null, "de")]
    [InlineData("eng", "ger", "nds", "nds")]
    [InlineData("und", "eng", null, "en")]
    [InlineData("eng", "eng", null, "en")]
    [InlineData("de", "en", null, "de")]
    [InlineData("eng", "und", null, "en")]
    [InlineData("en-US", "unknown", null, "en")]
    [InlineData("eng-US", "deu-DE", null, "de")]
    public void ResolveMuxVideoLanguageCode_PrefersExplicitHintThenPrimaryAudioLanguage(
        string? videoLanguage,
        string? primaryAudioLanguage,
        string? explicitHint,
        string expected)
    {
        var result = MediaLanguageHelper.ResolveMuxVideoLanguageCode(
            videoLanguage,
            primaryAudioLanguage,
            explicitHint);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("fra", "fr", "Français")]
    [InlineData("es-MX", "es", "Español")]
    [InlineData("swe", "sv", "Svenska")]
    [InlineData("jpn", "ja", "日本語")]
    public void NormalizeMuxLanguageCode_SupportsManualOverrideLanguages(
        string rawCode,
        string expectedCode,
        string expectedDisplayName)
    {
        Assert.Equal(expectedCode, MediaLanguageHelper.NormalizeMuxLanguageCode(rawCode));
        Assert.Equal(expectedDisplayName, MediaLanguageHelper.GetLanguageDisplayName(rawCode));
    }

    [Fact]
    public void TryNormalizeKnownMuxLanguageCode_ReturnsNull_ForUnknownValues()
    {
        Assert.Equal("sv", MediaLanguageHelper.TryNormalizeKnownMuxLanguageCode("swe"));
        Assert.Null(MediaLanguageHelper.TryNormalizeKnownMuxLanguageCode("zz"));
        Assert.Null(MediaLanguageHelper.TryNormalizeKnownMuxLanguageCode(null));
    }

    [Theory]
    [InlineData("eng-US", "en")]
    [InlineData("swe_SE", "sv")]
    [InlineData("fra-CA", "fr")]
    [InlineData("cmn-Hans", "zh")]
    [InlineData("ger-DE", "de")]
    public void NormalizeMuxLanguageCode_PreservesKnownThreeLetterLanguageWithSubtags(string input, string expected)
    {
        Assert.Equal(expected, MediaLanguageHelper.NormalizeMuxLanguageCode(input));
    }
}
