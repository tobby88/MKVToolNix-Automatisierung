namespace MkvToolnixAutomatisierung.Services.Metadata;

// Dieser Helfer bündelt die eigentlichen TVDB-Matching-Heuristiken, damit der Service Orchestrierung und Persistenz klarer trennt.
internal static class EpisodeMetadataMatchingHeuristics
{
    public static string NormalizeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.ToLowerInvariant();
        normalized = normalized.Replace("&", " und ");
        normalized = new string(normalized.Select(character => char.IsLetterOrDigit(character) ? character : ' ').ToArray());
        normalized = string.Join(" ", normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return normalized.Trim();
    }

    public static TvdbSeriesSearchResult? FindPreferredSeriesResult(
        EpisodeMetadataGuess guess,
        IReadOnlyList<TvdbSeriesSearchResult> seriesResults,
        SeriesMetadataMapping? storedMapping)
    {
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

    public static List<SeriesCandidate> BuildSeriesCandidates(
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

    public static ScoredEpisodeMatch? FindBestEpisodeMatch(
        EpisodeMetadataGuess guess,
        TvdbSeriesSearchResult series,
        IReadOnlyList<TvdbEpisodeRecord> episodes,
        int seriesScore)
    {
        var scoredEpisodes = episodes
            .Select(episode => new
            {
                Episode = episode,
                TitleSimilarity = CalculateTitleSimilarity(guess.EpisodeTitle, episode.Name)
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

    public static bool ShouldRequireReview(ScoredAutomaticMatch match)
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

    public static string BuildStatusText(ScoredAutomaticMatch match, bool requiresReview)
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

    private static string FormatNumber(int? value)
    {
        return value is null or <= 0 ? "xx" : value.Value.ToString("00");
    }
}

internal sealed record SeriesCandidate(TvdbSeriesSearchResult Series, int SeriesScore, bool IsStoredFallback = false);

internal sealed record ScoredEpisodeMatch(
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

internal sealed record ScoredAutomaticMatch(
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
