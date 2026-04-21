using System.Windows;
using System.Windows.Controls;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels;

namespace MkvToolnixAutomatisierung.Windows;

/// <summary>
/// Schlanker Window-Host für die manuelle TVDB-Auswahl; Suchlogik und Zustand liegen im zugehörigen ViewModel.
/// </summary>
public partial class TvdbLookupWindow : Window
{
    private readonly TvdbLookupWindowViewModel _viewModel;
    private readonly IAppSettingsDialogService? _settingsDialog;
    private bool _loadedOnce;

    /// <summary>
    /// Initialisiert den Dialog für die manuelle TVDB-Serien- und Episodenauswahl.
    /// </summary>
    /// <param name="lookupService">Service für TVDB-Suche, Episodenladen und Settings-Persistenz.</param>
    /// <param name="guess">Lokal erkannter Startvorschlag für Serie, Staffel, Folge und Titel.</param>
    /// <param name="settingsDialog">Zentraler Einstellungsdialog zum Nachpflegen von API-Key und PIN.</param>
    internal TvdbLookupWindow(
        EpisodeMetadataLookupService lookupService,
        EpisodeMetadataGuess guess,
        IAppSettingsDialogService? settingsDialog = null)
    {
        InitializeComponent();
        _viewModel = new TvdbLookupWindowViewModel(lookupService, guess);
        _settingsDialog = settingsDialog;
        DataContext = _viewModel;
        Loaded += TvdbLookupWindow_Loaded;
    }

    /// <summary>
    /// Vom Benutzer bestätigte TVDB-Zuordnung nach einem erfolgreichen Dialogabschluss.
    /// </summary>
    public TvdbEpisodeSelection? SelectedEpisodeSelection { get; private set; }

    /// <summary>
    /// Kennzeichnet, dass der Benutzer die lokale Erkennung bewusst beibehalten wollte.
    /// </summary>
    public bool KeepLocalDetection { get; private set; }

    private async void TvdbLookupWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce)
        {
            return;
        }

        _loadedOnce = true;
        await RunUiActionAsync(() => _viewModel.InitializeAsync(), "TVDB-Fehler");
    }

    private async void SearchSeriesButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(() => _viewModel.SearchSeriesAsync(autoLoadEpisodes: true), "TVDB-Fehler");
    }

    private void OpenSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsDialog is null)
        {
            MessageBox.Show(this, "Der zentrale Einstellungsdialog ist hier nicht verfügbar.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_settingsDialog.ShowDialog(this, AppSettingsPage.Tvdb))
        {
            _viewModel.ReloadStoredSettings();
        }
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_viewModel.TryBuildSelection(out var selection, out var validationMessage))
            {
                MessageBox.Show(
                    this,
                    validationMessage ?? "Bitte zuerst Serie und Episode auswählen.",
                    "Hinweis",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            SelectedEpisodeSelection = selection;
            KeepLocalDetection = false;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void KeepLocalButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.RememberLocalDetectionChoice();
            SelectedEpisodeSelection = null;
            KeepLocalDetection = true;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SeriesResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await RunUiActionAsync(() => _viewModel.HandleSelectedSeriesSelectionChangedAsync(), "TVDB-Fehler");
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
