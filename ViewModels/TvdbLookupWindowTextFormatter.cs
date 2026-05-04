using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.ViewModels;

/// <summary>
/// Erzeugt alle textuellen Darstellungen des TVDB-Dialogs aus fachlichen Modellen.
/// </summary>
internal static class TvdbLookupWindowTextFormatter
{
    public static string BuildGuessSummaryText(EpisodeMetadataGuess guess)
    {
        var summary = $"Lokal erkannt: {guess.SeriesName} - {EpisodeFileNameHelper.BuildEpisodeCode(guess.SeasonNumber, guess.EpisodeNumber)} - {guess.EpisodeTitle}";
        return string.IsNullOrWhiteSpace(guess.SourceFileName)
            ? summary
            : summary + $"{Environment.NewLine}Quelle: {guess.SourceFileName}";
    }

    public static string BuildComparisonSummaryText(
        EpisodeMetadataGuess guess,
        TvdbSeriesSearchResult? selectedSeries,
        TvdbEpisodeRecord? selectedEpisode)
    {
        if (selectedSeries is null)
        {
            return "Noch keine TVDB-Serie ausgewählt.";
        }

        if (selectedEpisode is null)
        {
            return "Noch keine TVDB-Episode ausgewählt.";
        }

        var selectedSeason = FormatTvdbNumber(selectedEpisode.SeasonNumber);
        var selectedEpisodeNumber = FormatTvdbNumber(selectedEpisode.EpisodeNumber);
        var differences = new List<string>();

        if (!string.Equals(guess.SeriesName.Trim(), selectedSeries.Name.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            differences.Add($"Serie: lokal '{guess.SeriesName}' -> TVDB '{selectedSeries.Name}'");
        }

        if (!string.Equals(EpisodeFileNameHelper.NormalizeSeasonNumber(guess.SeasonNumber), selectedSeason, StringComparison.OrdinalIgnoreCase))
        {
            differences.Add($"Staffel: lokal '{EpisodeFileNameHelper.NormalizeSeasonNumber(guess.SeasonNumber)}' -> TVDB '{selectedSeason}'");
        }

        if (!string.Equals(EpisodeFileNameHelper.NormalizeEpisodeNumber(guess.EpisodeNumber), selectedEpisodeNumber, StringComparison.OrdinalIgnoreCase))
        {
            differences.Add($"Folge: lokal '{EpisodeFileNameHelper.NormalizeEpisodeNumber(guess.EpisodeNumber)}' -> TVDB '{selectedEpisodeNumber}'");
        }

        if (!string.Equals(guess.EpisodeTitle.Trim(), selectedEpisode.Name.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            differences.Add($"Titel: lokal '{guess.EpisodeTitle}' -> TVDB '{selectedEpisode.Name}'");
        }

        return differences.Count == 0
            ? "TVDB stimmt mit der lokalen Erkennung überein."
            : "Abweichungen: " + string.Join(" | ", differences);
    }

    public static string FormatSeriesDisplayText(TvdbSeriesSearchResult series)
    {
        return string.IsNullOrWhiteSpace(series.Year)
            ? $"{series.Name} (ID {series.Id})"
            : $"{series.Name} ({series.Year}) - ID {series.Id}";
    }

    public static string FormatEpisodeDisplayText(TvdbEpisodeRecord episode)
    {
        return $"S{FormatTvdbNumber(episode.SeasonNumber)}E{FormatTvdbNumber(episode.EpisodeNumber)} - {episode.Name}";
    }

    public static string FormatTvdbNumber(int? value)
    {
        return value is null or < 0 ? "xx" : value.Value.ToString("00");
    }
}
