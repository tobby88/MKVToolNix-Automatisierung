using System.Windows;
using System.Windows.Controls;

namespace MkvToolnixAutomatisierung.Views;

/// <summary>
/// Reine XAML-Host-Ansicht fuer das Emby-Abgleichsmodul.
/// </summary>
public partial class EmbySyncView : UserControl
{
    /// <summary>
    /// Initialisiert die Ansicht samt XAML-Komponenten.
    /// </summary>
    public EmbySyncView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Hält das Protokoll beim Anhängen neuer Zeilen automatisch am Ende, solange die
    /// Ansicht geöffnet ist.
    /// </summary>
    private void LogTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        ReadOnlyTextBoxAutoScroll.ScrollToEndDeferred(sender as TextBox);
    }

    /// <summary>
    /// Scrollt beim Öffnen des Protokollbereichs direkt ans Textende.
    /// </summary>
    private void LogExpander_OnExpanded(object sender, RoutedEventArgs e)
    {
        ReadOnlyTextBoxAutoScroll.ScrollToEndDeferred(LogTextBox);
    }
}
