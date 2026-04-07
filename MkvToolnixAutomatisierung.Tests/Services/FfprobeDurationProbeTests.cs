using System.IO;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class FfprobeDurationProbeTests : IDisposable
{
    private readonly string _tempDirectory;

    public FfprobeDurationProbeTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-ffprobe-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void IsAvailable_ReusesFailedLookup_ForImmediateFollowUpChecks()
    {
        var locator = new CountingFfprobeLocator(null);
        var probe = new FfprobeDurationProbe(locator);

        Assert.False(probe.IsAvailable);
        Assert.Null(probe.ExecutablePath);
        Assert.Equal(1, locator.CallCount);
    }

    [Fact]
    public void IsAvailable_ReusesSuccessfulLookup_ForImmediateFollowUpChecks()
    {
        var ffprobePath = Path.Combine(_tempDirectory, "ffprobe.exe");
        File.WriteAllText(ffprobePath, "fake");
        var locator = new CountingFfprobeLocator(ffprobePath);
        var probe = new FfprobeDurationProbe(locator);

        Assert.True(probe.IsAvailable);
        Assert.Equal(ffprobePath, probe.ExecutablePath);
        Assert.Equal(1, locator.CallCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private sealed class CountingFfprobeLocator(string? resolvedPath) : IFfprobeLocator
    {
        public int CallCount { get; private set; }

        public string? TryFindFfprobePath()
        {
            CallCount++;
            return resolvedPath;
        }
    }
}
