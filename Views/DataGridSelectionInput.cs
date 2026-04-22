using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MkvToolnixAutomatisierung.Views;

/// <summary>
/// Gemeinsame Eingabehilfe fuer DataGrids mit fachlicher Zeilen-Checkbox.
/// </summary>
internal static class DataGridSelectionInput
{
    /// <summary>
    /// Prüft, ob ein Ereignis aus der fachlichen Auswahlspalte stammt.
    /// </summary>
    /// <param name="dataGrid">Das betroffene DataGrid mit seiner deklarativen Spaltenreihenfolge.</param>
    /// <param name="source">Ursprüngliches WPF-Quellelement des Ereignisses.</param>
    /// <param name="toggleColumnIndex">Deklarativer Spaltenindex der Auswahlspalte.</param>
    /// <returns><see langword="true"/>, wenn das Ereignis innerhalb der Auswahlspalte ausgelöst wurde.</returns>
    public static bool IsSelectionColumnSource(DataGrid dataGrid, DependencyObject? source, int toggleColumnIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(dataGrid);

        var cell = FindVisualParent<DataGridCell>(source);
        var selectionColumn = ResolveSelectionColumn(dataGrid, toggleColumnIndex);
        return cell?.Column is not null
            && selectionColumn is not null
            && ReferenceEquals(cell.Column, selectionColumn);
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

        toggleCommand.Execute(null);
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

        if (e.Handled || e.ChangedButton != MouseButton.Left || e.ClickCount > 1)
        {
            return false;
        }

        if (!IsSelectionColumnSource(dataGrid, e.OriginalSource as DependencyObject, toggleColumnDisplayIndex))
        {
            return false;
        }

        var focusTarget = CaptureSelectionTarget(dataGrid, e.OriginalSource as DependencyObject);
        if (focusTarget.Item is not null)
        {
            dataGrid.SelectedItem = focusTarget.Item;
        }

        if (focusTarget.Column is not null && focusTarget.Item is not null)
        {
            dataGrid.CurrentCell = new DataGridCellInfo(focusTarget.Item, focusTarget.Column);
        }

        if (!toggleCommand.CanExecute(null))
        {
            e.Handled = true;
            FocusGrid(dataGrid);
            return true;
        }

        toggleCommand.Execute(null);
        FocusGrid(dataGrid);
        e.Handled = true;
        return true;
    }

    /// <summary>
    /// Ermittelt die fachlich aktive Zeile und Spalte aus dem ursprünglichen WPF-Ereignis.
    /// Die Information reicht aus, um bei Mausklicks Auswahl und CurrentCell synchron auf
    /// denselben Batch-/Download-Eintrag zu setzen, ohne einen separaten Editpfad zu öffnen.
    /// </summary>
    private static DataGridSelectionTarget CaptureSelectionTarget(DataGrid dataGrid, DependencyObject? source)
    {
        var row = FindVisualParent<DataGridRow>(source);
        var cell = FindVisualParent<DataGridCell>(source);
        var item = row?.Item ?? dataGrid.SelectedItem ?? dataGrid.CurrentCell.Item;
        var column = cell?.Column ?? dataGrid.CurrentCell.Column;
        return new DataGridSelectionTarget(item, column);
    }

    /// <summary>
    /// Liefert die fachliche Auswahlspalte anhand ihrer deklarativen Position im Grid.
    /// Diese Zuordnung bleibt auch dann stabil, wenn WPF die sichtbaren DisplayIndices der
    /// Spalten zur Laufzeit verändert.
    /// </summary>
    private static DataGridColumn? ResolveSelectionColumn(DataGrid dataGrid, int toggleColumnIndex)
    {
        return toggleColumnIndex >= 0 && toggleColumnIndex < dataGrid.Columns.Count
            ? dataGrid.Columns[toggleColumnIndex]
            : null;
    }

    /// <summary>
    /// Bearbeitbare Eingabeelemente behalten ihr Standardverhalten. Die Auswahl-Shortcuts gelten
    /// nur fuer reine Zeilenoberflächen, nicht etwa fuer TextBoxen in Edit-Templates.
    /// </summary>
    private static bool IsEditingElement(DependencyObject? source)
    {
        return FindVisualParent<TextBoxBase>(source) is not null
            || FindVisualParent<ComboBox>(source) is not null
            || FindVisualParent<PasswordBox>(source) is not null;
    }

    /// <summary>
    /// Legt den Tastaturfokus nach einem expliziten Toggle wieder auf das Grid selbst.
    /// Seit dem Fix in <c>BatchEpisodeItemViewModel.OnPropertyChanged</c> ist kein zusätzlicher
    /// Dispatcher-Nachlauf mehr nötig; das Grid darf direkt fokussiert bleiben.
    /// </summary>
    private static void FocusGrid(DataGrid dataGrid)
    {
        dataGrid.Focus();
        Keyboard.Focus(dataGrid);

        if (Window.GetWindow(dataGrid) is Window window)
        {
            FocusManager.SetFocusedElement(window, dataGrid);
        }
    }

    private readonly record struct DataGridSelectionTarget(object? Item, DataGridColumn? Column);

    /// <summary>
    /// Läuft die visuelle Elternkette nach oben, bis ein passendes WPF-Element gefunden wird.
    /// </summary>
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
