namespace MkvToolnixAutomatisierung.Services.Metadata;

public sealed class EpisodeMetadataLookupService
{
    private readonly AppMetadataStore _store;
    private readonly TvdbClient _tvdbClient;

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
        var normalized = NormalizeSeriesName(localSeriesName);
        return LoadSettings().SeriesMappings.FirstOrDefault(mapping =>
            string.Equals(NormalizeSeriesName(mapping.LocalSeriesName), normalized, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<TvdbSeriesSearchResult>> SearchSeriesAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var settings = LoadSettings();
        EnsureApiKeyConfigured(settings);
        return await _tvdbClient.SearchSeriesAsync(settings.TvdbApiKey, settings.TvdbPin, query, cancellationToken);
    }

    public async Task<IReadOnlyList<TvdbEpisodeRecord>> LoadEpisodesAsync(
        int seriesId,
        CancellationToken cancellationToken = default)
    {
        var settings = LoadSettings();
        EnsureApiKeyConfigured(settings);
        return await _tvdbClient.GetSeriesEpisodesAsync(settings.TvdbApiKey, settings.TvdbPin, seriesId, cancellationToken);
    }

    public async Task<TvdbEpisodeSelection?> ResolveWithStoredMappingAsync(
        EpisodeMetadataGuess guess,
        CancellationToken cancellationToken = default)
    {
        var mapping = FindSeriesMapping(guess.SeriesName);
        if (mapping is null)
        {
            return null;
        }

        var episodes = await LoadEpisodesAsync(mapping.TvdbSeriesId, cancellationToken);
        return FindBestEpisodeMatch(
            guess,
            new TvdbSeriesSearchResult(mapping.TvdbSeriesId, mapping.TvdbSeriesName, null, null),
            episodes);
    }

    public TvdbEpisodeSelection? FindBestEpisodeMatch(
        EpisodeMetadataGuess guess,
        TvdbSeriesSearchResult series,
        IReadOnlyList<TvdbEpisodeRecord> episodes)
    {
        var bestEpisode = episodes
            .Select(episode => new
            {
                Episode = episode,
                Score = CalculateEpisodeScore(guess, episode)
            })
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Episode.SeasonNumber ?? int.MaxValue)
            .ThenBy(entry => entry.Episode.EpisodeNumber ?? int.MaxValue)
            .FirstOrDefault();

        if (bestEpisode is null || bestEpisode.Score < 35)
        {
            return null;
        }

        return new TvdbEpisodeSelection(
            series.Id,
            series.Name,
            bestEpisode.Episode.Id,
            bestEpisode.Episode.Name,
            FormatNumber(bestEpisode.Episode.SeasonNumber),
            FormatNumber(bestEpisode.Episode.EpisodeNumber));
    }

    public void SaveSeriesMapping(string localSeriesName, TvdbSeriesSearchResult series)
    {
        var settings = LoadSettings();
        var normalized = NormalizeSeriesName(localSeriesName);
        var existing = settings.SeriesMappings.FirstOrDefault(mapping =>
            string.Equals(NormalizeSeriesName(mapping.LocalSeriesName), normalized, StringComparison.OrdinalIgnoreCase));

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

    private static void EnsureApiKeyConfigured(AppMetadataSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.TvdbApiKey))
        {
            throw new InvalidOperationException(
                "Es ist noch kein TVDB-API-Key gespeichert. Bitte im TVDB-Dialog zuerst den API-Key eintragen.");
        }
    }

    private static int CalculateEpisodeScore(EpisodeMetadataGuess guess, TvdbEpisodeRecord episode)
    {
        var score = 0;

        if (int.TryParse(guess.SeasonNumber, out var seasonNumber) && episode.SeasonNumber == seasonNumber)
        {
            score += 40;
        }

        if (int.TryParse(guess.EpisodeNumber, out var episodeNumber) && episode.EpisodeNumber == episodeNumber)
        {
            score += 40;
        }

        score += CalculateTitleSimilarity(guess.EpisodeTitle, episode.Name);
        return score;
    }

    private static int CalculateTitleSimilarity(string left, string right)
    {
        var normalizedLeft = NormalizeSeriesName(left);
        var normalizedRight = NormalizeSeriesName(right);

        if (string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase))
        {
            return 40;
        }

        if (normalizedLeft.Contains(normalizedRight, StringComparison.OrdinalIgnoreCase)
            || normalizedRight.Contains(normalizedLeft, StringComparison.OrdinalIgnoreCase))
        {
            return 30;
        }

        var leftTokens = normalizedLeft.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rightTokens = normalizedRight.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (leftTokens.Length == 0 || rightTokens.Length == 0)
        {
            return 0;
        }

        var intersection = leftTokens.Intersect(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        var union = leftTokens.Union(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        return union == 0 ? 0 : (int)Math.Round(intersection * 40d / union);
    }

    private static string NormalizeSeriesName(string value)
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
}
