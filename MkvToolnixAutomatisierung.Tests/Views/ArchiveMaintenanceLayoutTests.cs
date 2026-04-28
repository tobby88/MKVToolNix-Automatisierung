using System.Windows;
using System.Windows.Controls;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.Views;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Views;

public sealed class ArchiveMaintenanceLayoutTests
{
    [Fact]
    public async Task ManualCorrectionExpander_UsesResizablePrimaryArea_WhenExpanded()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var view = new ArchiveMaintenanceView();
            var window = new Window
            {
                Width = 1200,
                Height = 820,
                Content = view,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -2000,
                Top = -2000
            };

            try
            {
                window.Show();
                await WpfTestHost.WaitForIdleAsync();

                var expander = Assert.IsType<Expander>(view.FindName("ManualCorrectionExpander"));
                var splitter = Assert.IsType<GridSplitter>(view.FindName("ManualCorrectionSplitter"));
                var manualRow = Assert.IsType<RowDefinition>(view.FindName("ManualCorrectionRow"));
                var plannedRow = Assert.IsType<RowDefinition>(view.FindName("PlannedMaintenanceRow"));

                Assert.Equal(GridUnitType.Auto, manualRow.Height.GridUnitType);
                Assert.Equal(Visibility.Collapsed, splitter.Visibility);

                expander.IsExpanded = true;
                await WpfTestHost.WaitForIdleAsync();

                Assert.Equal(Visibility.Visible, splitter.Visibility);
                Assert.Equal(GridUnitType.Star, manualRow.Height.GridUnitType);
                Assert.True(manualRow.Height.Value >= 1.6d, $"Manual row height was {manualRow.Height}.");
                Assert.Equal(GridUnitType.Star, plannedRow.Height.GridUnitType);
                Assert.True(plannedRow.Height.Value <= 0.3d, $"Planned row height was {plannedRow.Height}.");

                expander.IsExpanded = false;
                await WpfTestHost.WaitForIdleAsync();

                Assert.Equal(GridUnitType.Auto, manualRow.Height.GridUnitType);
                Assert.Equal(Visibility.Collapsed, splitter.Visibility);
                Assert.True(plannedRow.MinHeight >= 150d, $"Planned row min height was {plannedRow.MinHeight}.");
            }
            finally
            {
                window.Close();
            }
        });
    }
}
