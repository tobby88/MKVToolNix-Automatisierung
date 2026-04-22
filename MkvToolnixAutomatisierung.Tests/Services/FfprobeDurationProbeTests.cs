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

    [Fact]
    public async Task TryReadDurationAsync_DoesNotCacheCanceledLookup()
    {
        var ffprobePath = Path.Combine(_tempDirectory, "ffprobe.exe");
        var mediaFilePath = CreateFile("episode.mp4");
        File.WriteAllText(ffprobePath, "fake");
        var locator = new CountingFfprobeLocator(ffprobePath);
        var firstLookupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;
        var expectedDuration = TimeSpan.FromMinutes(42);
        var probe = new FfprobeDurationProbe(locator, async (_filePath, _resolvedFfprobePath, _timeout, cancellationToken) =>
        {
            invocationCount++;
            if (invocationCount == 1)
            {
                firstLookupStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return expectedDuration;
        });

        using var cancellationSource = new CancellationTokenSource();
        var canceledLookup = probe.TryReadDurationAsync(
            mediaFilePath,
            TimeSpan.FromSeconds(1),
            cancellationSource.Token);
        await firstLookupStarted.Task;
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledLookup);

        var duration = await probe.TryReadDurationAsync(mediaFilePath, TimeSpan.FromSeconds(1));

        Assert.Equal(expectedDuration, duration);
        Assert.Equal(2, invocationCount);
    }

    [Fact]
    public async Task TryReadDurationAsync_DoesNotCacheMissingResult_FromTimeoutOrFailure()
    {
        var ffprobePath = Path.Combine(_tempDirectory, "ffprobe.exe");
        var mediaFilePath = CreateFile("timeout.mp4");
        File.WriteAllText(ffprobePath, "fake");
        var locator = new CountingFfprobeLocator(ffprobePath);
        var invocationCount = 0;
        var expectedDuration = TimeSpan.FromMinutes(24);
        var probe = new FfprobeDurationProbe(locator, (_filePath, _resolvedFfprobePath, _timeout, _cancellationToken) =>
        {
            invocationCount++;
            return Task.FromResult(invocationCount == 1 ? (TimeSpan?)null : expectedDuration);
        });

        var firstDuration = await probe.TryReadDurationAsync(mediaFilePath, TimeSpan.FromSeconds(1));
        var secondDuration = await probe.TryReadDurationAsync(mediaFilePath, TimeSpan.FromSeconds(1));

        Assert.Null(firstDuration);
        Assert.Equal(expectedDuration, secondDuration);
        Assert.Equal(2, invocationCount);
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

    private string CreateFile(string fileName)
    {
        var filePath = Path.Combine(_tempDirectory, fileName);
        File.WriteAllText(filePath, "media");
        return filePath;
    }
}
