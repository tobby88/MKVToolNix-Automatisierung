using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class MkvMergeOutputParserTests
{
    private readonly MkvMergeOutputParser _parser = new();

    [Fact]
    public void Parse_GermanProgressLine_ExtractsPercent()
    {
        var result = _parser.Parse("Fortschritt: 50%");

        Assert.Equal(50, result.ProgressPercent);
        Assert.False(result.IsWarning);
    }

    [Fact]
    public void Parse_EnglishProgressLine_ExtractsPercent()
    {
        var result = _parser.Parse("Progress: 75%");

        Assert.Equal(75, result.ProgressPercent);
        Assert.False(result.IsWarning);
    }

    [Fact]
    public void Parse_LineWithoutProgressOrWarning_ReturnsBothFalse()
    {
        var result = _parser.Parse("Multiplexing tracks into output file...");

        Assert.Null(result.ProgressPercent);
        Assert.False(result.IsWarning);
    }

    [Theory]
    [InlineData("Warnung: Unbekannte Trackoption gefunden.")]
    [InlineData("Warning: unknown track option")]
    public void Parse_WarningLine_SetsIsWarning(string line)
    {
        var result = _parser.Parse(line);

        Assert.True(result.IsWarning);
    }

    [Fact]
    public void Parse_LineWithProgressAndWarning_SetsBothFields()
    {
        var result = _parser.Parse("Warnung: Fortschritt: 30%");

        Assert.Equal(30, result.ProgressPercent);
        Assert.True(result.IsWarning);
    }

    [Fact]
    public void Parse_ProgressAboveHundred_ClampsToHundred()
    {
        var result = _parser.Parse("Progress: 150%");

        Assert.Equal(100, result.ProgressPercent);
    }

    [Fact]
    public void Parse_ProgressAtZero_ReturnsZero()
    {
        var result = _parser.Parse("Fortschritt: 0%");

        Assert.Equal(0, result.ProgressPercent);
    }

    [Fact]
    public void Parse_ProgressAtHundred_ReturnsHundred()
    {
        var result = _parser.Parse("Progress: 100%");

        Assert.Equal(100, result.ProgressPercent);
    }
}
