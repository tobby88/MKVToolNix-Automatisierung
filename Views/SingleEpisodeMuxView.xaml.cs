using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MkvToolnixAutomatisierung.Views;

/// <summary>
/// Enthält nur UI-spezifische Eingabefilter; Verhalten und Zustand bleiben im ViewModel.
/// </summary>
public partial class SingleEpisodeMuxView : UserControl
{
    /// <summary>
    /// Initialisiert die Einzelmodus-Ansicht mit ihren XAML-Komponenten.
    /// </summary>
    public SingleEpisodeMuxView()
    {
        InitializeComponent();
    }

    private void EpisodeIndexTextBox_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(character => !char.IsDigit(character));
    }

    private void EpisodeIndexTextBox_OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var pastedText = e.DataObject.GetData(DataFormats.Text) as string;
        if (string.IsNullOrWhiteSpace(pastedText) || pastedText.Any(character => !char.IsDigit(character)))
        {
            e.CancelCommand();
        }
    }
}
