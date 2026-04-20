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
    /// Prüft, ob ein Ereignis aus der fachlichen Auswahlspalte stammt.
    /// </summary>
    /// <param name="source">Ursprüngliches WPF-Quellelement des Ereignisses.</param>
    /// <param name="toggleColumnDisplayIndex">DisplayIndex der Auswahlspalte.</param>
    /// <returns><see langword="true"/>, wenn das Ereignis innerhalb der Auswahlspalte ausgelöst wurde.</returns>
    public static bool IsSelectionColumnSource(DependencyObject? source, int toggleColumnDisplayIndex = 0)
    {
        var cell = FindVisualParent<DataGridCell>(source);
        return cell?.Column is not null && cell.Column.DisplayIndex == toggleColumnDisplayIndex;
    }

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
    /// <param name="toggleColumnDisplayIndex">
    /// DisplayIndex der Auswahlspalte. Nur Klicks in diese Spalte loesen den Toggle aus.
    /// </param>
    /// <returns><see langword="true"/>, wenn der Mausklick verarbeitet wurde.</returns>
    public static bool TryHandleMouseToggle(
        DataGrid dataGrid,
        MouseButtonEventArgs e,
        ICommand toggleCommand,
        int toggleColumnDisplayIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(dataGrid);
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(toggleCommand);

        if (e.Handled || e.ChangedButton != MouseButton.Left)
        {
            return false;
        }

        if (!IsSelectionColumnSource(e.OriginalSource as DependencyObject, toggleColumnDisplayIndex))
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
        // Die vorherige Variante wartete immer bis ContextIdle, bevor die Zelle wieder fokussiert
        // wurde. Das stabilisierte zwar manche WPF-Nachläufer, verursachte aber selbst das sichtbare
        // "Ausgrauen": Die Zeile verlor den Fokus erst kurz und bekam ihn dann asynchron zurück.
        // Deshalb restaurieren wir jetzt sofort die aktive Zelle und verwenden den Dispatcher nur
        // noch als Sicherheitsnetz, falls WPF nachgelagert doch noch einmal hineinfunkt.
        RestoreFocusState(dataGrid, focusTarget);
        ScheduleFocusVerification(dataGrid, focusTarget, remainingRetries: 2, DispatcherPriority.Background);
    }

    private static void RestoreFocusState(DataGrid dataGrid, DataGridFocusTarget focusTarget)
    {
        if (focusTarget.Item is null)
        {
            FocusGrid(dataGrid, focusTarget);
            return;
        }

        dataGrid.SelectedItem = focusTarget.Item;
        if (focusTarget.Column is not null)
        {
            dataGrid.CurrentCell = new DataGridCellInfo(focusTarget.Item, focusTarget.Column);
            dataGrid.ScrollIntoView(focusTarget.Item, focusTarget.Column);
            dataGrid.UpdateLayout();
        }

        FocusGrid(dataGrid, focusTarget);
    }

    private static void ScheduleFocusVerification(
        DataGrid dataGrid,
        DataGridFocusTarget focusTarget,
        int remainingRetries,
        DispatcherPriority priority)
    {
        if (remainingRetries <= 0)
        {
            return;
        }

        _ = dataGrid.Dispatcher.BeginInvoke(
            priority,
            new Action(() => VerifyFocus(dataGrid, focusTarget, remainingRetries)));
    }

    private static void VerifyFocus(DataGrid dataGrid, DataGridFocusTarget focusTarget, int remainingRetries)
    {
        if (IsFocusOnSelectedRow(dataGrid, focusTarget))
        {
            return;
        }

        RestoreFocusState(dataGrid, focusTarget);
        if (remainingRetries <= 0)
        {
            return;
        }

        // Manche WPF-Pfade laufen noch einmal spaeter, etwa nach Layout-/Template-Neuaufbau.
        // Reicht die sofortige Rueckfokussierung nicht, pruefen wir mit sinkender Prioritaet
        // nach und ziehen den Fokus hoechstens noch ein weiteres Mal gezielt zur aktiven Zelle.
        ScheduleFocusVerification(
            dataGrid,
            focusTarget,
            remainingRetries - 1,
            DispatcherPriority.ApplicationIdle);
    }

    private static bool IsFocusOnSelectedRow(DataGrid dataGrid, DataGridFocusTarget focusTarget)
    {
        if (focusTarget.Item is null)
        {
            return dataGrid.IsKeyboardFocusWithin;
        }

        if (dataGrid.ItemContainerGenerator.ContainerFromItem(focusTarget.Item) is DataGridRow row)
        {
            return row.IsKeyboardFocusWithin;
        }

        return dataGrid.IsKeyboardFocusWithin;
    }

    private static bool IsEditingElement(DependencyObject? source)
    {
        return FindVisualParent<TextBoxBase>(source) is not null
            || FindVisualParent<ComboBox>(source) is not null
            || FindVisualParent<PasswordBox>(source) is not null;
    }

    private static void FocusGrid(DataGrid dataGrid, DataGridFocusTarget focusTarget)
    {
        if (TryFocusCell(dataGrid, focusTarget))
        {
            return;
        }

        dataGrid.Focus();
        Keyboard.Focus(dataGrid);

        if (Window.GetWindow(dataGrid) is Window window)
        {
            FocusManager.SetFocusedElement(window, dataGrid);
        }
    }

    private readonly record struct DataGridFocusTarget(object? Item, DataGridColumn? Column, DataGridCellInfo CellInfo);

    private static bool TryFocusCell(DataGrid dataGrid, DataGridFocusTarget focusTarget)
    {
        if (focusTarget.Item is null || focusTarget.Column is null)
        {
            return false;
        }

        if (dataGrid.ItemContainerGenerator.ContainerFromItem(focusTarget.Item) is not DataGridRow row)
        {
            return false;
        }

        row.ApplyTemplate();
        if (GetCell(row, focusTarget.Column) is not DataGridCell cell)
        {
            return false;
        }

        cell.Focus();
        Keyboard.Focus(cell);

        if (Window.GetWindow(dataGrid) is Window window)
        {
            FocusManager.SetFocusedElement(window, cell);
        }

        return cell.IsKeyboardFocusWithin;
    }

    private static DataGridCell? GetCell(DataGridRow row, DataGridColumn column)
    {
        if (column.GetCellContent(row)?.Parent is DataGridCell cell)
        {
            return cell;
        }

        row.ApplyTemplate();
        var presenter = FindVisualChild<DataGridCellsPresenter>(row);
        presenter?.ApplyTemplate();
        return presenter?.ItemContainerGenerator.ContainerFromIndex(column.DisplayIndex) as DataGridCell;
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

    private static T? FindVisualChild<T>(DependencyObject? current)
        where T : DependencyObject
    {
        if (current is null)
        {
            return null;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
        {
            var child = VisualTreeHelper.GetChild(current, i);
            if (child is T typed)
            {
                return typed;
            }

            if (FindVisualChild<T>(child) is T descendant)
            {
                return descendant;
            }
        }

        return null;
    }
}
