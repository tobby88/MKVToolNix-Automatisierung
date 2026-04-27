using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.ViewModels.Commands;
using MkvToolnixAutomatisierung.Views;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Views;

public sealed class PlanReviewLayoutTests
{
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
        public bool HasPendingPlanReview => true;

        public string PrimaryActionablePlanNote => "Mehrfachfolge erkannt: Dieser lange Hinweis muss umbrechen, ohne den Freigabe-Button am rechten Rand zusammenzudrücken.";

        public RelayCommand ApprovePlanReviewCommand { get; } = new(() => { });
    }

    private sealed class BatchPlanReviewData
    {
        public SelectedEpisodePlanReviewData SelectedEpisodeItem { get; set; } = new();

        public RelayCommand ApproveSelectedPlanReviewCommand { get; } = new(() => { });
    }

    private sealed class SelectedEpisodePlanReviewData
    {
        public string Title => "Testfolge";

        public string MetadataDisplayText => "S01E01";

        public bool HasActionablePlanNotes => true;

        public string PrimaryActionablePlanNote => "Mehrfachfolge erkannt: Dieser lange Hinweis muss umbrechen, ohne den Freigabe-Button zusammenzudrücken.";
    }
}
