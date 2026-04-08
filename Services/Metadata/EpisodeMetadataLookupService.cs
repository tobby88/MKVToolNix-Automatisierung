using System.Collections.Concurrent;

namespace MkvToolnixAutomatisierung.Services.Metadata;

/// <summary>
/// Führt TVDB-Suche, Caching und automatische Auswahlregeln für Serien- und Episodenmetadaten zusammen.
/// </summary>
internal sealed class EpisodeMetadataLookupService
{
    private readonly IAppMetadataStore _store;
    private readonly ITvdbClient _tvdbClient;
    private readonly ConcurrentDictionary<TvdbSeriesSearchCacheKey, IReadOnlyList<TvdbSeriesSearchResult>> _seriesSearchCache = new();
    private readonly ConcurrentDictionary<TvdbEpisodeCacheKey, IReadOnlyList<TvdbEpisodeRecord>> _episodeCache = new();
    private readonly ConcurrentDictionary<TvdbSeriesSearchCacheKey, Task<IReadOnlyList<TvdbSeriesSearchResult>>> _seriesSearchInFlight = new();
    private readonly ConcurrentDictionary<TvdbEpisodeCacheKey, Task<IReadOnlyList<TvdbEpisodeRecord>>> _episodeLoadsInFlight = new();

    /// <summary>
    /// Initialisiert den TVDB-Lookup-Service mit persistentem Settings-Store und API-Client.
    /// </summary>
    /// <param name="store">Persistenter Store für Zugangsdaten und Serien-Mappings.</param>
    /// <param name="tvdbClient">HTTP-Client für TVDB-Serien- und Episodenabfragen.</param>
    public EpisodeMetadataLookupService(IAppMetadataStore store, ITvdbClient tvdbClient)
    {
        _store = store;
        _tvdbClient = tvdbClient;
    }

    /// <summary>
    /// Lädt die aktuell gespeicherten TVDB- und Serien-Mapping-Einstellungen.
    /// </summary>
    /// <returns>Aktueller Metadaten-Einstellungssatz.</returns>
    public AppMetadataSettings LoadSettings()
    {
        return _store.Load();
    }

    /// <summary>
    /// Speichert TVDB-Zugangsdaten und lokale Serien-Mappings.
    /// </summary>
    /// <param name="settings">Zu speichernde Metadaten-Einstellungen.</param>
    public void SaveSettings(AppMetadataSettings settings)
    {
        _store.Save(settings);
    }

