namespace MkvToolnixAutomatisierung.Services.Metadata;

public sealed record EpisodeMetadataGuess(
    string SeriesName,
    string EpisodeTitle,
    string SeasonNumber,
    string EpisodeNumber);

public sealed record TvdbSeriesSearchResult(
    int Id,
    string Name,
    string? Year,
    string? Overview);

public sealed record TvdbEpisodeRecord(
    int Id,
    string Name,
    int? SeasonNumber,
    int? EpisodeNumber,
    string? Aired);

public sealed record TvdbEpisodeSelection(
    int TvdbSeriesId,
    string TvdbSeriesName,
    int TvdbEpisodeId,
    string EpisodeTitle,
    string SeasonNumber,
    string EpisodeNumber);

public sealed record EpisodeMetadataResolutionResult(
    EpisodeMetadataGuess Guess,
    TvdbEpisodeSelection? Selection,
    string StatusText,
    int ConfidenceScore,
    bool RequiresReview,
    bool QueryWasAttempted,
    bool QuerySucceeded)
{
    public bool HasSelection => Selection is not null;
}
