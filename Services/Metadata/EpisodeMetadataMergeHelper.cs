namespace MkvToolnixAutomatisierung.Services.Metadata;

using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

/// <summary>
/// Vereinheitlicht, wie TVDB-Daten oder lokale Fallbacks in erkannte Episodenobjekte zurückgemischt werden.
/// </summary>
public static class EpisodeMetadataMergeHelper
{
    /// <summary>
    /// Wendet eine bestätigte TVDB-Auswahl auf ein lokal erkanntes Episodenobjekt an.
    /// </summary>
    /// <param name="detected">Bisher lokal erkannte Episodendaten.</param>
    /// <param name="selection">Bestätigte TVDB-Zuordnung.</param>
    /// <returns>Neues Erkennungsobjekt mit TVDB-normalisierten Metadaten und Hinweisen.</returns>
    public static AutoDetectedEpisodeFiles ApplySelection(
        AutoDetectedEpisodeFiles detected,
        TvdbEpisodeSelection selection)
    {
        var directory = Path.GetDirectoryName(detected.SuggestedOutputFilePath)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        var notes = detected.Notes
            .Concat([$"TVDB: {selection.TvdbSeriesName} - {EpisodeFileNameHelper.BuildEpisodeCode(selection.SeasonNumber, selection.EpisodeNumber)} - {selection.EpisodeTitle}"])
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

    /// <summary>
    /// Baut aus Metadaten einen vorgeschlagenen MKV-Dateipfad für den aktuellen Zielordner.
    /// </summary>
    /// <param name="directory">Zielordner der Ausgabedatei.</param>
    /// <param name="seriesName">Serienname der Episode.</param>
    /// <param name="seasonNumber">Staffelnummer oder Jahresstaffel.</param>
    /// <param name="episodeNumber">Normalisierte Episodennummer.</param>
    /// <param name="title">Episodentitel.</param>
    /// <returns>Vollständiger vorgeschlagener Ausgabepfad.</returns>
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

    /// <summary>
    /// Normalisiert eine lokale Episodennummer auf das projektweit verwendete zweistellige Format.
    /// </summary>
    /// <param name="value">Rohwert aus Dateiname, TVDB oder Benutzereingabe.</param>
    /// <returns>Normalisierte Episodennummer oder <c>xx</c> für unbekannte Werte.</returns>
    public static string NormalizeEpisodeNumber(string? value)
    {
        return EpisodeFileNameHelper.NormalizeEpisodeNumber(value);
    }

    /// <summary>
    /// Normalisiert eine lokale Staffelnummer auf das projektweit verwendete Format.
    /// </summary>
    /// <param name="value">Rohwert aus Dateiname, TVDB oder Benutzereingabe.</param>
    /// <returns>Normalisierte Staffelnummer oder <c>xx</c> für unbekannte Werte.</returns>
    public static string NormalizeSeasonNumber(string? value)
    {
        return EpisodeFileNameHelper.NormalizeSeasonNumber(value);
    }
}
