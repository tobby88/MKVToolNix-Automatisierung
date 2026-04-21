using System.Collections.ObjectModel;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels;

internal sealed partial class TvdbLookupWindowViewModel
{
    /// <summary>
    /// Lädt beim ersten Öffnen direkt die vorbefüllte TVDB-Suche, sofern bereits ein API-Key vorhanden ist.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        await SearchSeriesAsync(autoLoadEpisodes: true);
    }

    /// <summary>
    /// Startet die TVDB-Seriensuche mit den aktuell sichtbaren Zugangsdaten und Suchfeldern.
    /// </summary>
    /// <param name="autoLoadEpisodes">Lädt nach erfolgreicher Seriensuche direkt die Episodenliste der bevorzugten Serie.</param>
    public async Task SearchSeriesAsync(bool autoLoadEpisodes)
    {
        try
        {
            SetBusy(true, "Suche Serie bei TVDB...");
            var currentSettings = BuildTransientSettings();
            if (string.IsNullOrWhiteSpace(currentSettings.TvdbApiKey))
            {
                ClearLoadedResults();
                StatusText = "TVDB-API-Key fehlt. Bitte zuerst im zentralen Einstellungsdialog hinterlegen.";
                UpdateComparisonSummary();
                return;
            }

            var results = await _lookupService.SearchSeriesAsync(SeriesSearchText.Trim(), currentSettings);

            _seriesResults.Clear();
            _seriesResults.AddRange(results);
            ReplaceItems(SeriesResults, results.Select(result => new SelectableSeriesItem(result)));

            _episodes.Clear();
            ReplaceItems(EpisodeResults, []);
            SelectedEpisodeItem = null;

            if (_seriesResults.Count == 0)
            {
                SelectedSeriesItem = null;
                StatusText = "Keine passende Serie gefunden.";
                UpdateComparisonSummary();
                return;
            }

            var preferredSeries = _lookupService.FindPreferredSeriesResult(_guess, _seriesResults) ?? _seriesResults[0];
            var preferredItem = SeriesResults.FirstOrDefault(result => result.Series.Id == preferredSeries.Id) ?? SeriesResults[0];

            _suppressSeriesSelectionChanged = true;
            SelectedSeriesItem = preferredItem;
            _suppressSeriesSelectionChanged = false;

            StatusText = $"{_seriesResults.Count} Serie(n) gefunden.";

            if (autoLoadEpisodes)
            {
                await LoadEpisodesForSelectedSeriesAsync(autoSelectBest: true);
            }
            else
            {
                UpdateComparisonSummary();
            }
        }
        catch
        {
            StatusText = "TVDB-Suche fehlgeschlagen";
            throw;
        }
        finally
        {
            SetBusy(false, StatusText);
        }
    }

    /// <summary>
    /// Reagiert auf eine manuell geänderte Serienauswahl und lädt die Episoden der gewählten Serie.
    /// </summary>
    public async Task HandleSelectedSeriesSelectionChangedAsync()
    {
        if (_suppressSeriesSelectionChanged || IsBusy || SelectedSeriesItem is null)
        {
            return;
        }

        await LoadEpisodesForSelectedSeriesAsync(autoSelectBest: true);
    }

    private void ClearLoadedResults()
    {
        _seriesResults.Clear();
        _episodes.Clear();
        _suppressSeriesSelectionChanged = true;
        SelectedSeriesItem = null;
        _suppressSeriesSelectionChanged = false;
        SelectedEpisodeItem = null;
        ReplaceItems(SeriesResults, []);
        ReplaceItems(EpisodeResults, []);
    }

    private async Task LoadEpisodesForSelectedSeriesAsync(bool autoSelectBest)
    {
        if (SelectedSeriesItem is null)
        {
            return;
        }

        try
        {
            SetBusy(true, "Lade Episodenliste...");
            var currentSettings = BuildTransientSettings();
            var episodes = await _lookupService.LoadEpisodesAsync(SelectedSeriesItem.Series.Id, currentSettings);

            _episodes.Clear();
            _episodes.AddRange(episodes);
            ApplyEpisodeFilter(autoSelectBest);

            if (SelectedEpisodeItem is null)
            {
                StatusText = $"{_episodes.Count} Episode(n) geladen.";
            }
        }
        catch
        {
            StatusText = "Episodenliste konnte nicht geladen werden";
            throw;
        }
        finally
        {
            SetBusy(false, StatusText);
        }
    }

    private void ApplyEpisodeFilter(bool autoSelectBest)
    {
        var filteredEpisodes = TvdbLookupEpisodeFilter.FilterEpisodes(_episodes, EpisodeSearchText);
        var items = filteredEpisodes
            .OrderBy(episode => episode.SeasonNumber ?? int.MaxValue)
            .ThenBy(episode => episode.EpisodeNumber ?? int.MaxValue)
            .Select(episode => new SelectableEpisodeItem(episode))
            .ToList();

        var previouslySelectedEpisodeId = SelectedEpisodeItem?.Episode.Id;
        ReplaceItems(EpisodeResults, items);
        SelectedEpisodeItem = null;

        if (!autoSelectBest || SelectedSeriesItem is null)
        {
            if (previouslySelectedEpisodeId is int episodeId)
            {
                SelectedEpisodeItem = EpisodeResults.FirstOrDefault(item => item.Episode.Id == episodeId);
            }

            UpdateComparisonSummary();
            return;
        }

        var match = _lookupService.FindBestEpisodeMatch(_guess, SelectedSeriesItem.Series, _episodes);
        if (match is null)
        {
            StatusText = "Keine Episode automatisch sicher vorgewählt.";
            UpdateComparisonSummary();
            return;
        }

        SelectedEpisodeItem = EpisodeResults.FirstOrDefault(item => item.Episode.Id == match.TvdbEpisodeId);
        if (SelectedEpisodeItem is not null)
        {
            StatusText = $"TVDB-Vorschlag: S{match.SeasonNumber}E{match.EpisodeNumber} - {match.EpisodeTitle}";
        }

        UpdateComparisonSummary();
    }

    private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
