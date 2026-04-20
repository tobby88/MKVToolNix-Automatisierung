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

    private static bool IsEditingElement(DependencyObject? source)
    {
        return FindVisualParent<TextBoxBase>(source) is not null
            || FindVisualParent<ComboBox>(source) is not null
            || FindVisualParent<PasswordBox>(source) is not null;
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
}
