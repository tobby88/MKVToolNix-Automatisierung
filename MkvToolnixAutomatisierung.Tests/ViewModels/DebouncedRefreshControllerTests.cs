using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class DebouncedRefreshControllerTests
{
    [Fact]
    public async Task Schedule_ExecutesOnlyLatestPlannedRefresh()
    {
        var controller = new DebouncedRefreshController(TimeSpan.FromMilliseconds(20));
        var executedVersions = new List<int>();

        controller.Schedule((version, cancellationToken) =>
        {
            executedVersions.Add(version);
            return Task.CompletedTask;
        });
        controller.Schedule((version, cancellationToken) =>
        {
            executedVersions.Add(version);
            return Task.CompletedTask;
        });

        await controller.CurrentTask!;

        Assert.Equal([2], executedVersions);
        Assert.False(controller.IsCurrent(1));
        Assert.True(controller.IsCurrent(2));
    }

    [Fact]
    public async Task Cancel_WithInvalidateInFlightRefreshes_MarksRunningVersionAsStale()
    {
        var controller = new DebouncedRefreshController(TimeSpan.Zero);
        var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        controller.Schedule(async (version, cancellationToken) =>
        {
            started.TrySetResult(version);
            await release.Task;
        });

        var runningVersion = await started.Task;
        controller.Cancel(invalidateInFlightRefreshes: true);
        release.TrySetResult(true);

        Assert.False(controller.IsCurrent(runningVersion));
    }
}
