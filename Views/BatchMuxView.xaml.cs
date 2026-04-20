using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

    private void EpisodeItemsGrid_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space
            || DataContext is not BatchMuxViewModel viewModel
            || !viewModel.IsInteractive
            || viewModel.SelectedEpisodeItem is null
            || sender is not DataGrid dataGrid)
        {
            return;
        }

        var selectedItem = viewModel.SelectedEpisodeItem;
        selectedItem.IsSelected = !selectedItem.IsSelected;
        e.Handled = true;

        // Der Space-Toggle soll sich wie eine echte Tabellenbedienung anfühlen. Die aktuelle
        // Zelle bleibt sofort gesetzt; aktiv nachfokussiert wird aber nur, wenn WPF den
        // Tastaturfokus wirklich aus der Tabelle herausbewegt. Das vermeidet sichtbares
        // Auswahl-Flackern, hält aber den Schutz gegen Fokus-Sprünge zu Filter/Splitter.
        EnsureEpisodeGridCurrentCell(dataGrid, selectedItem);
        Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
        {
            if (!dataGrid.IsKeyboardFocusWithin)
            {
                RestoreEpisodeGridKeyboardFocus(dataGrid, selectedItem);
                return;
            }

            EnsureEpisodeGridCurrentCell(dataGrid, selectedItem);
        }));
    }

    private static void EnsureEpisodeGridCurrentCell(DataGrid dataGrid, object selectedItem)
    {
        if (!dataGrid.IsEnabled || dataGrid.Columns.Count == 0)
        {
            return;
        }

        var focusColumn = dataGrid.CurrentCell.Column ?? dataGrid.Columns[0];
        dataGrid.SelectedItem = selectedItem;
        dataGrid.CurrentItem = selectedItem;
        dataGrid.CurrentCell = new DataGridCellInfo(selectedItem, focusColumn);
        dataGrid.ScrollIntoView(selectedItem, focusColumn);
    }

    private static void RestoreEpisodeGridKeyboardFocus(DataGrid dataGrid, object selectedItem)
    {
        if (!dataGrid.IsEnabled || dataGrid.Columns.Count == 0)
        {
            return;
        }

        EnsureEpisodeGridCurrentCell(dataGrid, selectedItem);
        var focusColumn = dataGrid.CurrentCell.Column ?? dataGrid.Columns[0];
        dataGrid.UpdateLayout();

        var row = dataGrid.ItemContainerGenerator.ContainerFromItem(selectedItem) as DataGridRow;
        if (row is null)
        {
            dataGrid.Focus();
            Keyboard.Focus(dataGrid);
            return;
        }

        var cell = FindVisualChild<DataGridCellsPresenter>(row)
            ?.ItemContainerGenerator.ContainerFromIndex(dataGrid.Columns.IndexOf(focusColumn)) as DataGridCell;
        var focusTarget = (IInputElement?)cell ?? row;
        Keyboard.Focus(focusTarget);
        if (focusTarget is Control control)
        {
            control.Focus();
        }
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

    private static T? FindVisualChild<T>(DependencyObject current)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
        {
            var child = VisualTreeHelper.GetChild(current, index);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
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
