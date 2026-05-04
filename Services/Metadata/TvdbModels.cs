namespace MkvToolnixAutomatisierung.Services.Metadata;

/// <summary>
/// Lokale Schätzung einer Episode vor dem TVDB-Abgleich.
/// </summary>
/// <param name="SeriesName">Lokal erkannter Serienname.</param>
/// <param name="EpisodeTitle">Lokal erkannter Episodentitel.</param>
/// <param name="SeasonNumber">Lokal erkannte Staffelnummer oder <c>xx</c>.</param>
/// <param name="EpisodeNumber">Lokal erkannte Episodennummer oder <c>xx</c>.</param>
/// <param name="SourceFileName">
/// Optionaler ursprünglicher Dateiname der ausgewählten Haupt-/Seed-Quelle. Der Dialog zeigt ihn
/// nur zur Orientierung; er darf das automatische Matching nicht beeinflussen.
/// </param>
public sealed record EpisodeMetadataGuess(
    string SeriesName,
    string EpisodeTitle,
    string SeasonNumber,
    string EpisodeNumber,
    string? SourceFileName = null);

/// <summary>
/// Minimales TVDB-Suchergebnis, das für Serienauswahl und Mapping ausreicht.
/// </summary>
public sealed record TvdbSeriesSearchResult(
    int Id,
    string Name,
    string? Year,
    string? Overview,
    string? PrimaryLanguage = null);

/// <summary>
/// Minimale TVDB-Episodenrepräsentation für die automatische Zuordnung.
/// </summary>
public sealed record TvdbEpisodeRecord(
    int Id,
    string Name,
    int? SeasonNumber,
    int? EpisodeNumber,
    string? Aired);

/// <summary>
/// Final ausgewählte TVDB-Zuordnung, die in ViewModels und Planner zurückgeschrieben werden kann.
/// </summary>
public sealed record TvdbEpisodeSelection(
    int TvdbSeriesId,
    string TvdbSeriesName,
    int TvdbEpisodeId,
    string EpisodeTitle,
    string SeasonNumber,
    string EpisodeNumber,
    string? OriginalLanguage = null);

/// <summary>
/// Ergebnis der automatischen Metadatenauflösung inklusive Vertrauens- und Review-Signalen.
/// </summary>
public sealed record EpisodeMetadataResolutionResult(
    EpisodeMetadataGuess Guess,
    TvdbEpisodeSelection? Selection,
    string StatusText,
    int ConfidenceScore,
    bool RequiresReview,
    bool QueryWasAttempted,
    bool QuerySucceeded)
{
    /// <summary>
    /// Kennzeichnet, ob aus der Auflösung bereits eine konkrete TVDB-Zuordnung hervorgegangen ist.
    /// </summary>
    public bool HasSelection => Selection is not null;
}
