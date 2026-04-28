using System.Windows;
using System.Windows.Controls;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.Views;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Views;

public sealed class ArchiveMaintenanceLayoutTests
{
    [Fact]
    public async Task ManualCorrectionExpander_HidesPlannedMaintenanceArea_WhenExpanded()
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
                var plannedSplitter = Assert.IsType<GridSplitter>(view.FindName("PlannedMaintenanceSplitter"));
                var plannedGroup = Assert.IsType<GroupBox>(view.FindName("PlannedMaintenanceGroup"));
                var archiveRow = Assert.IsType<RowDefinition>(view.FindName("ArchiveItemsRow"));
                var manualRow = Assert.IsType<RowDefinition>(view.FindName("ManualCorrectionRow"));
                var plannedRow = Assert.IsType<RowDefinition>(view.FindName("PlannedMaintenanceRow"));
                var plannedSplitterRow = Assert.IsType<RowDefinition>(view.FindName("PlannedMaintenanceSplitterRow"));

                Assert.Equal(GridUnitType.Star, archiveRow.Height.GridUnitType);
                Assert.Equal(GridUnitType.Auto, manualRow.Height.GridUnitType);
                Assert.Equal(Visibility.Visible, plannedSplitter.Visibility);
                Assert.Equal(Visibility.Visible, plannedGroup.Visibility);

                expander.IsExpanded = true;
                await WpfTestHost.WaitForIdleAsync();

                Assert.Equal(Visibility.Collapsed, plannedSplitter.Visibility);
                Assert.Equal(Visibility.Collapsed, plannedGroup.Visibility);
                Assert.Equal(GridUnitType.Auto, archiveRow.Height.GridUnitType);
                Assert.Equal(0d, archiveRow.MinHeight);
                Assert.Equal(GridUnitType.Star, manualRow.Height.GridUnitType);
                Assert.True(manualRow.Height.Value >= 1.6d, $"Manual row height was {manualRow.Height}.");
                Assert.Equal(GridUnitType.Pixel, plannedRow.Height.GridUnitType);
                Assert.Equal(0d, plannedRow.Height.Value);
                Assert.Equal(GridUnitType.Pixel, plannedSplitterRow.Height.GridUnitType);
                Assert.Equal(0d, plannedSplitterRow.Height.Value);

                expander.IsExpanded = false;
                await WpfTestHost.WaitForIdleAsync();

                Assert.Equal(GridUnitType.Auto, manualRow.Height.GridUnitType);
                Assert.Equal(Visibility.Visible, plannedSplitter.Visibility);
                Assert.Equal(Visibility.Visible, plannedGroup.Visibility);
                Assert.Equal(GridUnitType.Star, archiveRow.Height.GridUnitType);
                Assert.Equal(GridUnitType.Pixel, plannedSplitterRow.Height.GridUnitType);
                Assert.Equal(6d, plannedSplitterRow.Height.Value);
                Assert.True(archiveRow.MinHeight >= 100d, $"Archive row min height was {archiveRow.MinHeight}.");
                Assert.True(plannedRow.MinHeight >= 150d, $"Planned row min height was {plannedRow.MinHeight}.");
            }
            finally
            {
                window.Close();
            }
        });
    }
}
