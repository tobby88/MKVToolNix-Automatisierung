namespace MkvToolnixAutomatisierung.Services.Metadata;

public sealed class EpisodeMetadataLookupService
{
    private readonly AppMetadataStore _store;
    private readonly TvdbClient _tvdbClient;
    private readonly Dictionary<TvdbSeriesSearchCacheKey, IReadOnlyList<TvdbSeriesSearchResult>> _seriesSearchCache = new();
    private readonly Dictionary<TvdbEpisodeCacheKey, IReadOnlyList<TvdbEpisodeRecord>> _episodeCache = new();

    public EpisodeMetadataLookupService(AppMetadataStore store, TvdbClient tvdbClient)
    {
        _store = store;
        _tvdbClient = tvdbClient;
    }

    public AppMetadataSettings LoadSettings()
    {
        return _store.Load();
    }

    public void SaveSettings(AppMetadataSettings settings)
    {
        _store.Save(settings);
    }

    public SeriesMetadataMapping? FindSeriesMapping(string localSeriesName)
    {
        var normalized = NormalizeText(localSeriesName);
        return LoadSettings().SeriesMappings.FirstOrDefault(mapping =>
            string.Equals(NormalizeText(mapping.LocalSeriesName), normalized, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<TvdbSeriesSearchResult>> SearchSeriesAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        return await SearchSeriesAsync(query, LoadSettings(), cancellationToken);
    }

    public async Task<IReadOnlyList<TvdbSeriesSearchResult>> SearchSeriesAsync(
        string query,
        AppMetadataSettings settings,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = NormalizeText(query);
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

        var results = await _tvdbClient.SearchSeriesAsync(settings.TvdbApiKey, settings.TvdbPin, query, cancellationToken);
        _seriesSearchCache[cacheKey] = results;
        return results;
    }

    public async Task<IReadOnlyList<TvdbEpisodeRecord>> LoadEpisodesAsync(
        int seriesId,
        CancellationToken cancellationToken = default)
    {
        return await LoadEpisodesAsync(seriesId, LoadSettings(), cancellationToken);
    }

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

        var episodes = await _tvdbClient.GetSeriesEpisodesAsync(settings.TvdbApiKey, settings.TvdbPin, seriesId, cancellationToken);
        _episodeCache[cacheKey] = episodes;
        return episodes;
    }

    public TvdbSeriesSearchResult? FindPreferredSeriesResult(
        EpisodeMetadataGuess guess,
        IReadOnlyList<TvdbSeriesSearchResult> seriesResults)
    {
        var storedMapping = FindSeriesMapping(guess.SeriesName);

        return seriesResults
            .Select(result => new
            {
                Result = result,
                Score = CalculateSeriesScore(guess.SeriesName, result, storedMapping)
            })
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Result.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.Result)
            .FirstOrDefault();
    }

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

