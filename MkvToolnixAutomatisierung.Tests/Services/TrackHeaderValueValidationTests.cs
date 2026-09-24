using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class TrackHeaderValueValidationTests
{
    [Theory]
    [InlineData("flag-default", "maybe")]
    [InlineData("flag-original", "true")]
    [InlineData("flag-not-a-property", "0")]
    [InlineData("language", "Deutsch!")]
    [InlineData("codec-id", "1")]
    public void InvalidRawPropertiesAreRejected(string property, string value) =>
        Assert.Throws<ArgumentException>(() => TrackHeaderValueValidation.Validate(property, value));

    [Theory]
    [InlineData("language", "pt-BR")]
    [InlineData("language", "zh-Hant-TW")]
    [InlineData("name", "Deutsch - AAC")]
    [InlineData("flag-original", "0")]
    public void SupportedRawValuesRemainAllowed(string property, string value) => TrackHeaderValueValidation.Validate(property, value);

    [Fact]
    public void InvalidFlagIsVisibleAndNeverSilentlyConvertedToFalse()
    {
        var candidate = new ArchiveTrackHeaderCorrectionCandidate("track:1", "Video", "Video", []);
        var value = new ArchiveTrackHeaderValueCandidate("flag-default", "Standard", "ja", "ja", "1", true);
        var row = new ArchiveMaintenanceHeaderCorrectionViewModel(candidate, value) { TargetValue = "vielleicht" };
        Assert.NotNull(row.ValidationMessage);
        Assert.True(row.HasChange);
        Assert.Null(row.CreateValueEdit());
        Assert.Throws<ArgumentException>(() => TrackHeaderValueValidation.FlagToRaw("vielleicht"));
    }
}
