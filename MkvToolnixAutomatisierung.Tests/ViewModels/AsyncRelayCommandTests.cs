using MkvToolnixAutomatisierung.ViewModels.Commands;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class AsyncRelayCommandTests
{
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
