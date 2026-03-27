using System.Windows;
using System.Windows.Controls;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels;

namespace MkvToolnixAutomatisierung.Windows;

/// <summary>
/// Schlanker Window-Host für die manuelle TVDB-Auswahl; Suchlogik und Zustand liegen im zugehörigen ViewModel.
/// </summary>
public partial class TvdbLookupWindow : Window
{
    private readonly TvdbLookupWindowViewModel _viewModel;
    private bool _loadedOnce;

    public TvdbLookupWindow(EpisodeMetadataLookupService lookupService, EpisodeMetadataGuess guess)
    {
        InitializeComponent();
        _viewModel = new TvdbLookupWindowViewModel(lookupService, guess);
        DataContext = _viewModel;
        Loaded += TvdbLookupWindow_Loaded;
    }

    public TvdbEpisodeSelection? SelectedEpisodeSelection { get; private set; }

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

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.SaveSettings();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
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
