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
        if (_disposed)
        {
            return;
        }

        var (revision, cancellationToken) = BeginRequest();
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

            var results = await _lookupService.SearchSeriesAsync(SeriesSearchText.Trim(), currentSettings, cancellationToken);
            if (revision != _requestRevision)
            {
                return;
            }

            _seriesResults.Clear();
            _seriesResults.AddRange(results);
            _suppressSeriesSelectionChanged = true;
            try
            {
                ReplaceItems(SeriesResults, results.Select(result => new SelectableSeriesItem(result)));
            }
            finally
            {
                _suppressSeriesSelectionChanged = false;
            }

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

            var currentGuess = _guess with { SeriesName = SeriesSearchText.Trim(), EpisodeTitle = EpisodeSearchText.Trim() };
            var preferredSeries = _lookupService.FindPreferredSeriesResult(currentGuess, _seriesResults) ?? _seriesResults[0];
            var preferredItem = SeriesResults.FirstOrDefault(result => result.Series.Id == preferredSeries.Id) ?? SeriesResults[0];

            _suppressSeriesSelectionChanged = true;
            SelectedSeriesItem = preferredItem;
            _suppressSeriesSelectionChanged = false;

            StatusText = $"{_seriesResults.Count} Serie(n) gefunden.";

            if (autoLoadEpisodes)
            {
                await LoadEpisodesForSelectedSeriesAsync(autoSelectBest: true, revision, cancellationToken);
            }
            else
            {
                UpdateComparisonSummary();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (revision == _requestRevision)
            {
                StatusText = ProviderLookupErrorFormatter.FormatTvdbSearchFailure(ex);
                ClearLoadedResults();
                UpdateComparisonSummary();
            }
        }
        finally
        {
            if (revision == _requestRevision)
            {
                SetBusy(false, StatusText);
            }
        }
    }

    /// <summary>
    /// Reagiert auf eine manuell geänderte Serienauswahl und lädt die Episoden der gewählten Serie.
    /// </summary>
    public async Task HandleSelectedSeriesSelectionChangedAsync()
    {
        if (_suppressSeriesSelectionChanged || _disposed || SelectedSeriesItem is null)
        {
            return;
        }

        var (revision, cancellationToken) = BeginRequest();
        await LoadEpisodesForSelectedSeriesAsync(autoSelectBest: true, revision, cancellationToken);
    }

    private void ClearLoadedResults()
    {
        _seriesResults.Clear();
        _episodes.Clear();
        _suppressSeriesSelectionChanged = true;
        try
        {
            SelectedSeriesItem = null;
            SelectedEpisodeItem = null;
            ReplaceItems(SeriesResults, []);
            ReplaceItems(EpisodeResults, []);
        }
        finally
        {
            _suppressSeriesSelectionChanged = false;
        }
    }

    private async Task LoadEpisodesForSelectedSeriesAsync(bool autoSelectBest, int revision, CancellationToken cancellationToken)
    {
        if (SelectedSeriesItem is not { } selectedSeries)
        {
            return;
        }

        try
        {
            SetBusy(true, "Lade Episodenliste...");
            var currentSettings = BuildTransientSettings();
            var episodes = await _lookupService.LoadEpisodesAsync(selectedSeries.Series.Id, currentSettings, cancellationToken);
            if (revision != _requestRevision || !ReferenceEquals(selectedSeries, SelectedSeriesItem))
            {
                return;
            }

            _episodes.Clear();
            _episodes.AddRange(episodes);
            ApplyEpisodeFilter(autoSelectBest);

            if (SelectedEpisodeItem is null)
            {
                StatusText = $"{_episodes.Count} Episode(n) geladen.";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (revision == _requestRevision)
            {
                StatusText = ProviderLookupErrorFormatter.FormatTvdbEpisodeFailure(ex);
                _episodes.Clear();
                ReplaceItems(EpisodeResults, []);
                SelectedEpisodeItem = null;
                UpdateComparisonSummary();
            }
        }
        finally
        {
            if (revision == _requestRevision)
            {
                SetBusy(false, StatusText);
            }
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

        var currentGuess = _guess with { SeriesName = SeriesSearchText.Trim(), EpisodeTitle = EpisodeSearchText.Trim() };
        var match = _lookupService.FindBestEpisodeMatch(currentGuess, SelectedSeriesItem.Series, filteredEpisodes);
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
