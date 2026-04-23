namespace MkvToolnixAutomatisierung.Services.Metadata;

/// <summary>
/// Minimaler Serienkandidat aus der freien IMDb-API.
/// </summary>
internal sealed record ImdbSeriesSearchResult(
    string Id,
    string PrimaryTitle,
    string OriginalTitle,
    string Type,
    int? StartYear,
    int? EndYear);

/// <summary>
/// Minimaler Episodenkandidat aus der freien IMDb-API.
/// </summary>
internal sealed record ImdbEpisodeRecord(
    string Id,
    string Title,
    string Season,
    int? EpisodeNumber);
