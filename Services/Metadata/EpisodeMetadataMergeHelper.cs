namespace MkvToolnixAutomatisierung.Services.Metadata;

using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

/// <summary>
/// Vereinheitlicht, wie TVDB-Daten oder lokale Fallbacks in erkannte Episodenobjekte zurückgemischt werden.
/// </summary>
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
        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? "Unbekannter Titel" : title.Trim();

        return Path.Combine(
            directory,
            EpisodeFileNameHelper.BuildEpisodeFileName(
                normalizedSeriesName,
                seasonNumber,
                episodeNumber,
                normalizedTitle));
    }

    public static string NormalizeEpisodeNumber(string? value)
    {
        return EpisodeFileNameHelper.NormalizeEpisodeNumber(value);
    }
}
