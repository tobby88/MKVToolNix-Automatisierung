using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MkvToolnixAutomatisierung.ViewModels.Modules;

namespace MkvToolnixAutomatisierung.Views;

/// <summary>
/// XAML-Host für die Archivpflege mit derselben Tabellen-Auswahlbedienung wie Batch, Emby und Einsortieren.
/// </summary>
public partial class ArchiveMaintenanceView : UserControl
{
    /// <summary>
    /// Initialisiert die Ansicht samt XAML-Komponenten.
    /// </summary>
    public ArchiveMaintenanceView()
    {
        InitializeComponent();
    }

    private void ArchiveItemsGrid_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is DataGrid dataGrid && DataContext is ArchiveMaintenanceViewModel viewModel)
        {
            DataGridSelectionInput.TryHandleSpaceToggle(
                dataGrid,
                e,
                viewModel.ToggleSelectedItemSelectionCommand);
        }
    }

    private void ArchiveItemsGrid_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid dataGrid && DataContext is ArchiveMaintenanceViewModel viewModel)
        {
            DataGridSelectionInput.TryHandleMouseToggle(
                dataGrid,
                e,
                viewModel.ToggleSelectedItemSelectionCommand);
        }
    }

    private void LogTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        ReadOnlyTextBoxAutoScroll.ScrollToEndDeferred(sender as TextBox);
    }

    private void LogExpander_OnExpanded(object sender, RoutedEventArgs e)
    {
        ReadOnlyTextBoxAutoScroll.ScrollToEndDeferred(LogTextBox);
    }
}
