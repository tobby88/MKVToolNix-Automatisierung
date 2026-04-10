using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.ViewModels.Modules;

namespace MkvToolnixAutomatisierung.Views;

/// <summary>
/// Enthält nur UI-spezifische Eingabefilter, die im ViewModel keinen Platz haben.
/// </summary>
public partial class BatchMuxView : UserControl
{
    private static readonly GridLength DefaultEpisodeListRowHeight = new(1.2, GridUnitType.Star);
    private static readonly GridLength ExpandedEpisodeListRowHeight = new(0.9, GridUnitType.Star);
    private static readonly GridLength DefaultSelectedUsageRowHeight = new(1.15, GridUnitType.Star);
    private static readonly GridLength HiddenRowHeight = new(0d);
    private static readonly GridLength DefaultUsageSplitterRowHeight = new(6d);
    private static readonly GridLength ExpandedDetailsRowHeight = new(1.45, GridUnitType.Star);
    private bool _restoreSelectedUsageAfterDetailsCollapse = true;

    /// <summary>
    /// Initialisiert die Batch-Ansicht mit ihren XAML-Komponenten.
    /// </summary>
    public BatchMuxView()
    {
        InitializeComponent();
    }

    private void EpisodeItemsGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not BatchMuxViewModel viewModel || viewModel.SelectedEpisodeItem is null)
        {
            return;
        }

        if (viewModel.SelectedEpisodeItem.RequiresManualCheck
            && !viewModel.SelectedEpisodeItem.IsManualCheckApproved
            && viewModel.OpenSelectedSourcesCommand.CanExecute(null))
        {
            viewModel.OpenSelectedSourcesCommand.Execute(null);
            return;
        }

        if (viewModel.SelectedEpisodeItem.RequiresMetadataReview
            && !viewModel.SelectedEpisodeItem.IsMetadataReviewApproved
            && viewModel.ReviewSelectedMetadataCommand.CanExecute(null))
        {
            viewModel.ReviewSelectedMetadataCommand.Execute(null);
            return;
        }

        DetailsExpander.IsExpanded = true;
        DetailsExpander.BringIntoView();
    }

    private void DetailsExpander_OnExpanded(object sender, RoutedEventArgs e)
    {
        // Im Batch bringt ein bisschen Umverteilung kaum etwas, weil die Verwendungsuebersicht
        // sehr hoch werden kann. Beim Oeffnen der Korrekturen blenden wir diesen Block daher
        // temporaer aus und geben den Platz gezielt an die eigentliche Bearbeitung weiter.
        _restoreSelectedUsageAfterDetailsCollapse = SelectedUsageGroupBox.Visibility == Visibility.Visible;
        EpisodeListRowDefinition.Height = ExpandedEpisodeListRowHeight;
        SelectedUsageRowDefinition.Height = HiddenRowHeight;
        UsageSplitterRowDefinition.Height = HiddenRowHeight;
        DetailsRowDefinition.Height = ExpandedDetailsRowHeight;
        SelectedUsageGroupBox.Visibility = Visibility.Collapsed;
        UsageGridSplitter.Visibility = Visibility.Collapsed;
        DetailsExpander.BringIntoView();
    }

    private void DetailsExpander_OnCollapsed(object sender, RoutedEventArgs e)
    {
        // Nach dem Schliessen stellen wir die normale Uebersicht wieder her.
        EpisodeListRowDefinition.Height = DefaultEpisodeListRowHeight;
        SelectedUsageRowDefinition.Height = DefaultSelectedUsageRowHeight;
        UsageSplitterRowDefinition.Height = DefaultUsageSplitterRowHeight;
        DetailsRowDefinition.Height = GridLength.Auto;
        if (_restoreSelectedUsageAfterDetailsCollapse)
        {
            SelectedUsageGroupBox.Visibility = Visibility.Visible;
            UsageGridSplitter.Visibility = Visibility.Visible;
        }
    }

    private void EpisodeIndexTextBox_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(character => !char.IsDigit(character));
    }

    private void EpisodeIndexTextBox_OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var pastedText = e.DataObject.GetData(DataFormats.Text) as string;
        if (string.IsNullOrWhiteSpace(pastedText) || pastedText.Any(character => !char.IsDigit(character)))
        {
            e.CancelCommand();
        }
    }

    private void EpisodeRangeTextBox_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(character =>
            !char.IsDigit(character)
            && character != '-'
            && character != 'E'
            && character != 'e');
    }

    private void EpisodeRangeTextBox_OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var pastedText = e.DataObject.GetData(DataFormats.Text) as string;
        if (string.IsNullOrWhiteSpace(pastedText)
            || EpisodeFileNameHelper.NormalizeEpisodeNumber(pastedText) == "xx")
        {
            e.CancelCommand();
        }
    }
}
