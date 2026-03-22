using System.Windows;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Windows;

public partial class TvdbLookupWindow : Window
{
    private readonly EpisodeMetadataLookupService _lookupService;
    private readonly EpisodeMetadataGuess _guess;

    private List<TvdbSeriesSearchResult> _seriesResults = [];
    private List<TvdbEpisodeRecord> _episodes = [];

    public TvdbLookupWindow(EpisodeMetadataLookupService lookupService, EpisodeMetadataGuess guess)
    {
        InitializeComponent();
        _lookupService = lookupService;
        _guess = guess;

        var settings = _lookupService.LoadSettings();
        ApiKeyTextBox.Text = settings.TvdbApiKey;
        PinTextBox.Text = settings.TvdbPin;
        SeriesSearchTextBox.Text = guess.SeriesName;
        EpisodeSearchTextBox.Text = guess.EpisodeTitle;

        var mapping = _lookupService.FindSeriesMapping(guess.SeriesName);
        if (mapping is not null)
        {
            StatusTextBlock.Text = $"Gespeicherte TVDB-Serie gefunden: {mapping.TvdbSeriesName} ({mapping.TvdbSeriesId})";
        }
    }

    public TvdbEpisodeSelection? SelectedEpisodeSelection { get; private set; }

    private async void SearchSeriesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true, "Suche Serie bei TVDB...");
            SaveSettingsCore();

            _seriesResults = (await _lookupService.SearchSeriesAsync(SeriesSearchTextBox.Text.Trim())).ToList();
            SeriesResultsListBox.ItemsSource = _seriesResults.Select(result => new SelectableSeriesItem(result)).ToList();

            var storedMapping = _lookupService.FindSeriesMapping(_guess.SeriesName);
            if (storedMapping is not null)
            {
                var storedIndex = _seriesResults.FindIndex(result => result.Id == storedMapping.TvdbSeriesId);
                if (storedIndex >= 0)
                {
                    SeriesResultsListBox.SelectedIndex = storedIndex;
                }
            }

            StatusTextBlock.Text = _seriesResults.Count == 0
                ? "Keine passende Serie gefunden."
                : $"{_seriesResults.Count} Serie(n) gefunden.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "TVDB-Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusTextBlock.Text = "TVDB-Suche fehlgeschlagen";
        }
        finally
        {
            SetBusy(false, StatusTextBlock.Text);
        }
    }

    private async void LoadEpisodesButton_Click(object sender, RoutedEventArgs e)
    {
        if (SeriesResultsListBox.SelectedIndex < 0 || SeriesResultsListBox.SelectedIndex >= _seriesResults.Count)
        {
            MessageBox.Show(this, "Bitte zuerst eine Serie auswaehlen.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            SetBusy(true, "Lade Episodenliste...");
            SaveSettingsCore();

            var selectedSeries = _seriesResults[SeriesResultsListBox.SelectedIndex];
            _episodes = (await _lookupService.LoadEpisodesAsync(selectedSeries.Id)).ToList();
            ApplyEpisodeFilter(autoSelectBest: true);

            StatusTextBlock.Text = $"{_episodes.Count} Episode(n) geladen.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "TVDB-Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusTextBlock.Text = "Episodenliste konnte nicht geladen werden";
        }
        finally
        {
            SetBusy(false, StatusTextBlock.Text);
        }
    }

    private void FilterEpisodesButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyEpisodeFilter(autoSelectBest: false);
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettingsCore();
            StatusTextBlock.Text = $"TVDB-Einstellungen gespeichert: {_lookupService.SettingsFilePath}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (SeriesResultsListBox.SelectedIndex < 0 || SeriesResultsListBox.SelectedIndex >= _seriesResults.Count)
        {
            MessageBox.Show(this, "Bitte zuerst eine Serie auswaehlen.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (EpisodeResultsListBox.SelectedItem is not SelectableEpisodeItem selectedEpisode)
        {
            MessageBox.Show(this, "Bitte zuerst eine Episode auswaehlen.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveSettingsCore();
        var selectedSeries = _seriesResults[SeriesResultsListBox.SelectedIndex];
        _lookupService.SaveSeriesMapping(_guess.SeriesName, selectedSeries);

        SelectedEpisodeSelection = new TvdbEpisodeSelection(
            selectedSeries.Id,
            selectedSeries.Name,
            selectedEpisode.Episode.Id,
            selectedEpisode.Episode.Name,
            FormatNumber(selectedEpisode.Episode.SeasonNumber),
            FormatNumber(selectedEpisode.Episode.EpisodeNumber));

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void SeriesResultsListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _episodes = [];
        EpisodeResultsListBox.ItemsSource = null;
    }

    private void ApplyEpisodeFilter(bool autoSelectBest)
    {
        var filteredEpisodes = _episodes;
        var searchText = EpisodeSearchTextBox.Text.Trim();

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            filteredEpisodes = filteredEpisodes
                .Where(episode => episode.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var items = filteredEpisodes
            .OrderBy(episode => episode.SeasonNumber ?? int.MaxValue)
            .ThenBy(episode => episode.EpisodeNumber ?? int.MaxValue)
            .Select(episode => new SelectableEpisodeItem(episode))
            .ToList();

        EpisodeResultsListBox.ItemsSource = items;

        if (!autoSelectBest || SeriesResultsListBox.SelectedIndex < 0 || SeriesResultsListBox.SelectedIndex >= _seriesResults.Count)
        {
            return;
        }

        var selectedSeries = _seriesResults[SeriesResultsListBox.SelectedIndex];
        var match = _lookupService.FindBestEpisodeMatch(_guess, selectedSeries, _episodes);
        if (match is null)
        {
            return;
        }

        var selectedItem = items.FirstOrDefault(item => item.Episode.Id == match.TvdbEpisodeId);
        if (selectedItem is not null)
        {
            EpisodeResultsListBox.SelectedItem = selectedItem;
            StatusTextBlock.Text = $"TVDB-Vorschlag: S{match.SeasonNumber}E{match.EpisodeNumber} - {match.EpisodeTitle}";
        }
    }

    private void SaveSettingsCore()
    {
        _lookupService.SaveSettings(new AppMetadataSettings
        {
            TvdbApiKey = ApiKeyTextBox.Text.Trim(),
            TvdbPin = PinTextBox.Text.Trim(),
            SeriesMappings = _lookupService.LoadSettings().SeriesMappings
        });
    }

    private void SetBusy(bool isBusy, string statusText)
    {
        IsEnabled = !isBusy;
        StatusTextBlock.Text = statusText;
    }

    private static string FormatNumber(int? value)
    {
        return value is null or <= 0 ? "xx" : value.Value.ToString("00");
    }

    private sealed class SelectableSeriesItem
    {
        public SelectableSeriesItem(TvdbSeriesSearchResult series)
        {
            Series = series;
        }

        public TvdbSeriesSearchResult Series { get; }

        public string DisplayText => string.IsNullOrWhiteSpace(Series.Year)
            ? $"{Series.Name} (ID {Series.Id})"
            : $"{Series.Name} ({Series.Year}) - ID {Series.Id}";
    }

    private sealed class SelectableEpisodeItem
    {
        public SelectableEpisodeItem(TvdbEpisodeRecord episode)
        {
            Episode = episode;
        }

        public TvdbEpisodeRecord Episode { get; }

        public string DisplayText => $"S{FormatNumber(Episode.SeasonNumber)}E{FormatNumber(Episode.EpisodeNumber)} - {Episode.Name}";
    }
}
