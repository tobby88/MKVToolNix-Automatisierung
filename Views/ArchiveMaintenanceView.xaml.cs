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
    private static readonly GridLength DefaultArchiveItemsHeight = new(1.35, GridUnitType.Star);
    private static readonly GridLength CompactPlannedMaintenanceHeight = new(0.25, GridUnitType.Star);
    private static readonly GridLength DefaultManualCorrectionHeight = new(1.65, GridUnitType.Star);
    private GridLength _manualCorrectionExpandedHeight = DefaultManualCorrectionHeight;
    private GridLength _plannedMaintenanceHeightBeforeManualExpansion;

    /// <summary>
    /// Initialisiert die Ansicht samt XAML-Komponenten.
    /// </summary>
    public ArchiveMaintenanceView()
    {
        InitializeComponent();
        _plannedMaintenanceHeightBeforeManualExpansion = PlannedMaintenanceRow.Height;
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
        ArchiveItemsRow.MinHeight = 0;
        ArchiveItemsRow.Height = GridLength.Auto;
        _plannedMaintenanceHeightBeforeManualExpansion = PlannedMaintenanceRow.Height;
        PlannedMaintenanceRow.MinHeight = 64;
        PlannedMaintenanceRow.Height = CompactPlannedMaintenanceHeight;
        ManualCorrectionRow.MinHeight = 260;
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
        ArchiveItemsRow.MinHeight = 110;
        ArchiveItemsRow.Height = DefaultArchiveItemsHeight;
        PlannedMaintenanceRow.MinHeight = 150;
        PlannedMaintenanceRow.Height = _plannedMaintenanceHeightBeforeManualExpansion;
        ManualCorrectionRow.MinHeight = 0;
        ManualCorrectionRow.Height = GridLength.Auto;
    }
}
