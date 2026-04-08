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
