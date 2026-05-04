using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels;

namespace MkvToolnixAutomatisierung.Windows;

/// <summary>
/// Schlanker Window-Host für den manuellen IMDb-Abgleich.
/// </summary>
public partial class ImdbLookupWindow : Window
{
    private readonly ImdbLookupWindowViewModel _viewModel;
    private bool _loadedOnce;

    internal ImdbLookupWindow(
        ImdbLookupService lookupService,
        ImdbLookupMode lookupMode,
        EpisodeMetadataGuess? guess,
        string? currentImdbId)
    {
        InitializeComponent();
        _viewModel = new ImdbLookupWindowViewModel(lookupService, lookupMode, guess, currentImdbId);
        DataContext = _viewModel;
    }

    /// <summary>
    /// Vom Benutzer bestätigte IMDb-ID nach erfolgreichem Dialogabschluss.
    /// </summary>
    public string? SelectedImdbId { get; private set; }

    /// <summary>
    /// Kennzeichnet die bewusste Entscheidung, für diese Episode keine IMDb-ID zu vergeben.
    /// </summary>
    public bool ImdbExplicitlyUnavailable { get; private set; }

    private async void ImdbLookupWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce)
        {
            return;
        }

        _loadedOnce = true;
        await RunUiActionAsync(() => _viewModel.InitializeAsync(), "IMDb-Fehler");
    }

    private async void SearchSeriesButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => _viewModel.SearchSeriesAsync(autoLoadEpisodes: true), "IMDb-Fehler");
    }

    private async void SeriesResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await RunUiActionAsync(() => _viewModel.HandleSelectedSeriesSelectionChangedAsync(), "IMDb-Fehler");
    }

    private async void PreviousSeasonButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => _viewModel.LoadPreviousEpisodeSeasonAsync(), "IMDb-Fehler");
    }

    private async void LoadSeasonButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => _viewModel.LoadSelectedEpisodeSeasonAsync(), "IMDb-Fehler");
    }

    private async void NextSeasonButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => _viewModel.LoadNextEpisodeSeasonAsync(), "IMDb-Fehler");
    }

    private void OpenSearchButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedSearch();
    }

    private void OpenCurrentBrowserSearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.PrepareBrowserSearchFromCurrentFields())
        {
            return;
        }

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
        ImdbExplicitlyUnavailable = false;
        DialogResult = true;
    }

    private void NoImdbButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedImdbId = null;
        ImdbExplicitlyUnavailable = true;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ImportClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        TryImportClipboard(showInvalidMessage: true);
    }

    private void Window_OnActivated(object? sender, EventArgs e)
    {
        TryImportClipboard(showInvalidMessage: false);
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

    private void TryImportClipboard(bool showInvalidMessage)
    {
        try
        {
            if (!Clipboard.ContainsText())
            {
                if (showInvalidMessage)
                {
                    MessageBox.Show(
                        this,
                        "In der Zwischenablage liegt aktuell kein Text mit IMDb-ID oder IMDb-URL.",
                        "Hinweis",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return;
            }

            var clipboardText = Clipboard.GetText();
            if (_viewModel.TryImportClipboardText(clipboardText))
            {
                return;
            }

            if (showInvalidMessage)
            {
                MessageBox.Show(
                    this,
                    "In der Zwischenablage wurde keine gültige IMDb-ID oder IMDb-URL gefunden.",
                    "Hinweis",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (COMException)
        {
            if (showInvalidMessage)
            {
                MessageBox.Show(
                    this,
                    "Auf die Zwischenablage konnte gerade nicht zugegriffen werden. Bitte gleich noch einmal versuchen.",
                    "Hinweis",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
    }

    private async Task RunUiActionAsync(Func<Task> action, string errorTitle)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, errorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
