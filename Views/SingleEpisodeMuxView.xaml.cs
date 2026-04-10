using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Views;

/// <summary>
/// Enthält nur UI-spezifische Eingabefilter; Verhalten und Zustand bleiben im ViewModel.
/// </summary>
public partial class SingleEpisodeMuxView : UserControl
{
    private static readonly GridLength DefaultOverviewRowHeight = new(1, GridUnitType.Star);
    private static readonly GridLength ExpandedOverviewRowHeight = new(0.95, GridUnitType.Star);
    private static readonly GridLength ExpandedCorrectionsRowHeight = new(1.25, GridUnitType.Star);
    private static readonly GridLength DefaultStatusRowHeight = new(1, GridUnitType.Star);
    private static readonly GridLength ExpandedStatusRowHeight = new(0.9, GridUnitType.Star);

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

    private void EpisodeRangeTextBox_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(character =>
            !char.IsDigit(character)
            && character != '-'
            && character != 'E'
            && character != 'e');
    }

    private void EpisodeRangeTextBox_OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var pastedText = e.DataObject.GetData(DataFormats.Text) as string;
        if (string.IsNullOrWhiteSpace(pastedText)
            || EpisodeFileNameHelper.NormalizeEpisodeNumber(pastedText) == "xx")
        {
            e.CancelCommand();
        }
    }

    private void CorrectionExpander_OnExpanded(object sender, RoutedEventArgs e)
    {
        // Der Korrekturbereich soll beim Oeffnen echten Arbeitsraum bekommen, ohne dass die
        // restliche Ansicht komplett verschwindet. Deshalb verteilen wir die Sternzeilen neu.
        OverviewRowDefinition.Height = ExpandedOverviewRowHeight;
        CorrectionsRowDefinition.Height = ExpandedCorrectionsRowHeight;
        StatusRowDefinition.Height = ExpandedStatusRowHeight;
        CorrectionExpander.BringIntoView();
    }

    private void CorrectionExpander_OnCollapsed(object sender, RoutedEventArgs e)
    {
        // Im Ruhezustand kehrt die kompakte Standardaufteilung zurueck.
        OverviewRowDefinition.Height = DefaultOverviewRowHeight;
        CorrectionsRowDefinition.Height = GridLength.Auto;
        StatusRowDefinition.Height = DefaultStatusRowHeight;
    }
}