            var requiresReview = ShouldRequireReview(bestMatch);
            var statusText = BuildStatusText(bestMatch, requiresReview);

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
        catch (Exception ex)
        {
            return new EpisodeMetadataResolutionResult(
                guess,
                Selection: null,
                StatusText: $"TVDB-Automatik fehlgeschlagen: {ex.Message}",
                ConfidenceScore: 0,
                RequiresReview: false,
                QueryWasAttempted: true,
                QuerySucceeded: false);
        }
    }

    public TvdbEpisodeSelection? FindBestEpisodeMatch(
        EpisodeMetadataGuess guess,
        TvdbSeriesSearchResult series,
        IReadOnlyList<TvdbEpisodeRecord> episodes)
    {
        return FindBestEpisodeMatchCore(guess, series, episodes, seriesScore: 0)?.Selection;
    }

    public void SaveSeriesMapping(string localSeriesName, TvdbSeriesSearchResult series)
    {
        var settings = LoadSettings();
        var normalized = NormalizeText(localSeriesName);
        var existing = settings.SeriesMappings.FirstOrDefault(mapping =>
            string.Equals(NormalizeText(mapping.LocalSeriesName), normalized, StringComparison.OrdinalIgnoreCase));

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
            var match = FindBestEpisodeMatchCore(guess, candidate.Series, episodes, candidate.SeriesScore);
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
                    candidate.IsStoredFallback)
                {
                    SelectionMatch = match
                };
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
        var candidates = searchResults
            .Select(result => new SeriesCandidate(
                result,
                CalculateSeriesScore(guess.SeriesName, result, storedMapping)))
            .OrderByDescending(candidate => candidate.SeriesScore)
            .ThenBy(candidate => candidate.Series.Name, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        if (storedMapping is not null && candidates.All(candidate => candidate.Series.Id != storedMapping.TvdbSeriesId))
        {
            candidates.Add(new SeriesCandidate(
                new TvdbSeriesSearchResult(storedMapping.TvdbSeriesId, storedMapping.TvdbSeriesName, null, null),
                5,
                IsStoredFallback: true));
        }

        return candidates
            .OrderByDescending(candidate => candidate.SeriesScore)
            .ThenBy(candidate => candidate.Series.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ScoredEpisodeMatch? FindBestEpisodeMatchCore(
        EpisodeMetadataGuess guess,
        TvdbSeriesSearchResult series,
        IReadOnlyList<TvdbEpisodeRecord> episodes,
        int seriesScore)
    {
        var scoredEpisodes = episodes
            .Select(episode => new
            {
                Episode = episode,
                TitleSimilarity = CalculateTitleSimilarity(guess.EpisodeTitle, episode.Name),
                EpisodeScore = 0
            })
            .Select(entry => new
            {
                entry.Episode,
                entry.TitleSimilarity,
                EpisodeScore = CalculateEpisodeScore(guess, entry.Episode, entry.TitleSimilarity)
            })
            .ToList();

        if (scoredEpisodes.Count == 0)
        {
            return null;
        }

        var bestTitleSimilarity = scoredEpisodes.Max(entry => entry.TitleSimilarity);
        var bestTitleSimilarityCount = scoredEpisodes.Count(entry => entry.TitleSimilarity == bestTitleSimilarity);
        var exactTitleMatchCount = scoredEpisodes.Count(entry => entry.TitleSimilarity >= 30);
        var strongTitleMatchCount = scoredEpisodes.Count(entry => entry.TitleSimilarity >= 22);
        var prioritizedEpisodes = scoredEpisodes.AsEnumerable();

        if (bestTitleSimilarity >= 30)
        {
            // Exakter Titeltreffer soll lokale, oft unzuverlaessige Staffelangaben aus dem Dateinamen ueberstimmen.
            prioritizedEpisodes = scoredEpisodes.Where(entry => entry.TitleSimilarity == bestTitleSimilarity);
        }
        else if (bestTitleSimilarity >= 22)
        {
            // Bei stark aehnlichem Titel werden nur die titelnaechsten Treffer weiter betrachtet.
            prioritizedEpisodes = scoredEpisodes.Where(entry => entry.TitleSimilarity >= bestTitleSimilarity - 2);
        }

        var bestEpisode = prioritizedEpisodes
            .OrderByDescending(entry => entry.EpisodeScore)
            .ThenBy(entry => entry.Episode.SeasonNumber ?? int.MaxValue)
            .ThenBy(entry => entry.Episode.EpisodeNumber ?? int.MaxValue)
            .FirstOrDefault();

        if (bestEpisode is null)
        {
            return null;
        }

        var combinedScore = bestEpisode.EpisodeScore + Math.Min(seriesScore, 25);
        if (combinedScore < 45)
        {
            return null;
        }

        return new ScoredEpisodeMatch(
            series,
            new TvdbEpisodeSelection(
                series.Id,
                series.Name,
                bestEpisode.Episode.Id,
                bestEpisode.Episode.Name,
                FormatNumber(bestEpisode.Episode.SeasonNumber),
                FormatNumber(bestEpisode.Episode.EpisodeNumber)),
            bestEpisode.EpisodeScore,
            combinedScore,
            bestEpisode.TitleSimilarity,
            bestTitleSimilarityCount,
            exactTitleMatchCount,
            strongTitleMatchCount,
            SeasonMatched: int.TryParse(guess.SeasonNumber, out var seasonNumber) && bestEpisode.Episode.SeasonNumber == seasonNumber,
            EpisodeMatched: int.TryParse(guess.EpisodeNumber, out var episodeNumber) && bestEpisode.Episode.EpisodeNumber == episodeNumber);
    }

    private static bool ShouldRequireReview(ScoredAutomaticMatch match)
    {
        if (match.UsedStoredFallback)
        {
            return true;
        }

        if (match.TitleSimilarity >= 30 && match.ExactTitleMatchCount == 1)
        {
            return false;
        }

        if (match.TitleSimilarity >= 30)
        {
            return !match.EpisodeMatched && match.ScoreGap < 4;
        }

        if (match.TitleSimilarity >= 22 && match.StrongTitleMatchCount == 1)
        {
            return !(match.SeasonMatched && match.EpisodeMatched) && match.ScoreGap < 6;
        }

        if (match.TitleSimilarity >= 22 && match.SeasonMatched && match.EpisodeMatched)
        {
            return match.ScoreGap < 4 && match.CombinedScore < 80;
        }

        return match.CombinedScore < 90 || match.ScoreGap < 8;
    }

    private static string BuildStatusText(ScoredAutomaticMatch match, bool requiresReview)
    {
        var parts = new List<string>
        {
            requiresReview
                ? $"TVDB-Vorschlag prüfen: S{match.Selection.SeasonNumber}E{match.Selection.EpisodeNumber} - {match.Selection.EpisodeTitle}"
                : $"TVDB automatisch erkannt: S{match.Selection.SeasonNumber}E{match.Selection.EpisodeNumber} - {match.Selection.EpisodeTitle}"
        };

        if (match.TitleSimilarity >= 30)
        {
            parts.Add("Exakter Titeltreffer.");
        }
        else if (match.TitleSimilarity >= 22)
        {
            parts.Add("Starker Titeltreffer.");
        }

        if (!match.SeasonMatched || !match.EpisodeMatched)
        {
            var differences = new List<string>();
            if (!match.SeasonMatched)
            {
                differences.Add("Staffel weicht von der lokalen Erkennung ab");
            }

            if (!match.EpisodeMatched)
            {
                differences.Add("Folge weicht von der lokalen Erkennung ab");
            }

            parts.Add(string.Join(", ", differences) + ".");
        }

        if (match.ExactTitleMatchCount > 1)
        {
            parts.Add("Mehrere exakte Titeltreffer gefunden.");
        }
        else if (match.StrongTitleMatchCount > 1)
        {
            parts.Add("Mehrere aehnliche Titeltreffer gefunden.");
        }

        return string.Join(" ", parts);
    }

    private static void EnsureApiKeyConfigured(AppMetadataSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.TvdbApiKey))
        {
            throw new InvalidOperationException(
                "Es ist noch kein TVDB-API-Key gespeichert. Bitte im TVDB-Dialog zuerst den API-Key eintragen.");
        }
    }

    private static int CalculateSeriesScore(
        string seriesQuery,
        TvdbSeriesSearchResult series,
        SeriesMetadataMapping? storedMapping)
    {
        var normalizedQuery = NormalizeText(seriesQuery);
        var normalizedSeriesName = NormalizeText(series.Name);
        var score = 0;

        if (string.Equals(normalizedQuery, normalizedSeriesName, StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }
        else if (normalizedSeriesName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
            || normalizedQuery.Contains(normalizedSeriesName, StringComparison.OrdinalIgnoreCase))
        {
            score += 35;
        }
        else
        {
            score += CalculateTokenSimilarity(normalizedQuery, normalizedSeriesName, 28);
        }

        if (storedMapping is not null && storedMapping.TvdbSeriesId == series.Id)
        {
            score += 6;
        }

        return score;
    }

    private static int CalculateEpisodeScore(EpisodeMetadataGuess guess, TvdbEpisodeRecord episode, int titleSimilarity)
    {
        var score = 0;

        if (int.TryParse(guess.SeasonNumber, out var seasonNumber))
        {
            score += episode.SeasonNumber == seasonNumber
                ? 35
                : titleSimilarity >= 30 ? 0
                : titleSimilarity >= 22 ? -6
                : -18;
        }

        if (int.TryParse(guess.EpisodeNumber, out var episodeNumber))
        {
            score += episode.EpisodeNumber == episodeNumber
                ? 35
                : titleSimilarity >= 30 ? 0
                : titleSimilarity >= 22 ? -6
                : -18;
        }

        score += titleSimilarity;
        return score;
    }

    private static int CalculateTitleSimilarity(string left, string right)
    {
        var normalizedLeft = NormalizeText(left);
        var normalizedRight = NormalizeText(right);

        if (string.IsNullOrWhiteSpace(normalizedLeft) || string.IsNullOrWhiteSpace(normalizedRight))
        {
            return 0;
        }

        if (string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase))
        {
            return 30;
        }

        if (normalizedLeft.Contains(normalizedRight, StringComparison.OrdinalIgnoreCase)
            || normalizedRight.Contains(normalizedLeft, StringComparison.OrdinalIgnoreCase))
        {
            return 22;
        }

        return CalculateTokenSimilarity(normalizedLeft, normalizedRight, 18);
    }

    private static int CalculateTokenSimilarity(string left, string right, int maxScore)
    {
        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (leftTokens.Length == 0 || rightTokens.Length == 0)
        {
            return 0;
        }

        var intersection = leftTokens.Intersect(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        var union = leftTokens.Union(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        return union == 0 ? 0 : (int)Math.Round(intersection * maxScore / (double)union);
    }

    private static string NormalizeText(string value)
    {
        var normalized = value.ToLowerInvariant();
        normalized = normalized.Replace("&", " und ");
        normalized = new string(normalized.Select(character => char.IsLetterOrDigit(character) ? character : ' ').ToArray());
        normalized = string.Join(" ", normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return normalized.Trim();
    }

    private static string FormatNumber(int? value)
    {
        return value is null or <= 0 ? "xx" : value.Value.ToString("00");
    }

    private sealed record SeriesCandidate(TvdbSeriesSearchResult Series, int SeriesScore, bool IsStoredFallback = false);

    private sealed record ScoredEpisodeMatch(
        TvdbSeriesSearchResult Series,
        TvdbEpisodeSelection Selection,
        int EpisodeScore,
        int CombinedScore,
        int TitleSimilarity,
        int BestTitleSimilarityCount,
        int ExactTitleMatchCount,
        int StrongTitleMatchCount,
        bool SeasonMatched,
        bool EpisodeMatched);

    private sealed record ScoredAutomaticMatch(
        TvdbSeriesSearchResult Series,
        TvdbEpisodeSelection Selection,
        int CombinedScore,
        int ScoreGap,
        bool UsedStoredFallback)
    {
        public int TitleSimilarity => SelectionMatch.TitleSimilarity;
        public int BestTitleSimilarityCount => SelectionMatch.BestTitleSimilarityCount;
        public int ExactTitleMatchCount => SelectionMatch.ExactTitleMatchCount;
        public int StrongTitleMatchCount => SelectionMatch.StrongTitleMatchCount;
        public bool SeasonMatched => SelectionMatch.SeasonMatched;
        public bool EpisodeMatched => SelectionMatch.EpisodeMatched;

        public ScoredEpisodeMatch SelectionMatch { get; init; } = null!;
    }
}

internal readonly record struct TvdbSeriesSearchCacheKey(string ApiKey, string Pin, string Query);

internal readonly record struct TvdbEpisodeCacheKey(string ApiKey, string Pin, int SeriesId);
