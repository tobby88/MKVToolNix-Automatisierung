using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.ViewModels.Commands;
using MkvToolnixAutomatisierung.Views;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Views;

public sealed class PlanReviewLayoutTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task MuxTabs_DisableOnlyInactiveTab_AndKeepCancelEnabled(int activeTab)
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var view = new MuxModuleView { DataContext = new BusyMuxTabData { SelectedTabIndex = activeTab } };
            var window = CreateHostWindow(view);
            try
            {
                window.Show();
                await WpfTestHost.WaitForIdleAsync();

                var tabs = Assert.Single(FindVisualChildren<TabControl>(view));
                Assert.Equal(activeTab, tabs.SelectedIndex);
                Assert.True(((TabItem)tabs.Items[activeTab]).IsEnabled);
                Assert.False(((TabItem)tabs.Items[1 - activeTab]).IsEnabled);
                var cancelPath = activeTab == 0 ? "CancelCurrentOperationCommand" : "CancelBatchOperationCommand";
                var cancel = Assert.Single(FindVisualChildren<Button>(view), button =>
                    BindingOperations.GetBinding(button, Button.CommandProperty)?.Path.Path == cancelPath);
                Assert.True(cancel.IsEnabled);
                Assert.True(cancel.IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task SingleEpisodeEditors_AreDisabledWhileBusy_WithoutDisablingCancel()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var view = new SingleEpisodeMuxView { DataContext = new SinglePlanReviewData() };
            var window = CreateHostWindow(view);
            try
            {
                window.Show();
                await WpfTestHost.WaitForIdleAsync();

                var titleEditor = Assert.Single(FindVisualChildren<TextBox>(view), textBox =>
                    BindingOperations.GetBinding(textBox, TextBox.TextProperty)?.Path.Path == "Title");
                Assert.False(titleEditor.IsEnabled);
                var cancelButton = Assert.Single(FindVisualChildren<Button>(view), button => Equals(button.Content, "Abbrechen"));
                Assert.True(cancelButton.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task SingleEpisodePlanReviewButton_RemainsReadable_WhenWarningTextNeedsSpace()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var view = new SingleEpisodeMuxView
            {
                DataContext = new SinglePlanReviewData()
            };
            var window = CreateHostWindow(view);

            try
            {
                window.Show();
                await WpfTestHost.WaitForIdleAsync();

                var button = AssertPlanReviewButton(view);

                Assert.True(button.ActualWidth >= 120d, $"Button width was {button.ActualWidth}.");
                Assert.Contains(FindVisualChildren<TextBlock>(view), text => text.Text.Contains("Zweiter Prüfhinweis", StringComparison.Ordinal));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task BatchPlanReviewButton_UsesSameReadableMinimumWidth()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var view = new BatchMuxView
            {
                DataContext = new BatchPlanReviewData()
            };
            var window = CreateHostWindow(view);

            try
            {
                window.Show();
                await WpfTestHost.WaitForIdleAsync();

                var button = AssertPlanReviewButton(view);

                Assert.True(button.ActualWidth >= 120d, $"Button width was {button.ActualWidth}.");
                Assert.Contains(FindVisualChildren<TextBlock>(view), text => text.Text.Contains("Zweiter Prüfhinweis", StringComparison.Ordinal));
                var grid = Assert.Single(FindVisualChildren<DataGrid>(view));
                Assert.Contains(grid.Columns, column => Equals(column.Header, "Zieldatei"));
                Assert.DoesNotContain(grid.Columns, column => Equals(column.Header, "In Bibliothek"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Window CreateHostWindow(FrameworkElement content)
    {
        return new Window
        {
            Width = 820,
            Height = 640,
            Content = content,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -2000,
            Top = -2000
        };
    }

    private static Button AssertPlanReviewButton(DependencyObject root)
    {
        return Assert.Single(
            FindVisualChildren<Button>(root),
            button => string.Equals(button.Content as string, "Hinweis geprüft", StringComparison.Ordinal));
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                yield return typedChild;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class SinglePlanReviewData
    {
        public bool IsInteractive => false;

        public bool HasPendingPlanReview => true;

        public string PrimaryActionablePlanNote => "Mehrfachfolge erkannt: Dieser lange Hinweis muss umbrechen, ohne den Freigabe-Button am rechten Rand zusammenzudrücken.";

        public string ActionablePlanNotesDisplayText => PrimaryActionablePlanNote + "\nZweiter Prüfhinweis: Auch dieser wird freigegeben.";

        public RelayCommand ApprovePlanReviewCommand { get; } = new(() => { });

        public RelayCommand CancelCurrentOperationCommand { get; } = new(() => { });
    }

    private sealed class BatchPlanReviewData
    {
        public bool IsInteractive => false;

        public bool CanCancelBatchOperation => true;

        public string CancelBatchOperationText => "Batch abbrechen";

        public RelayCommand CancelBatchOperationCommand { get; } = new(() => { });

        public SelectedEpisodePlanReviewData SelectedEpisodeItem { get; set; } = new();

        public RelayCommand ApproveSelectedPlanReviewCommand { get; } = new(() => { });
    }

    private sealed class BusyMuxTabData
    {
        public int SelectedTabIndex { get; set; }

        public bool IsSingleTabEnabled => SelectedTabIndex == 0;

        public bool IsBatchTabEnabled => SelectedTabIndex == 1;

        public SinglePlanReviewData SingleMux { get; } = new();

        public BatchPlanReviewData BatchMux { get; } = new();
    }

    private sealed class SelectedEpisodePlanReviewData
    {
        public string Title => "Testfolge";

        public string MetadataDisplayText => "S01E01";

        public bool HasActionablePlanNotes => true;

        public string PrimaryActionablePlanNote => "Mehrfachfolge erkannt: Dieser lange Hinweis muss umbrechen, ohne den Freigabe-Button zusammenzudrücken.";

        public string ActionablePlanNotesDisplayText => PrimaryActionablePlanNote + "\nZweiter Prüfhinweis: Auch dieser wird freigegeben.";
    }
}
