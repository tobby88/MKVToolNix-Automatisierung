using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Minimale Eingabeoberfläche, damit Planerzeugung aus unterschiedlichen ViewModels heraus wiederverwendbar bleibt.
/// </summary>
public interface IEpisodePlanInput
{
    /// <summary>
    /// Pfad zur primären Videodatei.
    /// </summary>
    string MainVideoPath { get; }

    /// <summary>
    /// Optionaler Pfad zur AD-Datei.
    /// </summary>
    string? AudioDescriptionPath { get; }

    /// <summary>
    /// Gewählte externe Untertiteldateien.
    /// </summary>
    IReadOnlyList<string> SubtitlePaths { get; }

    /// <summary>
    /// Gewählte zusätzliche Dateianhänge.
    /// </summary>
    IReadOnlyList<string> AttachmentPaths { get; }

    /// <summary>
    /// Explizit manuell gewählte Dateianhänge. Automatisch erkannte TXT-Anhänge der Videoquellen sind hier nicht enthalten.
    /// </summary>
    IReadOnlyList<string> ManualAttachmentPaths { get; }

    /// <summary>
    /// Vollständiger Zielpfad der Ausgabe-MKV.
    /// </summary>
    string OutputPath { get; }

    /// <summary>
    /// Titel, der in den finalen mkvmerge-Aufruf übernommen werden soll.
    /// </summary>
    string TitleForMux { get; }

    /// <summary>
    /// Optionaler Satz an Quellpfaden, die bei einer erneuten Detection nicht wiederverwendet werden dürfen.
    /// </summary>
    IReadOnlyCollection<string> ExcludedSourcePaths { get; }
}

/// <summary>
/// Erzeugt Mux-Pläne aus den aktuell sichtbaren Eingaben eines Moduls.
/// </summary>
public sealed class EpisodePlanCoordinator
{
    private readonly SeriesEpisodeMuxService _muxService;

    public EpisodePlanCoordinator(SeriesEpisodeMuxService muxService)
    {
        _muxService = muxService;
    }

    /// <summary>
    /// Baut aus einer abstrahierten UI-Eingabe einen vollständigen Mux-Plan.
    /// </summary>
    /// <param name="input">Lesbare Eingabefläche des aufrufenden Moduls.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <returns>Der vollständige Mux-Plan für die aktuelle Episode.</returns>
    public Task<SeriesEpisodeMuxPlan> BuildPlanAsync(
        IEpisodePlanInput input,
        CancellationToken cancellationToken = default)
    {
        return _muxService.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            input.MainVideoPath,
            input.AudioDescriptionPath,
            input.SubtitlePaths,
            input.AttachmentPaths,
            input.OutputPath,
            input.TitleForMux,
            input.ExcludedSourcePaths,
            input.ManualAttachmentPaths),
            cancellationToken);
    }
}
