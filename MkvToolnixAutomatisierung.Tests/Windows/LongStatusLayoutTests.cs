using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.ViewModels;
using MkvToolnixAutomatisierung.Windows;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Windows;

public sealed class LongStatusLayoutTests
{
    private static readonly string LongStatus = string.Concat(Enumerable.Repeat("Network error with a very long archive path. ", 80));

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 1.5)]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    [InlineData(true, 1.5)]
    [InlineData(true, 2)]
    public async Task LookupFooter_ReservesButtonWidth_AndScrollsLongStatus(bool tvdb, double scale)
    {
        await WpfTestHost.RunAsync(() =>
        {
            // Measure the real compiled content, without showing the dialog or starting provider I/O.
            // Scaling models DIP layout, not a native multi-monitor DPI or screen-reader acceptance test.
            var window = tvdb
                ? (Window)new TvdbLookupWindow(new EpisodeMetadataLookupService(new EmptyStore(), null!), new("Series", "Episode", "1", "1"))
                : new ImdbLookupWindow(null, null, new ImdbDatasetSearchService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sqlite")));
            var root = (FrameworkElement)window.Content;
            root.DataContext = new { StatusText = LongStatus, IsInteractive = true, CanApply = true };
            Arrange(root, window.MinWidth - 24, window.MinHeight - 48, scale);
            var scroller = (ScrollViewer)window.FindName("FooterStatusScroller");
            var buttons = ((Grid)scroller.Parent).Children.OfType<StackPanel>().Single().Children.OfType<Button>().ToArray();
            Assert.Equal(3, buttons.Length);
            foreach (var button in buttons)
            {
                Assert.Equal(button.Width, button.ActualWidth, 1);
                AssertInside(root, button);
            }
            Assert.True(scroller.ScrollableHeight > 0);
            Assert.True(scroller.ActualWidth > 150);
            AssertInside(root, scroller);
            window.Close();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Startup_LeavesProgressVisible_WhenDetailsExceedMinimumHeight()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var model = new StartupProgressWindowViewModel();
            model.Report(new("Preparing tools with a longer status message", LongStatus, 63, false));
            var window = new StartupProgressWindow(model);
            var root = (FrameworkElement)window.Content;
            Arrange(root, window.MinWidth - 24, window.MinHeight - 48, 1);
            var bar = (ProgressBar)window.FindName("StartupProgressBar");
            var scroller = (ScrollViewer)window.FindName("DetailScroller");
            Assert.Equal(18, bar.ActualHeight);
            AssertInside(root, bar);
            Assert.True(scroller.ScrollableHeight > 0);
            window.CloseFromProgram();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Recovery_LeavesActionsVisible_WhenStatusIsLong()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var window = new RecoveryMaintenanceWindow(new AppToolPathSettings());
            ((TextBlock)window.FindName("StatusText")).Text = LongStatus;
            var root = (FrameworkElement)window.Content;
            Arrange(root, window.MinWidth - 24, window.MinHeight - 48, 1);
            foreach (var name in new[] { "OpenButton", "RestoreButton", "RecycleButton", "CancelScanButton" })
                AssertInside(root, (Button)window.FindName(name));
            var scroller = (ScrollViewer)((TextBlock)window.FindName("StatusText")).Parent;
            Assert.True(scroller.ScrollableHeight > 0);
            window.Close();
            return Task.CompletedTask;
        });
    }

    private static void Arrange(FrameworkElement root, double width, double height, double scale)
    {
        root.LayoutTransform = new ScaleTransform(scale, scale);
        root.Measure(new Size(width * scale, height * scale));
        root.Arrange(new Rect(0, 0, width * scale, height * scale));
        root.UpdateLayout();
    }

    private static void AssertInside(FrameworkElement root, FrameworkElement child)
    {
        var bounds = child.TransformToAncestor(root).TransformBounds(new Rect(child.RenderSize));
        Assert.True(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= root.ActualWidth + 1 && bounds.Bottom <= root.ActualHeight + 1,
            $"{child.GetType().Name}: {bounds}, root: {root.RenderSize}");
    }

    private sealed class EmptyStore : IAppMetadataStore
    {
        public string SettingsFilePath => string.Empty;
        public AppMetadataSettings Load() => new();
        public void Save(AppMetadataSettings settings) => throw new NotSupportedException();
        public void Update(Action<AppMetadataSettings> action) => throw new NotSupportedException();
    }
}
