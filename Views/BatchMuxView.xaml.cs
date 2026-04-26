using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    /// <summary>
    /// Leitet <kbd>Space</kbd> in der Episodenliste an den gemeinsamen Grid-Helfer weiter.
    /// Die eigentliche Auswahländerung bleibt im ViewModel; die View kapselt nur das WPF-Tastaturrouting.
    /// </summary>
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

    /// <summary>
    /// Macht die Auswahlspalte per einfachem Linksklick bedienbar, ohne dass zuerst eine DataGrid-Zelle
    /// in den Vordergrund geholt werden muss.
    /// </summary>
    private void EpisodeItemsGrid_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid dataGrid && DataContext is BatchMuxViewModel viewModel)
        {
            DataGridSelectionInput.TryHandleMouseToggle(
                dataGrid,
                e,
                viewModel.ToggleSelectedEpisodeSelectionCommand,
                toggleColumnIndex: 0);
        }
    }

    /// <summary>
    /// Verhindert jede ad-hoc-Header-Sortierung des Grids. Die Batch-Liste wird fachlich ausschließlich
    /// über <see cref="BatchMuxViewModel.SelectedSortMode"/> und die dazugehörige CollectionView gesteuert.
    /// </summary>
    private void EpisodeItemsGrid_OnSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
    }

    /// <summary>
    /// Doppelklicks auf die Auswahlspalte sollen nur die Auswahl ändern, aber keine weiteren Aktionen
    /// wie Quellenprüfung, TVDB-Dialog oder Detailansicht auslösen.
    /// </summary>
    private void EpisodeItemsGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid dataGrid
            && DataGridSelectionInput.IsSelectionColumnSource(dataGrid, e.OriginalSource as DependencyObject))
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

    /// <summary>
    /// Spiegelt während eines laufenden Batchs die aktuell verarbeitete Episode im sichtbaren
    /// Tabellenfenster wider. Im interaktiven Zustand bleibt die Tabelle dagegen komplett in Ruhe,
    /// damit normale Auswahl- und Tastaturbedienung keine Scrollsprünge auslöst.
    /// </summary>
    private void EpisodeItemsGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid dataGrid
            || dataGrid.SelectedItem is null
            || DataContext is not BatchMuxViewModel viewModel
            || viewModel.IsInteractive)
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

    /// <summary>
    /// Hält das sichtbare Batch-Protokoll automatisch am Ende, sobald neuer Text aus Scan oder Mux ankommt.
    /// </summary>
    private void BatchLogTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        // Das Batch-Protokoll ist ein fortlaufender Sitzungslog. Neue Zeilen sollen beim
        // laufenden Batch direkt sichtbar bleiben, ohne dass die TextBox unbegrenzt wächst.
        ReadOnlyTextBoxAutoScroll.ScrollToEndDeferred(sender as TextBox);
    }

    /// <summary>
    /// Scrollt beim Öffnen des Protokolls direkt zur neuesten Zeile.
    /// </summary>
    private void BatchLogExpander_OnExpanded(object sender, RoutedEventArgs e)
    {
        ReadOnlyTextBoxAutoScroll.ScrollToEndDeferred(BatchLogTextBox);
    }

    /// <summary>
    /// Schaltet den unteren Bereich in den Bearbeitungsmodus um. Dabei wird nur die Verwendungsübersicht
    /// ausgeblendet; der Hauptsplitter zwischen Tabelle und Details bleibt bewusst erhalten.
    /// </summary>
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

    /// <summary>
    /// Stellt nach dem Schließen der Detailkorrekturen die normale Zweiteilung des unteren Bereichs wieder her.
    /// </summary>
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

    /// <summary>
    /// Hält die erweiterte Detailfläche bei Größenänderungen des Hosts synchron.
    /// </summary>
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

    /// <summary>
    /// Lässt in numerischen Staffel-/Folgenfeldern nur Ziffern zu.
    /// </summary>
    private void EpisodeIndexTextBox_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(character => !char.IsDigit(character));
    }

    /// <summary>
    /// Verhindert nicht-numerische Einfügeinhalte in Staffel-/Folgenfeldern.
    /// </summary>
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

    /// <summary>
    /// Erlaubt für Mehrfachfolgen neben Ziffern auch Bindestrich und das interne <c>E</c>-Segment.
    /// </summary>
    private void EpisodeRangeTextBox_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(character =>
            !char.IsDigit(character)
            && character != '-'
            && character != 'E'
            && character != 'e');
    }

    /// <summary>
    /// Prüft eingefügte Mehrfachfolgen-Angaben über dieselbe Normalisierung wie der eigentliche Episodencode.
    /// </summary>
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
