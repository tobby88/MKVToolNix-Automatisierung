using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.ViewModels;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class StartupProgressWindowViewModelTests
{
    [Fact]
    public void Report_UpdatesDeterministicProgressState()
    {
        var viewModel = new StartupProgressWindowViewModel();

        viewModel.Report(new ManagedToolStartupProgress(
            "ffprobe wird heruntergeladen...",
            "12 MB / 24 MB",
            50d,
            false));

        Assert.Equal("ffprobe wird heruntergeladen...", viewModel.StatusText);
        Assert.Equal("12 MB / 24 MB", viewModel.DetailText);
        Assert.False(viewModel.IsIndeterminate);
        Assert.Equal(50d, viewModel.ProgressPercent);
        Assert.Equal("50%", viewModel.ProgressText);
    }

    [Fact]
    public void Report_FallsBackToDefaultDetailText()
    {
        var viewModel = new StartupProgressWindowViewModel();

        viewModel.Report(new ManagedToolStartupProgress("Werkzeuge werden geprüft"));

        Assert.Equal("Werkzeuge werden geprüft", viewModel.StatusText);
        Assert.Equal("Bitte warten...", viewModel.DetailText);
        Assert.True(viewModel.IsIndeterminate);
        Assert.Equal("läuft...", viewModel.ProgressText);
    }
}
