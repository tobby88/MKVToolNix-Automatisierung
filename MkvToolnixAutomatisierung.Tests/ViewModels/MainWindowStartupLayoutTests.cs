using System.Windows;
using MkvToolnixAutomatisierung;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class MainWindowStartupLayoutTests
{
    [Fact]
    public void Constrain_ClampsInitialAndMinimumSize_ToVisibleWorkArea()
    {
        var bounds = MainWindowStartupLayout.Constrain(
            requestedWidth: 1320,
            requestedHeight: 900,
            requestedMinWidth: 1100,
            requestedMinHeight: 760,
            workArea: new Rect(0, 0, 1024, 720));

        Assert.Equal(1024, bounds.Width);
        Assert.Equal(720, bounds.Height);
        Assert.Equal(1024, bounds.MinWidth);
        Assert.Equal(720, bounds.MinHeight);
        Assert.Equal(1024, bounds.MaxWidth);
        Assert.Equal(720, bounds.MaxHeight);
    }

    [Fact]
    public void Constrain_PreservesRequestedSize_WhenWorkAreaIsLargeEnough()
    {
        var bounds = MainWindowStartupLayout.Constrain(
            requestedWidth: 1320,
            requestedHeight: 900,
            requestedMinWidth: 1100,
            requestedMinHeight: 760,
            workArea: new Rect(0, 0, 1920, 1080));

        Assert.Equal(1320, bounds.Width);
        Assert.Equal(900, bounds.Height);
        Assert.Equal(1100, bounds.MinWidth);
        Assert.Equal(760, bounds.MinHeight);
        Assert.Equal(1920, bounds.MaxWidth);
        Assert.Equal(1080, bounds.MaxHeight);
    }
}
