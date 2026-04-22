using System.Windows;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Composition;

public sealed class AppBootstrapperTests
{
    [Fact]
    public async Task CreateMainWindow_ThrowsOnUiThread()
    {
        await WpfTestHost.RunAsync(() =>
        {
            using var bootstrapper = new AppBootstrapper();
            var exception = Assert.Throws<InvalidOperationException>(() => bootstrapper.CreateMainWindow());
            Assert.Contains("CreateMainWindowAsync", exception.Message, StringComparison.Ordinal);
            return Task.CompletedTask;
        });
    }
}
