using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Windows;

public sealed class StartupBootstrapperTests
{
    [Fact]
    public async Task CreateMainWindowAsync_RejectsMissingDispatcherBeforeStartingServices()
    {
        using var bootstrapper = new AppBootstrapper();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => bootstrapper.CreateMainWindowAsync());

        Assert.Contains("WPF-Dispatcher", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateMainWindowAsync_RejectsDisposedBootstrapper()
    {
        var bootstrapper = new AppBootstrapper();
        bootstrapper.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => bootstrapper.CreateMainWindowAsync());
    }

    [Fact]
    public async Task CreateMainWindowAsync_ObservesCancellationBeforeCreatingServices()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            using var bootstrapper = new AppBootstrapper();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                bootstrapper.CreateMainWindowAsync(cancellationToken: cancellation.Token));
        });
    }
}
