using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MkvToolnixAutomatisierung.ViewModels.Modules;

namespace MkvToolnixAutomatisierung.Views;

/// <summary>
/// Reine XAML-Host-Ansicht fuer das Emby-Abgleichsmodul.
/// </summary>
public partial class EmbySyncView : UserControl
{
    /// <summary>
    /// Initialisiert die Ansicht samt XAML-Komponenten.
    /// </summary>
    public EmbySyncView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Leitet <kbd>Space</kbd> in der Emby-Tabelle an die gemeinsame Auswahlhilfe weiter.
    /// </summary>
    private void EmbyItemsGrid_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is DataGrid dataGrid && DataContext is EmbySyncViewModel viewModel)
        {
            DataGridSelectionInput.TryHandleSpaceToggle(
                dataGrid,
                e,
                viewModel.ToggleSelectedItemSelectionCommand);
        }
    }

    /// <summary>
    /// Behandelt direkte Klicks in die Auswahlspalte sofort als fachlichen Toggle, statt das
    /// Grid erst in einen separaten Editmodus zu schicken.
    /// </summary>
    private void EmbyItemsGrid_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid dataGrid && DataContext is EmbySyncViewModel viewModel)
        {
            DataGridSelectionInput.TryHandleMouseToggle(
                dataGrid,
                e,
                viewModel.ToggleSelectedItemSelectionCommand);
        }
    }

    /// <summary>
    /// Hält das Protokoll beim Anhängen neuer Zeilen automatisch am Ende, solange die
    /// Ansicht geöffnet ist.
    /// </summary>
    private void LogTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        ReadOnlyTextBoxAutoScroll.ScrollToEndDeferred(sender as TextBox);
    }

    /// <summary>
    /// Scrollt beim Öffnen des Protokollbereichs direkt ans Textende.
    /// </summary>
    private void LogExpander_OnExpanded(object sender, RoutedEventArgs e)
    {
        ReadOnlyTextBoxAutoScroll.ScrollToEndDeferred(LogTextBox);
    }

    /// <summary>
    /// Öffnet die bekannte TVDB-Suche gezielt für die angeklickte Tabellenzeile, statt nur für die
    /// aktuell irgendwo anders ausgewählte Zeile.
    /// </summary>
    private void TvdbLookupButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EmbySyncViewModel viewModel
            || sender is not FrameworkElement element
            || element.DataContext is not EmbySyncItemViewModel item)
        {
            return;
        }

        viewModel.SelectedItem = item;
        if (viewModel.ReviewSelectedMetadataCommand.CanExecute(null))
        {
            viewModel.ReviewSelectedMetadataCommand.Execute(null);
        }
    }
}
