using System.IO;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class ProbeVersionTests
{
    [Fact]
    public void SnapshotRecognizesSameLengthReplacementWithRestoredWriteTime()
    {
        var path = Path.Combine(Path.GetTempPath(), "version-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(path, "old");
            var original = FileStateSnapshot.TryCreate(path)!.Value;
            File.WriteAllText(path + ".new", "new");
            File.SetLastWriteTimeUtc(path + ".new", original.LastWriteTimeUtc);
            File.Move(path + ".new", path, overwrite: true);
            var updated = FileStateSnapshot.TryCreate(path)!.Value;
            Assert.Equal(original.Length, updated.Length);
            Assert.Equal(original.LastWriteTimeUtc, updated.LastWriteTimeUtc);
            Assert.NotEqual(original, updated);
        }
        finally { File.Delete(path); File.Delete(path + ".new"); }
    }

    [Fact]
    public async Task NativeFallbackStopsWaitingAndDoesNotSpawnMoreBlockedReaders()
    {
        var path = Path.Combine(Path.GetTempPath(), "native-probe-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, "test");
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var ended = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        try
        {
            var calls = 0;
            var probe = new WindowsMediaDurationProbe(_ =>
            {
                Interlocked.Increment(ref calls);
                started.Set();
                release.Wait(TimeSpan.FromSeconds(10));
                ended.Set();
                return TimeSpan.FromSeconds(10);
            });
            var pending = Task.Run(() => probe.TryReadDuration(path, cancellation.Token));
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Null(probe.TryReadDuration(path));
            Assert.Equal(1, calls);
        }
        finally { release.Set(); ended.Wait(TimeSpan.FromSeconds(5)); File.Delete(path); }
    }
}
