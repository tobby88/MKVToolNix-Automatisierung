using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class LatestStatusProbeTests
{
    [Fact]
    public async Task Request_DoesNotBlockDispatcherAndDiscardsLateOldResult()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            using var probe = new LatestStatusProbe<int>();
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var applied = new List<int>();
            var uiThread = Environment.CurrentManagedThreadId;
            probe.Request(() =>
            {
                Assert.NotEqual(uiThread, Environment.CurrentManagedThreadId);
                entered.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                return 1;
            }, value => applied.Add(value), delayMilliseconds: 0);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var old = probe.Completion;
            probe.Request(() => 2, value =>
            {
                Assert.Equal(uiThread, Environment.CurrentManagedThreadId);
                applied.Add(value);
            }, delayMilliseconds: 0);
            release.Set();
            await old;
            await probe.Completion;
            Assert.Equal([2], applied);
        });
    }

    [Fact]
    public async Task Dispose_PreventsQueuedQueryAndCallback()
    {
        var probe = new LatestStatusProbe<int>();
        var calls = 0;
        probe.Request(() => ++calls, _ => calls++, delayMilliseconds: 500);
        probe.Dispose();
        await probe.Completion;
        Assert.Equal(0, calls);
    }
}
