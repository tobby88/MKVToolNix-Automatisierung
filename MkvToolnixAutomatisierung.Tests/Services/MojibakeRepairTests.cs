using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class MojibakeRepairTests
{
    [Fact]
    public void NormalizeLikelyMojibake_RepairsUtf8DecodedAsLatin1()
    {
        var normalized = MojibakeRepair.NormalizeLikelyMojibake("GrÃ¼n");

        Assert.Equal("Grün", normalized);
    }

    [Fact]
    public void NormalizeLikelyMojibake_RepairsWindows1252Sequences()
    {
        var normalized = MojibakeRepair.NormalizeLikelyMojibake("Teil â€“ 1");

        Assert.Equal("Teil – 1", normalized);
    }

    [Fact]
    public void NormalizeLikelyMojibake_LeavesCleanTextUntouched()
    {
        var normalized = MojibakeRepair.NormalizeLikelyMojibake("Straße - Teil 1");

        Assert.Equal("Straße - Teil 1", normalized);
    }

    [Theory]
    [InlineData("Mâcon")]
    [InlineData("Âge tendre")]
    [InlineData("Ein Titel mit 漢字 und â")]
    public void NormalizeLikelyMojibake_DoesNotDamageLegitimateSuspiciousCharacters(string text)
    {
        Assert.Equal(text, MojibakeRepair.NormalizeLikelyMojibake(text));
    }
}
