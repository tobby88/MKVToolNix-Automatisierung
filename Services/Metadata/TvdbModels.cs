namespace MkvToolnixAutomatisierung.Services.Metadata;

/// <summary>
/// Lokale Schätzung einer Episode vor dem TVDB-Abgleich.
/// </summary>
public sealed record EpisodeMetadataGuess(
    string SeriesName,
    string EpisodeTitle,
    string SeasonNumber,
    string EpisodeNumber);

/// <summary>
/// Minimales TVDB-Suchergebnis, das für Serienauswahl und Mapping ausreicht.
/// </summary>
public sealed record TvdbSeriesSearchResult(
    int Id,
    string Name,
    string? Year,
    string? Overview);

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
    string EpisodeNumber);

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
