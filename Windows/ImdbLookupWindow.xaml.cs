using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels;

namespace MkvToolnixAutomatisierung.Windows;

/// <summary>
/// Schlanker Window-Host für den browsergestützten IMDb-Abgleich.
/// </summary>
public partial class ImdbLookupWindow : Window
{
    private readonly ImdbLookupWindowViewModel _viewModel;

    /// <summary>
    /// Initialisiert den Dialog für die manuelle IMDb-Auswahl.
    /// </summary>
    internal ImdbLookupWindow(EpisodeMetadataGuess? guess, string? currentImdbId)
    {
        InitializeComponent();
        _viewModel = new ImdbLookupWindowViewModel(guess, currentImdbId);
        DataContext = _viewModel;
    }

    /// <summary>
    /// Vom Benutzer bestätigte IMDb-ID nach erfolgreichem Dialogabschluss.
    /// </summary>
    public string? SelectedImdbId { get; private set; }

    private void OpenSearchButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedSearch();
    }

    private void SearchOptionsListBox_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelectedSearch();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.TryBuildImdbId(out var imdbId, out var validationMessage))
        {
            MessageBox.Show(
                this,
                validationMessage ?? "Bitte zuerst eine gültige IMDb-ID eintragen.",
                "Hinweis",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SelectedImdbId = imdbId;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OpenSelectedSearch()
    {
        if (_viewModel.SelectedSearchOption is null)
        {
            return;
        }

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = _viewModel.SelectedSearchOption.TargetUrl,
                UseShellExecute = true
            });
            if (process is null)
            {
                MessageBox.Show(
                    this,
                    "Die IMDb-Suche konnte nicht im Browser geöffnet werden.",
                    "Hinweis",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _viewModel.MarkSelectedSearchOpened();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Die IMDb-Suche konnte nicht im Browser geöffnet werden.{Environment.NewLine}{Environment.NewLine}Technische Details: {ex.Message}",
                "Hinweis",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
