using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MkvToolnixAutomatisierung.Views;

/// <summary>
/// Gemeinsame Eingabehilfe fuer DataGrids mit fachlicher Zeilen-Checkbox.
/// </summary>
internal static class DataGridSelectionInput
{
    /// <summary>
    /// Behandelt <kbd>Space</kbd> als explizites Auswahlkommando, bevor das WPF-DataGrid
    /// daraus einen Edit- oder Fokusnavigationsvorgang machen kann.
    /// </summary>
    /// <param name="dataGrid">Das DataGrid, in dessen Tastaturroute der Tastendruck auftritt.</param>
    /// <param name="e">Das PreviewKeyDown-Ereignis des DataGrids.</param>
    /// <param name="toggleCommand">Das fachliche Kommando, das die aktuell markierte Zeile umschaltet.</param>
    /// <returns><see langword="true"/>, wenn der Tastendruck verarbeitet wurde.</returns>
    public static bool TryHandleSpaceToggle(DataGrid dataGrid, KeyEventArgs e, ICommand toggleCommand)
    {
        ArgumentNullException.ThrowIfNull(dataGrid);
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(toggleCommand);

        if (e.Handled || e.Key != Key.Space || IsEditingElement(e.OriginalSource as DependencyObject))
        {
            return false;
        }

        if (!toggleCommand.CanExecute(null))
        {
            e.Handled = true;
            return true;
        }

        var focusTarget = CaptureFocusTarget(dataGrid, e.OriginalSource as DependencyObject);
        toggleCommand.Execute(null);
        RestoreFocus(dataGrid, focusTarget);
        e.Handled = true;
        return true;
    }

    /// <summary>
    /// Behandelt einen direkten Mausklick auf die Zeilen-Checkbox sofort als fachlichen Toggle.
    /// Dadurch braucht die Auswahlspalte keinen ersten Klick nur für die DataGrid-Zellaktivierung.
    /// </summary>
    /// <param name="dataGrid">Das betroffene DataGrid.</param>
    /// <param name="e">Das PreviewMouseLeftButtonDown-Ereignis des DataGrids.</param>
    /// <param name="toggleCommand">Das fachliche Kommando, das die aktuell markierte Zeile umschaltet.</param>
    /// <returns><see langword="true"/>, wenn der Mausklick verarbeitet wurde.</returns>
    public static bool TryHandleMouseToggle(DataGrid dataGrid, MouseButtonEventArgs e, ICommand toggleCommand)
    {
        ArgumentNullException.ThrowIfNull(dataGrid);
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(toggleCommand);

        if (e.Handled
            || e.ChangedButton != MouseButton.Left
            || FindVisualParent<CheckBox>(e.OriginalSource as DependencyObject) is null)
        {
            return false;
        }

        var focusTarget = CaptureFocusTarget(dataGrid, e.OriginalSource as DependencyObject);
        if (focusTarget.Item is not null)
        {
            dataGrid.SelectedItem = focusTarget.Item;
        }

        if (focusTarget.CellInfo.IsValid)
        {
            dataGrid.CurrentCell = focusTarget.CellInfo;
        }

        if (!toggleCommand.CanExecute(null))
        {
            e.Handled = true;
            RestoreFocus(dataGrid, focusTarget);
            return true;
        }

        toggleCommand.Execute(null);
        RestoreFocus(dataGrid, focusTarget);
        e.Handled = true;
        return true;
    }

    private static DataGridFocusTarget CaptureFocusTarget(DataGrid dataGrid, DependencyObject? source)
    {
        var row = FindVisualParent<DataGridRow>(source);
        var cell = FindVisualParent<DataGridCell>(source);
        var item = row?.Item ?? dataGrid.SelectedItem ?? dataGrid.CurrentCell.Item;
        var column = cell?.Column ?? dataGrid.CurrentCell.Column;
        var cellInfo = item is not null && column is not null
            ? new DataGridCellInfo(item, column)
            : dataGrid.CurrentCell;
        return new DataGridFocusTarget(item, column, cellInfo);
    }

    private static void RestoreFocus(DataGrid dataGrid, DataGridFocusTarget focusTarget)
    {
        if (focusTarget.Item is null)
        {
            _ = dataGrid.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => _ = dataGrid.Focus()));
            return;
        }

        // WPF schiebt nach dem Toggle noch interne CurrentCell-/Edit-Navigation auf den Dispatcher.
        // Deshalb restaurieren wir die zuvor aktive Zelle bewusst erst nachgelagert, damit Pfeiltasten
        // weiter im Grid bleiben statt auf Filter, Splitter oder gar nirgendwo zu springen.
        _ = dataGrid.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => RestoreFocusCore(dataGrid, focusTarget)));
    }

    private static void RestoreFocusCore(DataGrid dataGrid, DataGridFocusTarget focusTarget)
    {
        if (focusTarget.Item is null)
        {
            dataGrid.Focus();
            return;
        }

        dataGrid.SelectedItem = focusTarget.Item;
        if (focusTarget.Column is not null)
        {
            dataGrid.CurrentCell = new DataGridCellInfo(focusTarget.Item, focusTarget.Column);
            dataGrid.ScrollIntoView(focusTarget.Item, focusTarget.Column);
            dataGrid.UpdateLayout();

            var cellContent = focusTarget.Column.GetCellContent(focusTarget.Item);
            if (FindVisualParent<DataGridCell>(cellContent) is DataGridCell cell)
            {
                cell.Focus();
                Keyboard.Focus(cell);
                return;
            }
        }

        dataGrid.Focus();
        Keyboard.Focus(dataGrid);
    }

    private static bool IsEditingElement(DependencyObject? source)
    {
        return FindVisualParent<TextBoxBase>(source) is not null
            || FindVisualParent<ComboBox>(source) is not null
            || FindVisualParent<PasswordBox>(source) is not null;
    }

    private readonly record struct DataGridFocusTarget(object? Item, DataGridColumn? Column, DataGridCellInfo CellInfo);

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
}
