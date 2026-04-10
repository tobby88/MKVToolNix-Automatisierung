using System.Windows;

namespace MkvToolnixAutomatisierung;

/// <summary>
/// Begrenzt das Hauptfenster vor dem Anzeigen auf die aktuell sichtbare Arbeitsfläche.
/// </summary>
internal static class MainWindowStartupLayout
{
    /// <summary>
    /// Berechnet aus den gewünschten Fenstermaßen eine sicher sichtbare Startkonfiguration.
    /// Dabei dürfen weder Initial- noch Mindestgröße die verfügbare Arbeitsfläche überschreiten.
    /// </summary>
    /// <param name="requestedWidth">Im XAML hinterlegte Wunschbreite.</param>
    /// <param name="requestedHeight">Im XAML hinterlegte Wunschhöhe.</param>
    /// <param name="requestedMinWidth">Im XAML hinterlegte Mindestbreite.</param>
    /// <param name="requestedMinHeight">Im XAML hinterlegte Mindesthöhe.</param>
    /// <param name="workArea">Sichtbare Arbeitsfläche des Systems ohne Taskleisten.</param>
    /// <returns>Auf die Arbeitsfläche begrenzte Startmaße für das Fenster.</returns>
    public static WindowStartupBounds Constrain(
        double requestedWidth,
        double requestedHeight,
        double requestedMinWidth,
        double requestedMinHeight,
        Rect workArea)
    {
        var maxWidth = Math.Max(1d, workArea.Width);
        var maxHeight = Math.Max(1d, workArea.Height);
        var minWidth = Math.Min(Math.Max(1d, requestedMinWidth), maxWidth);
        var minHeight = Math.Min(Math.Max(1d, requestedMinHeight), maxHeight);

        return new WindowStartupBounds(
            Width: Math.Clamp(requestedWidth, minWidth, maxWidth),
            Height: Math.Clamp(requestedHeight, minHeight, maxHeight),
            MinWidth: minWidth,
            MinHeight: minHeight,
            MaxWidth: maxWidth,
            MaxHeight: maxHeight);
    }

    /// <summary>
    /// Wendet die sichtbare Startkonfiguration direkt auf das Hauptfenster an.
    /// </summary>
    /// <param name="window">Zu begrenzendes Fenster.</param>
    /// <param name="workArea">Sichtbare Arbeitsfläche des Systems ohne Taskleisten.</param>
    public static void ApplyTo(Window window, Rect workArea)
    {
        ArgumentNullException.ThrowIfNull(window);

        var bounds = Constrain(window.Width, window.Height, window.MinWidth, window.MinHeight, workArea);
        // Die Begrenzung soll nur die Startgroesse sichern. Maximierte Fenster duerfen die
        // normalen Systemgrenzen weiter nutzen, sonst koennen auf manchen Setups sichtbare
        // Reststreifen am rechten oder unteren Rand entstehen.
        window.MinWidth = bounds.MinWidth;
        window.MinHeight = bounds.MinHeight;
        window.Width = bounds.Width;
        window.Height = bounds.Height;
    }
}

/// <summary>
/// Auf die sichtbare Arbeitsfläche begrenzte Startmaße des Hauptfensters.
/// </summary>
internal sealed record WindowStartupBounds(
    double Width,
    double Height,
    double MinWidth,
    double MinHeight,
    double MaxWidth,
    double MaxHeight);
