using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MkvToolnixAutomatisierung.Views;

/// <summary>
/// Robuste Nachscroll-Hilfe für read-only Textboxen mit laufend angehängtem Protokolltext.
/// </summary>
internal static class ReadOnlyTextBoxAutoScroll
{
    private static readonly ConditionalWeakTable<TextBox, PendingScroll> PendingScrolls = new();

    private sealed class PendingScroll
    {
        public bool IsScheduled { get; set; }
    }

    /// <summary>
    /// Plant ein Scrollen ans Textende nachgelagert auf den Dispatcher.
    /// </summary>
    /// <param name="textBox">Die zu aktualisierende TextBox.</param>
    public static void ScrollToEndDeferred(TextBox? textBox)
    {
        if (textBox is null || textBox.Dispatcher.HasShutdownStarted)
        {
            return;
        }

        var pending = PendingScrolls.GetValue(textBox, static _ => new PendingScroll());
        if (pending.IsScheduled)
        {
            return;
        }

        pending.IsScheduled = true;
        _ = textBox.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                pending.IsScheduled = false;
                ScrollToEndCore(textBox);
            }));
    }

    private static void ScrollToEndCore(TextBox textBox)
    {
        if (!textBox.IsLoaded)
        {
            return;
        }

        // Protokolltext bleibt beim Kopieren markiert; ScrollToEnd benoetigt keine Caret-Aenderung.
        if (textBox.SelectionLength > 0)
        {
            return;
        }

        textBox.ScrollToEnd();
    }
}
