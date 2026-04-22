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

    [Fact]
    public async Task Schedule_ClearsCurrentTask_AfterRefreshCompletes()
    {
        var controller = new DebouncedRefreshController(TimeSpan.Zero);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        controller.Schedule(async (version, cancellationToken) =>
        {
            started.TrySetResult(true);
            await release.Task;
        });

        await started.Task;
        var refreshTask = controller.CurrentTask;
        Assert.NotNull(refreshTask);

        release.TrySetResult(true);
        await refreshTask!;

        Assert.Null(controller.CurrentTask);
    }

    [Fact]
    public async Task OlderRefreshCompletion_DoesNotClearNewerCurrentTask()
    {
        var controller = new DebouncedRefreshController(TimeSpan.Zero);
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        controller.Schedule(async (version, cancellationToken) =>
        {
            firstStarted.TrySetResult(true);
            await releaseFirst.Task;
        });

        var firstTask = controller.CurrentTask;
        Assert.NotNull(firstTask);
        await firstStarted.Task;

        controller.Schedule(async (version, cancellationToken) =>
        {
            secondStarted.TrySetResult(true);
            await releaseSecond.Task;
        });

        var secondTask = controller.CurrentTask;
        Assert.NotNull(secondTask);
        Assert.NotSame(firstTask, secondTask);
        await secondStarted.Task;

        releaseFirst.TrySetResult(true);
        await firstTask!;

        Assert.Same(secondTask, controller.CurrentTask);

        releaseSecond.TrySetResult(true);
        await secondTask!;

        Assert.Null(controller.CurrentTask);
    }
}
