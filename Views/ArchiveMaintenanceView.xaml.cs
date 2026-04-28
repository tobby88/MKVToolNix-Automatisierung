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
    private static readonly GridLength DefaultManualCorrectionHeight = new(0.9, GridUnitType.Star);
    private GridLength _manualCorrectionExpandedHeight = DefaultManualCorrectionHeight;

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

    private void ManualCorrectionExpander_OnExpanded(object sender, RoutedEventArgs e)
    {
        ManualCorrectionSplitter.Visibility = Visibility.Visible;
        ManualCorrectionRow.MinHeight = 180;
        ManualCorrectionRow.Height = _manualCorrectionExpandedHeight.GridUnitType == GridUnitType.Auto
            ? DefaultManualCorrectionHeight
            : _manualCorrectionExpandedHeight;
    }

    private void ManualCorrectionExpander_OnCollapsed(object sender, RoutedEventArgs e)
    {
        if (ManualCorrectionRow.Height.GridUnitType != GridUnitType.Auto)
        {
            _manualCorrectionExpandedHeight = ManualCorrectionRow.Height;
        }

        ManualCorrectionSplitter.Visibility = Visibility.Collapsed;
        ManualCorrectionRow.MinHeight = 0;
        ManualCorrectionRow.Height = GridLength.Auto;
    }
}
