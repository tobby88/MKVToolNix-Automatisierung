using System;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class MkvMergeIdentifyRunnerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ParseIdentifyResult_AcceptsSuccessAndWarning(int exitCode)
    {
        using var result = MkvMergeIdentifyRunner.ParseIdentifyResult("{\"tracks\":[]}", "", exitCode);

        Assert.Equal(0, result.RootElement.GetProperty("tracks").GetArrayLength());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public void ParseIdentifyResult_RejectsFatalExitEvenWithValidJson(int exitCode)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            MkvMergeIdentifyRunner.ParseIdentifyResult("{\"tracks\":[],\"errors\":[\"broken input\"]}", "", exitCode));

        Assert.Contains("broken input", error.Message, StringComparison.Ordinal);
        Assert.Contains($"Exitcode {exitCode}", error.Message, StringComparison.Ordinal);
    }
}
