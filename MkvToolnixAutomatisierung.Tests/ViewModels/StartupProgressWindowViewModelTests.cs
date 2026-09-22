using System.Globalization;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.ViewModels;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class StartupProgressWindowViewModelTests
{
    [Theory]
    [InlineData(-1d, 0d, false)]
    [InlineData(101d, 100d, false)]
    [InlineData(double.NaN, 0d, true)]
    [InlineData(double.PositiveInfinity, 0d, true)]
    [InlineData(double.NegativeInfinity, 0d, true)]
    public void Report_NormalizesInvalidProgressValues(double value, double expected, bool indeterminate)
    {
        var viewModel = new StartupProgressWindowViewModel();

        viewModel.Report(new ManagedToolStartupProgress("Test", ProgressPercent: value, IsIndeterminate: false));

        Assert.Equal(expected, viewModel.ProgressPercent);
        Assert.Equal(indeterminate, viewModel.IsIndeterminate);
    }

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
        Assert.Equal($"Gesamt {50d.ToString("0.0", CultureInfo.CurrentCulture)}%", viewModel.ProgressText);

        viewModel.Report(new ManagedToolStartupProgress(
            "IMDb wird indexiert...",
            ProgressPercent: 64.4d,
            IsIndeterminate: false,
            ProgressLabel: "Import"));

        Assert.Equal("Import", viewModel.ProgressLabel);
        Assert.Equal($"Import {64.4d.ToString("0.0", CultureInfo.CurrentCulture)}%", viewModel.ProgressText);
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
