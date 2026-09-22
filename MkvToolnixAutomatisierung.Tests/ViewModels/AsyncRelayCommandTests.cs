using MkvToolnixAutomatisierung.ViewModels.Commands;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class AsyncRelayCommandTests
{
    [Fact]
    public async Task ExecuteAsync_IgnoresReentry_AndRestoresStateAfterCancellation()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var changes = 0;
        var command = new AsyncRelayCommand(async () =>
        {
            calls++;
            await release.Task;
            throw new OperationCanceledException();
        }, _ => Assert.Fail("Cancellation is not an error."));
        command.CanExecuteChanged += (_, _) => changes++;

        var running = command.ExecuteAsync();
        await command.ExecuteAsync();
        Assert.Equal(1, calls);
        release.SetResult();
        await running;

        Assert.True(command.CanExecute(null));
        Assert.Equal(2, changes);
    }

    [Fact]
    public void RelayCommand_Execute_RespectsDisabledState()
    {
        var called = false;
        var command = new RelayCommand(() => called = true, () => false);

        command.Execute(null);

        Assert.False(called);
    }

    [Fact]
    public async Task Execute_InvokesInjectedErrorHandler_ForUnexpectedException()
    {
        Exception? capturedException = null;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new AsyncRelayCommand(
            () => Task.FromException(new InvalidOperationException("kaputt")),
            () => true,
            ex =>
            {
                capturedException = ex;
                completion.TrySetResult(true);
            });

        command.Execute(null);
        await completion.Task;

        var exception = Assert.IsType<InvalidOperationException>(capturedException);
        Assert.Equal("kaputt", exception.Message);
    }

    [Fact]
    public async Task Execute_DoesNotInvokeErrorHandler_ForOperationCanceledException()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new AsyncRelayCommand(
            () => Task.FromCanceled(new CancellationToken(canceled: true)),
            () => true,
            ex => completion.TrySetResult(true));

        command.Execute(null);
        await Task.Yield();

        Assert.False(completion.Task.IsCompleted);
    }

    [Fact]
    public async Task CanExecute_IsFalseWhileCommandIsRunning_AndBecomesTrueAfterCompletion()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new AsyncRelayCommand(
            async () => await release.Task,
            () => true,
            _ => throw new Xunit.Sdk.XunitException("Keine Fehlerbehandlung erwartet."));

        Assert.True(command.CanExecute(null));

        var execution = command.ExecuteAsync();
        Assert.False(command.CanExecute(null));

        release.SetResult(true);
        await execution;
        Assert.True(command.CanExecute(null));
    }
}