    /// <summary>
    /// Sucht ein gespeichertes TVDB-Mapping für einen lokal erkannten Seriennamen.
    /// </summary>
    /// <param name="localSeriesName">Serienname aus Dateiname oder Textmetadaten.</param>
    /// <returns>Gespeichertes Mapping oder <see langword="null"/>.</returns>
    public SeriesMetadataMapping? FindSeriesMapping(string localSeriesName)
    {
        var normalized = EpisodeMetadataMatchingHeuristics.NormalizeText(localSeriesName);
        return LoadSettings().SeriesMappings.FirstOrDefault(mapping =>
            string.Equals(
                EpisodeMetadataMatchingHeuristics.NormalizeText(mapping.LocalSeriesName),
                normalized,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Führt eine TVDB-Seriensuche mit den aktuell gespeicherten Zugangsdaten aus.
    /// </summary>
    /// <param name="query">Suchbegriff aus dem lokal erkannten Seriennamen.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <returns>Gefundene TVDB-Serien.</returns>
    public async Task<IReadOnlyList<TvdbSeriesSearchResult>> SearchSeriesAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        return await SearchSeriesAsync(query, LoadSettings(), cancellationToken);
    }

    /// <summary>
    /// Führt eine TVDB-Seriensuche mit explizit übergebenen Zugangsdaten aus.
    /// </summary>
    /// <param name="query">Suchbegriff aus dem lokal erkannten Seriennamen.</param>
    /// <param name="settings">Explizit zu verwendende TVDB-Einstellungen.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <returns>Gefundene TVDB-Serien.</returns>
    public async Task<IReadOnlyList<TvdbSeriesSearchResult>> SearchSeriesAsync(
        string query,
        AppMetadataSettings settings,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = EpisodeMetadataMatchingHeuristics.NormalizeText(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return [];
        }

        EnsureApiKeyConfigured(settings);
        var cacheKey = new TvdbSeriesSearchCacheKey(
            settings.TvdbApiKey.Trim(),
            settings.TvdbPin?.Trim() ?? string.Empty,
            normalizedQuery);
        if (_seriesSearchCache.TryGetValue(cacheKey, out var cachedResults))
        {
            return cachedResults;
        }

        var requestTask = _seriesSearchInFlight.GetOrAdd(
            cacheKey,
            _ => ExecuteSharedLookupAsync(
                cacheKey,
                _seriesSearchCache,
                _seriesSearchInFlight,
                () => _tvdbClient.SearchSeriesAsync(settings.TvdbApiKey, settings.TvdbPin, query, CancellationToken.None)));

        return await requestTask.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Lädt alle TVDB-Episoden einer Serie mit den aktuell gespeicherten Zugangsdaten.
    /// </summary>
    /// <param name="seriesId">TVDB-Serien-ID.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <returns>Alle geladenen Episoden der Serie.</returns>
    public async Task<IReadOnlyList<TvdbEpisodeRecord>> LoadEpisodesAsync(
        int seriesId,
        CancellationToken cancellationToken = default)
    {
        return await LoadEpisodesAsync(seriesId, LoadSettings(), cancellationToken);
    }

    /// <summary>
    /// Lädt alle TVDB-Episoden einer Serie mit explizit übergebenen Zugangsdaten.
    /// </summary>
    /// <param name="seriesId">TVDB-Serien-ID.</param>
    /// <param name="settings">Explizit zu verwendende TVDB-Einstellungen.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <returns>Alle geladenen Episoden der Serie.</returns>
    public async Task<IReadOnlyList<TvdbEpisodeRecord>> LoadEpisodesAsync(
        int seriesId,
        AppMetadataSettings settings,
        CancellationToken cancellationToken = default)
    {
        EnsureApiKeyConfigured(settings);
        var cacheKey = new TvdbEpisodeCacheKey(
            settings.TvdbApiKey.Trim(),
            settings.TvdbPin?.Trim() ?? string.Empty,
            seriesId);
        if (_episodeCache.TryGetValue(cacheKey, out var cachedEpisodes))
        {
            return cachedEpisodes;
        }

        var requestTask = _episodeLoadsInFlight.GetOrAdd(
            cacheKey,
            _ => ExecuteSharedLookupAsync(
                cacheKey,
                _episodeCache,
                _episodeLoadsInFlight,
                () => _tvdbClient.GetSeriesEpisodesAsync(settings.TvdbApiKey, settings.TvdbPin, seriesId, CancellationToken.None)));

        return await requestTask.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Bestimmt unter mehreren TVDB-Serientreffern den fachlich bevorzugten Kandidaten.
    /// </summary>
    /// <param name="guess">Lokal erkannte Serien- und Episodeninformation.</param>
    /// <param name="seriesResults">Von TVDB zurückgelieferte Serienkandidaten.</param>
    /// <returns>Bevorzugter Serienkandidat oder <see langword="null"/>.</returns>
    public TvdbSeriesSearchResult? FindPreferredSeriesResult(
        EpisodeMetadataGuess guess,
        IReadOnlyList<TvdbSeriesSearchResult> seriesResults)
    {
        var storedMapping = FindSeriesMapping(guess.SeriesName);
        return EpisodeMetadataMatchingHeuristics.FindPreferredSeriesResult(guess, seriesResults, storedMapping);
    }

    /// <summary>
    /// Versucht eine lokale Episodenerkennung automatisch gegen TVDB aufzulösen.
    /// </summary>
    /// <param name="guess">Lokal erkannte Serien-, Staffel-, Folgen- und Titeldaten.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <returns>Auflösungsergebnis inklusive möglicher Freigabepflicht.</returns>
    public async Task<EpisodeMetadataResolutionResult> ResolveAutomaticallyAsync(
        EpisodeMetadataGuess guess,
        CancellationToken cancellationToken = default)
    {
        var settings = LoadSettings();
        if (string.IsNullOrWhiteSpace(settings.TvdbApiKey))
        {
            return new EpisodeMetadataResolutionResult(
                guess,
                Selection: null,
                StatusText: "TVDB-Automatik übersprungen: API-Key fehlt.",
                ConfidenceScore: 0,
                RequiresReview: false,
                QueryWasAttempted: false,
                QuerySucceeded: false);
        }

        try
        {
            var searchResults = await SearchSeriesAsync(guess.SeriesName, cancellationToken);
            var storedMapping = FindSeriesMapping(guess.SeriesName);
            var seriesCandidates = BuildSeriesCandidates(guess, searchResults, storedMapping);

            if (seriesCandidates.Count == 0)
            {
                return new EpisodeMetadataResolutionResult(
                    guess,
                    Selection: null,
                    StatusText: "TVDB: keine passende Serie gefunden. Bitte prüfen.",
                    ConfidenceScore: 0,
                    RequiresReview: true,
                    QueryWasAttempted: true,
                    QuerySucceeded: true);
            }

            var bestMatch = await FindBestAutomaticMatchAsync(guess, seriesCandidates, cancellationToken);
            if (bestMatch is null)
            {
                return new EpisodeMetadataResolutionResult(
                    guess,
                    Selection: null,
                    StatusText: "TVDB: keine Episode sicher zuordenbar. Bitte prüfen.",
                    ConfidenceScore: 0,
                    RequiresReview: true,
                    QueryWasAttempted: true,
                    QuerySucceeded: true);
            }

            var requiresReview = EpisodeMetadataMatchingHeuristics.ShouldRequireReview(bestMatch);
            var statusText = EpisodeMetadataMatchingHeuristics.BuildStatusText(bestMatch, requiresReview);

            if (!requiresReview)
            {
                SaveSeriesMapping(guess.SeriesName, bestMatch.Series);
            }

            return new EpisodeMetadataResolutionResult(
                guess,
                bestMatch.Selection,
                statusText,
                bestMatch.CombinedScore,
                requiresReview,
                QueryWasAttempted: true,
                QuerySucceeded: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new EpisodeMetadataResolutionResult(
                guess,
                Selection: null,
                StatusText: $"TVDB-Automatik fehlgeschlagen: {ex.Message} Bitte prüfen.",
                ConfidenceScore: 0,
                RequiresReview: true,
                QueryWasAttempted: true,
                QuerySucceeded: false);
        }
    }

    /// <summary>
    /// Sucht innerhalb einer konkreten TVDB-Serie die am besten passende Episode.
    /// </summary>
    /// <param name="guess">Lokal erkannte Episodeninformation.</param>
    /// <param name="series">Konkrete TVDB-Serie.</param>
    /// <param name="episodes">Bereits geladene Episoden der Serie.</param>
    /// <returns>Beste Episodenauswahl oder <see langword="null"/>.</returns>
    public TvdbEpisodeSelection? FindBestEpisodeMatch(
        EpisodeMetadataGuess guess,
        TvdbSeriesSearchResult series,
        IReadOnlyList<TvdbEpisodeRecord> episodes)
    {
        return EpisodeMetadataMatchingHeuristics.FindBestEpisodeMatch(guess, series, episodes, seriesScore: 0)?.Selection;
    }

    /// <summary>
    /// Speichert oder aktualisiert das bevorzugte TVDB-Serien-Mapping für einen lokalen Seriennamen.
    /// </summary>
    /// <param name="localSeriesName">Lokal erkannter Serienname.</param>
    /// <param name="series">Ausgewählte TVDB-Serie.</param>
    public void SaveSeriesMapping(string localSeriesName, TvdbSeriesSearchResult series)
    {
        var settings = LoadSettings();
        var normalized = EpisodeMetadataMatchingHeuristics.NormalizeText(localSeriesName);
        var existing = settings.SeriesMappings.FirstOrDefault(mapping =>
            string.Equals(
                EpisodeMetadataMatchingHeuristics.NormalizeText(mapping.LocalSeriesName),
                normalized,
                StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            settings.SeriesMappings.Add(new SeriesMetadataMapping
            {
                LocalSeriesName = localSeriesName,
                TvdbSeriesId = series.Id,
                TvdbSeriesName = series.Name
            });
        }
        else
        {
            existing.LocalSeriesName = localSeriesName;
            existing.TvdbSeriesId = series.Id;
            existing.TvdbSeriesName = series.Name;
        }

        settings.SeriesMappings = settings.SeriesMappings
            .OrderBy(mapping => mapping.LocalSeriesName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _store.Save(settings);
    }

    /// <summary>
    /// Pfad der zugrunde liegenden portablen Settings-Datei.
    /// </summary>
    public string SettingsFilePath => _store.SettingsFilePath;

    private async Task<ScoredAutomaticMatch?> FindBestAutomaticMatchAsync(
        EpisodeMetadataGuess guess,
        IReadOnlyList<SeriesCandidate> seriesCandidates,
        CancellationToken cancellationToken)
    {
        ScoredAutomaticMatch? bestMatch = null;
        var secondBestScore = int.MinValue;

        foreach (var candidate in seriesCandidates)
        {
            var episodes = await LoadEpisodesAsync(candidate.Series.Id, cancellationToken);
            var match = EpisodeMetadataMatchingHeuristics.FindBestEpisodeMatch(guess, candidate.Series, episodes, candidate.SeriesScore);
            if (match is null)
            {
                continue;
            }

            if (bestMatch is null || match.CombinedScore > bestMatch.CombinedScore)
            {
                if (bestMatch is not null)
                {
                    secondBestScore = Math.Max(secondBestScore, bestMatch.CombinedScore);
                }

                bestMatch = new ScoredAutomaticMatch(
                    match.Series,
                    match.Selection,
                    match.CombinedScore,
                    0,
                    candidate.IsStoredFallback,
                    match);
            }
            else
            {
                secondBestScore = Math.Max(secondBestScore, match.CombinedScore);
            }
        }

        return bestMatch is null
            ? null
            : bestMatch with
            {
                ScoreGap = secondBestScore == int.MinValue
                    ? bestMatch.CombinedScore
                    : Math.Max(0, bestMatch.CombinedScore - secondBestScore)
            };
    }

    private List<SeriesCandidate> BuildSeriesCandidates(
        EpisodeMetadataGuess guess,
        IReadOnlyList<TvdbSeriesSearchResult> searchResults,
        SeriesMetadataMapping? storedMapping)
    {
        return EpisodeMetadataMatchingHeuristics.BuildSeriesCandidates(guess, searchResults, storedMapping);
    }

    private static void EnsureApiKeyConfigured(AppMetadataSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.TvdbApiKey))
        {
            throw new InvalidOperationException(
                "Es ist noch kein TVDB-API-Key gespeichert. Bitte im TVDB-Dialog zuerst den API-Key eintragen.");
        }
    }

    /// <summary>
    /// Führt eine identische TVDB-Abfrage höchstens einmal gleichzeitig aus und teilt das Ergebnis
    /// mit allen parallelen Aufrufern desselben Cache-Schlüssels.
    /// </summary>
    /// <typeparam name="TCacheKey">Cache-Schlüsseltyp der Lookup-Art.</typeparam>
    /// <typeparam name="TResult">Elementtyp der zurückgelieferten Listenwerte.</typeparam>
    /// <param name="cacheKey">Eindeutiger Schlüssel der Anfrage.</param>
    /// <param name="cache">Ergebniscache für erfolgreiche Antworten.</param>
    /// <param name="inFlightCache">Zwischencache aktuell laufender identischer Requests.</param>
    /// <param name="fetchAsync">Eigentlicher TVDB-Fetch ohne aufrufergebundenes Cancellation-Token.</param>
    /// <returns>Das erfolgreiche Ergebnis der Lookup-Anfrage.</returns>
    private static async Task<IReadOnlyList<TResult>> ExecuteSharedLookupAsync<TCacheKey, TResult>(
        TCacheKey cacheKey,
        ConcurrentDictionary<TCacheKey, IReadOnlyList<TResult>> cache,
        ConcurrentDictionary<TCacheKey, Task<IReadOnlyList<TResult>>> inFlightCache,
        Func<Task<IReadOnlyList<TResult>>> fetchAsync)
        where TCacheKey : notnull
    {
        try
        {
            var results = await fetchAsync();
            cache[cacheKey] = results;
            return results;
        }
        finally
        {
            inFlightCache.TryRemove(cacheKey, out _);
        }
    }
}

internal readonly record struct TvdbSeriesSearchCacheKey(string ApiKey, string Pin, string Query);

internal readonly record struct TvdbEpisodeCacheKey(string ApiKey, string Pin, int SeriesId);
