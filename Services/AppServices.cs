using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Services;

public sealed record AppServices(
    SeriesEpisodeMuxService SeriesEpisodeMux,
    EpisodeMetadataLookupService EpisodeMetadata);
