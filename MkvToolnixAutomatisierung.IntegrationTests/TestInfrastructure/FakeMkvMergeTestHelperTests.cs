using Xunit;

namespace MkvToolnixAutomatisierung.IntegrationTests.TestInfrastructure;

public sealed class FakeMkvMergeTestHelperTests
{
    [Fact]
    public void ResolveExecutablePath_ReturnsMkvMergeExecutable()
    {
        var executablePath = FakeMkvMergeTestHelper.ResolveExecutablePath();

        Assert.EndsWith("mkvmerge.exe", executablePath, StringComparison.OrdinalIgnoreCase);
    }
}
