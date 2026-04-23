using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Services.Metadata;

/// <summary>
/// Ergänzt die TVDB-Automatik um einen konservativen Archiv-Fallback für bereits bekannte
/// Sonderfolgen, Trailer und Bonusclips ohne Provider-Eintrag.
/// </summary>
internal static class ArchiveSpecialMetadataFallback
{
    /// <summary>
    /// Übernimmt Zielpfad und sichtbare Metadaten aus einer vorhandenen Sondermaterial-MKV,
    /// wenn TVDB keine konkrete Episode geliefert hat.
    /// </summary>
    public static (AutoDetectedEpisodeFiles Detected, EpisodeMetadataResolutionResult Resolution) ApplyIfAvailable(
        AutoDetectedEpisodeFiles detected,
        EpisodeMetadataResolutionResult resolution,
        EpisodeOutputPathService outputPaths,
        EpisodeMetadataLookupService metadataLookup,
        string? outputRootOverride)
    {
        if (resolution.Selection is not null)
        {
            return (detected, resolution);
        }

        var mapping = metadataLookup.FindSeriesMapping(detected.SeriesName);
        var match = outputPaths.TryResolveExistingSpecialArchiveMatch(
            outputRootOverride,
            BuildSeriesNameCandidates(detected.SeriesName, mapping),
            detected.SuggestedTitle,
            mapping?.OriginalLanguage);
        if (match is null)
        {
            return (detected, resolution);
        }

        var archiveCode = EpisodeFileNameHelper.BuildEpisodeCode(match.SeasonNumber, match.EpisodeNumber);
        var notes = detected.Notes
            .Concat([$"Archiv-Sondermaterial erkannt: {Path.GetFileName(match.OutputPath)}. Metadaten wurden aus der vorhandenen Bibliotheksdatei übernommen."])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var updatedDetected = detected with
        {
            SuggestedOutputFilePath = match.OutputPath,
            SuggestedTitle = match.Title,
            SeriesName = match.SeriesName,
            SeasonNumber = match.SeasonNumber,
            EpisodeNumber = match.EpisodeNumber,
            Notes = notes,
            OriginalLanguage = match.OriginalLanguage
        };
        var updatedResolution = new EpisodeMetadataResolutionResult(
            resolution.Guess,
            Selection: null,
            StatusText: $"Archiv-Sondermaterial automatisch erkannt: {archiveCode} - {match.Title}",
            ConfidenceScore: 100,
            RequiresReview: false,
            QueryWasAttempted: true,
            QuerySucceeded: true);

        return (updatedDetected, updatedResolution);
    }

    private static IReadOnlyList<string> BuildSeriesNameCandidates(
        string localSeriesName,
        SeriesMetadataMapping? mapping)
    {
        return new[]
            {
                localSeriesName,
                mapping?.LocalSeriesName,
                mapping?.TvdbSeriesName
            }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
