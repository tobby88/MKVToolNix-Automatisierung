using System.Windows.Controls;
using System.Windows.Threading;

namespace MkvToolnixAutomatisierung.Views;

/// <summary>
/// Robuste Nachscroll-Hilfe für read-only Textboxen mit laufend angehängtem Protokolltext.
/// </summary>
internal static class ReadOnlyTextBoxAutoScroll
{
    /// <summary>
    /// Plant ein Scrollen ans Textende nachgelagert auf den Dispatcher.
    /// </summary>
    /// <param name="textBox">Die zu aktualisierende TextBox.</param>
    public static void ScrollToEndDeferred(TextBox? textBox)
    {
        if (textBox is null)
        {
            return;
        }

        _ = textBox.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => ScrollToEndCore(textBox)));
    }

    private static void ScrollToEndCore(TextBox textBox)
    {
        if (!textBox.IsLoaded)
        {
            return;
        }

        var textLength = textBox.Text?.Length ?? 0;
        textBox.CaretIndex = textLength;
        textBox.SelectionLength = 0;
        textBox.ScrollToEnd();
    }
}
