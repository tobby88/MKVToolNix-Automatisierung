using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;

namespace MkvToolnixAutomatisierung.Services.Metadata;

/// <summary>
/// Kapselt den optionalen IMDb-Abgleich über <c>api.imdbapi.dev</c>.
/// </summary>
/// <remarks>
/// Der Dienst ist bewusst best effort. Fällt die freie API aus oder ändert ihre Form, bleibt der
/// bestehende browsergestützte Fallback nutzbar. Caching reduziert dabei unnötige Wiederholungsanfragen
/// für dieselben Serien- und Episodendaten.
/// </remarks>
internal sealed class ImdbLookupService
{
    private const int MaxEpisodePagesPerSeason = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Uri ApiBaseAddress = new("https://api.imdbapi.dev/");
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, IReadOnlyList<ImdbSeriesSearchResult>> _seriesSearchCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<ImdbSeriesSearchResult>>> _seriesSearchInFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<ImdbEpisodeRecord>> _episodeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<ImdbEpisodeRecord>>> _episodeLoadsInFlight = new(StringComparer.OrdinalIgnoreCase);

    public ImdbLookupService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Sucht Serien- und Mehrteilerkandidaten anhand eines freien Texts.
    /// </summary>
    public async Task<IReadOnlyList<ImdbSeriesSearchResult>> SearchSeriesAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = EpisodeMetadataMatchingHeuristics.NormalizeText(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return [];
        }

        if (_seriesSearchCache.TryGetValue(normalizedQuery, out var cachedResults))
        {
            return cachedResults;
        }

        var requestTask = _seriesSearchInFlight.GetOrAdd(
            normalizedQuery,
            _ => ExecuteSharedLookupAsync(
                normalizedQuery,
                _seriesSearchCache,
                _seriesSearchInFlight,
                () => FetchSeriesAsync(query, CancellationToken.None)));

        return await requestTask.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Lädt alle Episoden einer Serie über sämtliche Seasons hinweg.
    /// </summary>
    public async Task<IReadOnlyList<ImdbEpisodeRecord>> LoadEpisodesAsync(
        string seriesId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(seriesId))
        {
            return [];
        }

        if (_episodeCache.TryGetValue(seriesId, out var cachedResults))
        {
            return cachedResults;
        }

        var requestTask = _episodeLoadsInFlight.GetOrAdd(
            seriesId,
            _ => ExecuteSharedLookupAsync(
                seriesId,
                _episodeCache,
                _episodeLoadsInFlight,
                () => FetchEpisodesAsync(seriesId, CancellationToken.None)));

        return await requestTask.WaitAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<ImdbSeriesSearchResult>> FetchSeriesAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var requestUri = new Uri(
            ApiBaseAddress,
            $"search/titles?query={Uri.EscapeDataString(query.Trim())}");
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<ImdbSearchTitlesResponse>(stream, JsonOptions, cancellationToken)
                      ?? throw new InvalidOperationException("Die IMDb-API lieferte kein lesbares Suchergebnis.");

        return payload.Titles
            .Where(static title => string.Equals(title.Type, "tvSeries", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(title.Type, "tvMiniSeries", StringComparison.OrdinalIgnoreCase))
            .Select(static title => new ImdbSeriesSearchResult(
                title.Id ?? string.Empty,
                title.PrimaryTitle ?? string.Empty,
                string.IsNullOrWhiteSpace(title.OriginalTitle) ? title.PrimaryTitle ?? string.Empty : title.OriginalTitle!,
                title.Type ?? string.Empty,
                title.StartYear,
                title.EndYear))
            .Where(static title => !string.IsNullOrWhiteSpace(title.Id) && !string.IsNullOrWhiteSpace(title.PrimaryTitle))
            .ToList();
    }

