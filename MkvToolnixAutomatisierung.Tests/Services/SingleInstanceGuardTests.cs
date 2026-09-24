using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void SecondOwnerIsRejectedAndReleaseAllowsNewOwner()
    {
        var name = @"Local\mkv-instance-test-" + Guid.NewGuid().ToString("N");
        using (var first = SingleInstanceGuard.TryAcquire(name))
        {
            Assert.NotNull(first);
            bool acquired = true;
            var other = new Thread(() => { using var guard = SingleInstanceGuard.TryAcquire(name); acquired = guard is not null; });
            other.Start();
            Assert.True(other.Join(TimeSpan.FromSeconds(10)));
            Assert.False(acquired);
        }
        using var next = SingleInstanceGuard.TryAcquire(name);
        Assert.NotNull(next);
    }

    [Fact]
    public void AbandonedOwnerCanBeRecovered()
    {
        var name = @"Local\mkv-instance-test-" + Guid.NewGuid().ToString("N");
        using var keepHandle = new Mutex(false, name);
        var thread = new Thread(() => keepHandle.WaitOne());
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        using var next = SingleInstanceGuard.TryAcquire(name);
        Assert.NotNull(next);
    }
}
