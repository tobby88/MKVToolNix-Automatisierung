namespace MkvToolnixAutomatisierung.Services.Metadata;

using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

public static class EpisodeMetadataMergeHelper
{
    public static AutoDetectedEpisodeFiles ApplySelection(
        AutoDetectedEpisodeFiles detected,
        TvdbEpisodeSelection selection)
    {
        var directory = Path.GetDirectoryName(detected.SuggestedOutputFilePath)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        var notes = detected.Notes
            .Concat([$"TVDB: {selection.TvdbSeriesName} - S{selection.SeasonNumber}E{selection.EpisodeNumber} - {selection.EpisodeTitle}"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return detected with
        {
            SuggestedTitle = selection.EpisodeTitle,
            SeriesName = selection.TvdbSeriesName,
            SeasonNumber = selection.SeasonNumber,
            EpisodeNumber = selection.EpisodeNumber,
            SuggestedOutputFilePath = BuildSuggestedOutputFilePath(
                directory,
                selection.TvdbSeriesName,
                selection.SeasonNumber,
                selection.EpisodeNumber,
                selection.EpisodeTitle),
            Notes = notes
        };
    }

    public static string BuildSuggestedOutputFilePath(
        string directory,
        string seriesName,
        string seasonNumber,
        string episodeNumber,
        string title)
    {
        var normalizedSeriesName = string.IsNullOrWhiteSpace(seriesName) ? "Unbekannte Serie" : seriesName.Trim();
        var normalizedSeasonNumber = NormalizeEpisodeNumber(seasonNumber);
        var normalizedEpisodeNumber = NormalizeEpisodeNumber(episodeNumber);
        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? "Unbekannter Titel" : title.Trim();

        var fileName = $"{normalizedSeriesName} - S{normalizedSeasonNumber}E{normalizedEpisodeNumber} - {normalizedTitle}.mkv";
        return Path.Combine(directory, SanitizeFileName(fileName));
    }

    public static string NormalizeEpisodeNumber(string? value)
    {
        if (int.TryParse(value, out var number) && number >= 0)
        {
            return number.ToString("00");
        }

        return "xx";
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        return string.Concat(fileName.Select(character => invalidCharacters.Contains(character) ? '_' : character));
    }
}
