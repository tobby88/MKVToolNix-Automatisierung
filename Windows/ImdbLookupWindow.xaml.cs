using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
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
    private readonly CancellationTokenSource _localSearchCancellationSource = new();
    internal ImdbLookupWindow(
        EpisodeMetadataGuess? guess,
        string? currentImdbId,
        ImdbDatasetSearchService? imdbDatasetSearch = null)
    {
        InitializeComponent();
        _viewModel = new ImdbLookupWindowViewModel(guess, currentImdbId, imdbDatasetSearch);
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

    private void ApplyLocalCandidateButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ApplySelectedLocalCandidate();
    }

    private void LocalCandidatesListView_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _viewModel.ApplySelectedLocalCandidate();
    }

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshLocalCandidatesAsync(_localSearchCancellationSource.Token);
    }

    private void Window_OnActivated(object? sender, EventArgs e)
    {
        if (!_viewModel.IsBrowserClipboardImportPending)
        {
            return;
        }

        try
        {
            _viewModel.TryImportBrowserClipboardText(ReadClipboardText());
        }
        catch (COMException)
        {
            _viewModel.CancelBrowserClipboardImport();
        }
    }

    private void Window_OnClosed(object? sender, EventArgs e)
    {
        _localSearchCancellationSource.Cancel();
        _localSearchCancellationSource.Dispose();
        _viewModel.Dispose();
    }

    private void OpenSelectedSearch()
    {
        if (_viewModel.SelectedSearchOption is null)
        {
            return;
        }

        _viewModel.CancelBrowserClipboardImport();
        try
        {
            _viewModel.PrepareBrowserClipboardImport(ReadClipboardText());
        }
        catch (COMException)
        {
            // Ohne lesbaren Ausgangswert nur den expliziten Clipboard-Button verwenden.
        }

        try
        {
            // Shell-Aktivierung eines vorhandenen Browsers kann ohne neuen Prozess erfolgreich sein.
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = _viewModel.SelectedSearchOption.TargetUrl,
                UseShellExecute = true
            });
            _viewModel.MarkSelectedSearchOpened();
        }
        catch (Exception ex)
        {
            _viewModel.CancelBrowserClipboardImport();
            MessageBox.Show(
                this,
                $"Die IMDb-Suche konnte nicht im Browser geöffnet werden.{Environment.NewLine}{Environment.NewLine}Technische Details: {ex.Message}",
                "Hinweis",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static string? ReadClipboardText() => Clipboard.ContainsText() ? Clipboard.GetText() : null;

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
}
