using System.Windows;
using System.Windows.Controls;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Windows;

public partial class TvdbLookupWindow : Window
{
    private readonly EpisodeMetadataLookupService _lookupService;
    private readonly EpisodeMetadataGuess _guess;

    private List<TvdbSeriesSearchResult> _seriesResults = [];
    private List<TvdbEpisodeRecord> _episodes = [];
    private bool _isBusy;
    private bool _suppressSeriesSelectionChanged;
    private bool _loadedOnce;

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
        GuessSummaryTextBlock.Text = BuildGuessSummaryText();
        ComparisonSummaryTextBlock.Text = "Noch kein TVDB-Treffer ausgewaehlt.";

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
        await SearchSeriesAsync(autoLoadEpisodes: true);
    }

    private async void SearchSeriesButton_Click(object sender, RoutedEventArgs e)
    {
        await SearchSeriesAsync(autoLoadEpisodes: true);
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

    private void KeepLocalButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettingsCore();
        SelectedEpisodeSelection = null;
        KeepLocalDetection = true;
        DialogResult = true;
    }

    private async void SeriesResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSeriesSelectionChanged || _isBusy)
        {
            return;
        }

        await LoadEpisodesForSelectedSeriesAsync(autoSelectBest: true);
    }

    private void EpisodeResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateComparisonSummary();
    }

    private void EpisodeSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyEpisodeFilter(autoSelectBest: false);
    }

    private async Task SearchSeriesAsync(bool autoLoadEpisodes)
    {
        try
        {
            SetBusy(true, "Suche Serie bei TVDB...");
            SaveSettingsCore();

            _seriesResults = (await _lookupService.SearchSeriesAsync(SeriesSearchTextBox.Text.Trim())).ToList();
            SeriesResultsListBox.ItemsSource = _seriesResults.Select(result => new SelectableSeriesItem(result)).ToList();
            EpisodeResultsListBox.ItemsSource = null;
            _episodes = [];

            if (_seriesResults.Count == 0)
            {
                StatusTextBlock.Text = "Keine passende Serie gefunden.";
                return;
            }

            var preferredSeries = _lookupService.FindPreferredSeriesResult(_guess, _seriesResults) ?? _seriesResults[0];
            var preferredIndex = _seriesResults.FindIndex(result => result.Id == preferredSeries.Id);

            _suppressSeriesSelectionChanged = true;
            SeriesResultsListBox.SelectedIndex = preferredIndex < 0 ? 0 : preferredIndex;
            _suppressSeriesSelectionChanged = false;

            StatusTextBlock.Text = $"{_seriesResults.Count} Serie(n) gefunden.";

            if (autoLoadEpisodes)
            {
                await LoadEpisodesForSelectedSeriesAsync(autoSelectBest: true);
            }
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

    private async Task LoadEpisodesForSelectedSeriesAsync(bool autoSelectBest)
    {
        if (SeriesResultsListBox.SelectedIndex < 0 || SeriesResultsListBox.SelectedIndex >= _seriesResults.Count)
        {
            return;
        }

        try
        {
            SetBusy(true, "Lade Episodenliste...");
            SaveSettingsCore();

            var selectedSeries = _seriesResults[SeriesResultsListBox.SelectedIndex];
            _episodes = (await _lookupService.LoadEpisodesAsync(selectedSeries.Id)).ToList();
            ApplyEpisodeFilter(autoSelectBest);

            StatusTextBlock.Text = $"{_episodes.Count} Episode(n) geladen.";
            UpdateComparisonSummary();
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
            StatusTextBlock.Text = "Keine Episode automatisch sicher vorgewaehlt.";
            UpdateComparisonSummary();
            return;
        }

        var selectedItem = items.FirstOrDefault(item => item.Episode.Id == match.TvdbEpisodeId);
        if (selectedItem is not null)
        {
            EpisodeResultsListBox.SelectedItem = selectedItem;
            StatusTextBlock.Text = $"TVDB-Vorschlag: S{match.SeasonNumber}E{match.EpisodeNumber} - {match.EpisodeTitle}";
        }

        UpdateComparisonSummary();
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
        _isBusy = isBusy;
        IsEnabled = !isBusy;
        StatusTextBlock.Text = statusText;
    }

    private string BuildGuessSummaryText()
    {
        return $"Lokal erkannt: {_guess.SeriesName} - S{NormalizeNumber(_guess.SeasonNumber)}E{NormalizeNumber(_guess.EpisodeNumber)} - {_guess.EpisodeTitle}";
    }

    private void UpdateComparisonSummary()
    {
        if (SeriesResultsListBox.SelectedIndex < 0 || SeriesResultsListBox.SelectedIndex >= _seriesResults.Count)
        {
            ComparisonSummaryTextBlock.Text = "Noch keine TVDB-Serie ausgewaehlt.";
            return;
        }

        if (EpisodeResultsListBox.SelectedItem is not SelectableEpisodeItem selectedEpisode)
        {
            ComparisonSummaryTextBlock.Text = "Noch keine TVDB-Episode ausgewaehlt.";
            return;
        }

        var selectedSeries = _seriesResults[SeriesResultsListBox.SelectedIndex];
        var selectedSeason = FormatNumber(selectedEpisode.Episode.SeasonNumber);
        var selectedEpisodeNumber = FormatNumber(selectedEpisode.Episode.EpisodeNumber);
        var differences = new List<string>();

        if (!string.Equals(_guess.SeriesName.Trim(), selectedSeries.Name.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            differences.Add($"Serie: lokal '{_guess.SeriesName}' -> TVDB '{selectedSeries.Name}'");
        }

        if (!string.Equals(NormalizeNumber(_guess.SeasonNumber), selectedSeason, StringComparison.OrdinalIgnoreCase))
        {
            differences.Add($"Staffel: lokal '{NormalizeNumber(_guess.SeasonNumber)}' -> TVDB '{selectedSeason}'");
        }

        if (!string.Equals(NormalizeNumber(_guess.EpisodeNumber), selectedEpisodeNumber, StringComparison.OrdinalIgnoreCase))
        {
            differences.Add($"Folge: lokal '{NormalizeNumber(_guess.EpisodeNumber)}' -> TVDB '{selectedEpisodeNumber}'");
        }

        if (!string.Equals(_guess.EpisodeTitle.Trim(), selectedEpisode.Episode.Name.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            differences.Add($"Titel: lokal '{_guess.EpisodeTitle}' -> TVDB '{selectedEpisode.Episode.Name}'");
        }

        if (differences.Count == 0)
        {
            ComparisonSummaryTextBlock.Text = "TVDB stimmt mit der lokalen Erkennung überein.";
            return;
        }

        ComparisonSummaryTextBlock.Text = "Abweichungen: " + string.Join(" | ", differences);
    }

    private static string NormalizeNumber(string value)
    {
        return int.TryParse(value, out var number) && number >= 0 ? number.ToString("00") : "xx";
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