    private async Task<IReadOnlyList<ImdbEpisodeRecord>> FetchEpisodesAsync(
        string seriesId,
        CancellationToken cancellationToken)
    {
        var seasons = await LoadSeasonsAsync(seriesId, cancellationToken);
        var episodes = new List<ImdbEpisodeRecord>();

        foreach (var season in seasons)
        {
            if (string.IsNullOrWhiteSpace(season.Season))
            {
                continue;
            }

            var seenPageTokens = new HashSet<string>(StringComparer.Ordinal);
            var pageCount = 0;
            string? nextPageToken = null;
            do
            {
                var currentPageToken = nextPageToken ?? string.Empty;
                if (!seenPageTokens.Add(currentPageToken))
                {
                    throw new InvalidOperationException("IMDb-Pagination abgebrochen, weil ein Seiten-Token erneut geliefert wurde.");
                }

                if (++pageCount > MaxEpisodePagesPerSeason)
                {
                    throw new InvalidOperationException($"IMDb-Pagination abgebrochen, weil mehr als {MaxEpisodePagesPerSeason} Seiten für eine Staffel angefordert wurden.");
                }

                var requestUri = BuildEpisodesRequestUri(seriesId, season.Season!, nextPageToken);
                using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var payload = await JsonSerializer.DeserializeAsync<ImdbEpisodesResponse>(stream, JsonOptions, cancellationToken)
                              ?? throw new InvalidOperationException("Die IMDb-API lieferte keine lesbaren Episodendaten.");

                episodes.AddRange(payload.Episodes
                    .Select(static episode => new ImdbEpisodeRecord(
                        episode.Id ?? string.Empty,
                        episode.Title ?? string.Empty,
                        episode.Season ?? string.Empty,
                        episode.EpisodeNumber))
                    .Where(static episode => !string.IsNullOrWhiteSpace(episode.Id) && !string.IsNullOrWhiteSpace(episode.Title)));

                nextPageToken = string.IsNullOrWhiteSpace(payload.NextPageToken)
                    ? null
                    : payload.NextPageToken;
                if (nextPageToken is not null && seenPageTokens.Contains(nextPageToken))
                {
                    throw new InvalidOperationException("IMDb-Pagination abgebrochen, weil der Provider erneut auf eine bereits geladene Seite verweist.");
                }
            }
            while (!string.IsNullOrWhiteSpace(nextPageToken));
        }

        return episodes
            .OrderBy(static episode => ParseSortableSeason(episode.Season))
            .ThenBy(static episode => episode.EpisodeNumber ?? int.MaxValue)
            .ThenBy(static episode => episode.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<ImdbSeasonRecord>> LoadSeasonsAsync(
        string seriesId,
        CancellationToken cancellationToken)
    {
        var requestUri = new Uri(ApiBaseAddress, $"titles/{Uri.EscapeDataString(seriesId)}/seasons");
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<ImdbSeasonsResponse>(stream, JsonOptions, cancellationToken)
                      ?? throw new InvalidOperationException("Die IMDb-API lieferte keine lesbaren Staffelinformationen.");

        return payload.Seasons
            .Where(static season => !string.IsNullOrWhiteSpace(season.Season))
            .OrderBy(static season => ParseSortableSeason(season.Season))
            .ToList();
    }

    private static Uri BuildEpisodesRequestUri(string seriesId, string season, string? pageToken)
    {
        var relativePath = $"titles/{Uri.EscapeDataString(seriesId)}/episodes?season={Uri.EscapeDataString(season)}";
        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            relativePath += $"&pageToken={Uri.EscapeDataString(pageToken)}";
        }

        return new Uri(ApiBaseAddress, relativePath);
    }

    private static int ParseSortableSeason(string? season)
    {
        return int.TryParse(season, out var parsed)
            ? parsed
            : int.MaxValue;
    }

    private static async Task<IReadOnlyList<TResult>> ExecuteSharedLookupAsync<TResult>(
        string cacheKey,
        ConcurrentDictionary<string, IReadOnlyList<TResult>> cache,
        ConcurrentDictionary<string, Task<IReadOnlyList<TResult>>> inFlightCache,
        Func<Task<IReadOnlyList<TResult>>> fetchAsync)
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

    private sealed class ImdbSearchTitlesResponse
    {
        public List<ImdbTitleRecord> Titles { get; init; } = [];
    }

    private sealed class ImdbTitleRecord
    {
        public string? Id { get; init; }

        public string? Type { get; init; }

        public string? PrimaryTitle { get; init; }

        public string? OriginalTitle { get; init; }

        public int? StartYear { get; init; }

        public int? EndYear { get; init; }
    }

    private sealed class ImdbSeasonsResponse
    {
        public List<ImdbSeasonRecord> Seasons { get; init; } = [];
    }

    private sealed class ImdbSeasonRecord
    {
        public string? Season { get; init; }
    }

    private sealed class ImdbEpisodesResponse
    {
        public List<ImdbEpisodePayload> Episodes { get; init; } = [];

        public string? NextPageToken { get; init; }
    }

    private sealed class ImdbEpisodePayload
    {
        public string? Id { get; init; }

        public string? Title { get; init; }

        public string? Season { get; init; }

        public int? EpisodeNumber { get; init; }
    }
}
