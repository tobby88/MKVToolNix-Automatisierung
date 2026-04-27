using System.Globalization;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Baut die importierbaren Metadatenzeilen für neu erzeugte MKV-Dateien einheitlich
/// für Einzel- und Batch-Mux. Dadurch bleibt der spätere Emby-Abgleich unabhängig
/// davon, aus welchem Modul die Datei entstanden ist.
/// </summary>
internal static class BatchOutputMetadataEntryFactory
{
    /// <summary>
    /// Erstellt eine Reportzeile für eine neu erzeugte Ausgabedatei.
    /// </summary>
    /// <param name="outputPath">Vollständiger Pfad zur erzeugten MKV.</param>
    /// <param name="seriesName">Aktuell freigegebener Serienname.</param>
    /// <param name="seasonNumber">Aktuell freigegebene Staffelnummer.</param>
    /// <param name="episodeNumber">Aktuell freigegebene Episodennummer.</param>
    /// <param name="episodeTitle">Aktuell freigegebener Episodentitel.</param>
    /// <param name="tvdbEpisodeId">Optional bekannte TVDB-Episoden-ID.</param>
    /// <param name="tvdbSeriesId">Optional bekannte TVDB-Serien-ID.</param>
    /// <param name="tvdbSeriesName">Optional bekannter TVDB-Serienname.</param>
    /// <returns>Eine vollständige Metadatenzeile für den JSON-Report.</returns>
    public static BatchOutputMetadataEntry Create(
        string outputPath,
        string seriesName,
        string seasonNumber,
        string episodeNumber,
        string episodeTitle,
        int? tvdbEpisodeId,
        int? tvdbSeriesId,
        string? tvdbSeriesName)
    {
        var tvdbEpisodeIdText = tvdbEpisodeId?.ToString(CultureInfo.InvariantCulture);
        return new BatchOutputMetadataEntry
        {
            OutputPath = outputPath,
            NfoPath = Path.ChangeExtension(outputPath, ".nfo"),
            SeriesName = seriesName,
            SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber,
            EpisodeTitle = episodeTitle,
            TvdbEpisodeId = tvdbEpisodeIdText,
            ProviderIds = string.IsNullOrWhiteSpace(tvdbEpisodeIdText)
                ? null
                : new BatchOutputProviderIds
                {
                    Tvdb = tvdbEpisodeIdText
                },
            Tvdb = tvdbEpisodeId is null && tvdbSeriesId is null && string.IsNullOrWhiteSpace(tvdbSeriesName)
                ? null
                : new BatchOutputTvdbMetadata
                {
                    SeriesId = tvdbSeriesId,
                    SeriesName = tvdbSeriesName,
                    EpisodeId = tvdbEpisodeId
                }
        };
    }
}
