using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Unveränderliche Kopie der planrelevanten UI-Eingaben für einen einzelnen Mux-Plan.
/// </summary>
/// <remarks>
/// Planberechnung und Cache-Key-Erzeugung laufen teilweise asynchron. Der Snapshot verhindert,
/// dass Änderungen am ViewModel währenddessen zu gemischten oder nachträglich falsch gecachten
/// Planständen führen.
/// </remarks>
internal sealed record EpisodePlanInputSnapshot(
    string MainVideoPath,
    bool HasPrimaryVideoSource,
    IReadOnlyList<string> SubtitlePaths,
    IReadOnlyList<string> AttachmentPaths,
    IReadOnlyList<string> ManualAttachmentPaths,
    string OutputPath,
    string TitleForMux,
    IReadOnlyCollection<string> ExcludedSourcePaths,
    IReadOnlyList<string> PlannedVideoPaths,
    IReadOnlyList<string> DetectionNotes,
    string SeriesName,
    string SeasonNumber,
    string EpisodeNumber,
    string? OriginalLanguage,
    string? VideoLanguageOverride,
    string? AudioLanguageOverride,
    string? AudioDescriptionPath) : IEpisodePlanInput
{
    /// <summary>
    /// Erstellt eine defensive Kopie aller planrelevanten Werte.
    /// </summary>
    public static EpisodePlanInputSnapshot Create(IEpisodePlanInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return new EpisodePlanInputSnapshot(
            input.MainVideoPath,
            input.HasPrimaryVideoSource,
            input.SubtitlePaths.ToList(),
            input.AttachmentPaths.ToList(),
            input.ManualAttachmentPaths.ToList(),
            input.OutputPath,
            input.TitleForMux,
            input.ExcludedSourcePaths.ToList(),
            input.PlannedVideoPaths.ToList(),
            input.DetectionNotes.ToList(),
            input.SeriesName,
            input.SeasonNumber,
            input.EpisodeNumber,
            input.OriginalLanguage,
            input.VideoLanguageOverride,
            input.AudioLanguageOverride,
            input.AudioDescriptionPath);
    }
}
