using System.Windows.Controls;
using System.Windows.Input;
using MkvToolnixAutomatisierung.ViewModels.Modules;

namespace MkvToolnixAutomatisierung.Views;

/// <summary>
/// Reine XAML-Host-Ansicht fuer das Download-Sortiermodul.
/// </summary>
public partial class DownloadSortView : UserControl
{
    /// <summary>
    /// Initialisiert die Ansicht samt XAML-Komponenten.
    /// </summary>
    public DownloadSortView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Leitet <kbd>Space</kbd> für die Auswahlspalte an den gemeinsamen DataGrid-Helfer weiter.
    /// Dadurch verhalten sich Einsortieren und Batch-Tabelle bei der Tastaturbedienung konsistent.
    /// </summary>
    private void DownloadItemsGrid_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is DataGrid dataGrid && DataContext is DownloadSortViewModel viewModel)
        {
            DataGridSelectionInput.TryHandleSpaceToggle(
                dataGrid,
                e,
                viewModel.ToggleSelectedItemSelectionCommand);
        }
    }

    /// <summary>
    /// Macht die Auswahlspalte im Einsortieren-Modul per einfachem Linksklick bedienbar.
    /// </summary>
    private void DownloadItemsGrid_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid dataGrid && DataContext is DownloadSortViewModel viewModel)
        {
            DataGridSelectionInput.TryHandleMouseToggle(
                dataGrid,
                e,
                viewModel.ToggleSelectedItemSelectionCommand,
                toggleColumnIndex: 0);
        }
    }
}
