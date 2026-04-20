using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.ViewModels.Modules;

namespace MkvToolnixAutomatisierung.Views;

/// <summary>
/// Enthält nur UI-spezifische Eingabefilter, die im ViewModel keinen Platz haben.
/// </summary>
public partial class BatchMuxView : UserControl
{
    private const double DefaultSelectedUsageMinHeight = 150d;
    private static readonly GridLength DefaultEpisodeListRowHeight = new(1.2, GridUnitType.Star);
    private static readonly GridLength ExpandedEpisodeListRowHeight = new(0.9, GridUnitType.Star);
    private static readonly GridLength DefaultDetailPanelRowHeight = new(1.15, GridUnitType.Star);
    private static readonly GridLength ExpandedDetailPanelRowHeight = new(1.45, GridUnitType.Star);
    private static readonly GridLength DefaultSelectedUsageRowHeight = new(1, GridUnitType.Star);
    private static readonly GridLength HiddenRowHeight = new(0d);
    private static readonly GridLength ExpandedDetailsRowHeight = new(1, GridUnitType.Star);
    private bool _restoreSelectedUsageAfterDetailsCollapse = true;

    /// <summary>
    /// Initialisiert die Batch-Ansicht mit ihren XAML-Komponenten.
    /// </summary>
    public BatchMuxView()
    {
        InitializeComponent();
    }

    private void EpisodeItemsGrid_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is DataGrid dataGrid && DataContext is BatchMuxViewModel viewModel)
        {
            DataGridSelectionInput.TryHandleSpaceToggle(
                dataGrid,
                e,
                viewModel.ToggleSelectedEpisodeSelectionCommand);
        }
    }

    private void EpisodeItemsGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject originalSource
            && FindVisualParent<CheckBox>(originalSource) is not null)
        {
            e.Handled = true;
            return;
        }

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

    private void EpisodeItemsGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid dataGrid || dataGrid.SelectedItem is null)
        {
            return;
        }

        // Während eines Batch-Laufs setzt das ViewModel die laufende Episode als
        // Auswahl. ScrollIntoView macht den Statuswechsel sichtbar, obwohl die Tabelle
        // zur Vermeidung paralleler Bearbeitung nicht interaktiv ist.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (dataGrid.SelectedItem is not null)
            {
                dataGrid.ScrollIntoView(dataGrid.SelectedItem);
            }
        }));
    }

    private void BatchLogTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        // Das Batch-Protokoll ist ein fortlaufender Sitzungslog. Neue Zeilen sollen beim
        // laufenden Batch direkt sichtbar bleiben, ohne dass die TextBox unbegrenzt wächst.
        textBox.ScrollToEnd();
    }

    private void DetailsExpander_OnExpanded(object sender, RoutedEventArgs e)
    {
        // Der grobe Trenner bleibt zwischen Episodenliste und gesamtem Detailbereich erhalten.
        // Innerhalb des unteren Panels blenden wir nur die Verwendungsuebersicht aus, damit der
        // frei werdende Raum wirklich dem Bearbeitungsbereich zugutekommt.
        _restoreSelectedUsageAfterDetailsCollapse = SelectedUsageGroupBox.Visibility == Visibility.Visible;
        EpisodeListRowDefinition.Height = ExpandedEpisodeListRowHeight;
        DetailPanelRowDefinition.Height = ExpandedDetailPanelRowHeight;
        SelectedUsageRowDefinition.Height = HiddenRowHeight;
        SelectedUsageRowDefinition.MinHeight = 0d;
        DetailsRowDefinition.Height = ExpandedDetailsRowHeight;
        SelectedUsageGroupBox.Visibility = Visibility.Collapsed;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            UpdateExpandedDetailsHeight();
            DetailsExpander.BringIntoView();
        }));
    }

    private void DetailsExpander_OnCollapsed(object sender, RoutedEventArgs e)
    {
        // Nach dem Schliessen stellen wir die normale Uebersicht wieder her.
        EpisodeListRowDefinition.Height = DefaultEpisodeListRowHeight;
        DetailPanelRowDefinition.Height = DefaultDetailPanelRowHeight;
        SelectedUsageRowDefinition.Height = DefaultSelectedUsageRowHeight;
        SelectedUsageRowDefinition.MinHeight = DefaultSelectedUsageMinHeight;
        DetailsRowDefinition.Height = GridLength.Auto;
        DetailsExpander.Height = double.NaN;
        if (_restoreSelectedUsageAfterDetailsCollapse)
        {
            SelectedUsageGroupBox.Visibility = Visibility.Visible;
        }
    }

    private void DetailsHost_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!DetailsExpander.IsExpanded)
        {
            return;
        }

        UpdateExpandedDetailsHeight();
    }

    /// <summary>
    /// Der Detail-Expander soll im Fokusmodus die komplette verfuegbare Host-Hoehe nutzen,
    /// damit der durch das Ausblenden der Verwendungsuebersicht frei gewordene Raum nicht leer bleibt.
    /// </summary>
    private void UpdateExpandedDetailsHeight()
    {
        DetailsExpander.Height = Math.Max(0d, DetailsHost.ActualHeight);
    }

    private static T? FindVisualParent<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T typed)
            {
                return typed;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
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
