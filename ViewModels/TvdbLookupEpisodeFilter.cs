using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels;

/// <summary>
/// Bündelt die Filterheuristiken für die TVDB-Episodenliste.
/// </summary>
internal static class TvdbLookupEpisodeFilter
{
    public static IReadOnlyList<TvdbEpisodeRecord> FilterEpisodes(
        IReadOnlyList<TvdbEpisodeRecord> episodes,
        string searchText)
    {
        var trimmedSearchText = searchText.Trim();
        if (string.IsNullOrWhiteSpace(trimmedSearchText))
        {
            return episodes.ToList();
        }

        var normalizedSearchText = NormalizeTextForSearch(trimmedSearchText);
        return episodes
            .Where(episode => EpisodeMatchesSearch(episode, trimmedSearchText, normalizedSearchText))
            .ToList();
    }

    private static bool EpisodeMatchesSearch(TvdbEpisodeRecord episode, string rawSearchText, string normalizedSearchText)
    {
        if (episode.Name.Contains(rawSearchText, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(normalizedSearchText))
        {
            return false;
        }

        // Zusätzliche Tokens decken typische Suchmuster wie S01E02 oder 01x02 ab, ohne die eigentliche Titelsuche zu verdrängen.
        return BuildEpisodeSearchTokens(episode).Any(token =>
            NormalizeTextForSearch(token).Contains(normalizedSearchText, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> BuildEpisodeSearchTokens(TvdbEpisodeRecord episode)
    {
        var seasonNumber = TvdbLookupWindowTextFormatter.FormatTvdbNumber(episode.SeasonNumber);
        var episodeNumber = TvdbLookupWindowTextFormatter.FormatTvdbNumber(episode.EpisodeNumber);

        yield return $"s{seasonNumber}e{episodeNumber}";
        yield return $"{seasonNumber}x{episodeNumber}";
        yield return $"staffel {seasonNumber} folge {episodeNumber}";
    }

    private static string NormalizeTextForSearch(string value)
    {
        // Vereinheitlicht Eingaben wie "S01-E02" oder "Staffel 1, Folge 2" auf einen robust vergleichbaren Kern.
        return string.Concat(value
            .Where(character => char.IsLetterOrDigit(character))
            .Select(char.ToLowerInvariant));
    }
}
