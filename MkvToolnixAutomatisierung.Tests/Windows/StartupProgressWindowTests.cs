using System.Windows;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.ViewModels;
using MkvToolnixAutomatisierung.Windows;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Windows;

public sealed class StartupProgressWindowTests
{
    [Fact]
    public async Task Close_RequestRaisesCancellationAndKeepsWindowOpenUntilProgramClosesIt()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var window = new StartupProgressWindow(new StartupProgressWindowViewModel());
            var cancellationRequested = 0;
            window.StartupCancellationRequested += (_, _) => cancellationRequested++;
            window.Show();

            await WpfTestHost.WaitForIdleAsync();
            window.Close();
            await WpfTestHost.WaitForIdleAsync();

            Assert.Equal(1, cancellationRequested);
            Assert.True(window.IsVisible);

            window.CloseFromProgram();
            await WpfTestHost.WaitForIdleAsync();

            Assert.False(window.IsVisible);
        });
    }
}
