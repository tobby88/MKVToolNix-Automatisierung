using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Minimale Eingabeoberfläche, damit Planerzeugung aus unterschiedlichen ViewModels heraus wiederverwendbar bleibt.
/// </summary>
internal interface IEpisodePlanInput
{
    /// <summary>
    /// Pfad zur aktuell ausgewählten Erkennungsquelle. Bei AD-only-Fällen kann dies bewusst die AD-Datei sein.
    /// </summary>
    string MainVideoPath { get; }

    /// <summary>
    /// Kennzeichnet, ob für diese Episode bereits eine frische Hauptvideoquelle vorliegt.
    /// </summary>
    bool HasPrimaryVideoSource { get; }

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

    /// <summary>
    /// Bereits erkannte und aktuell im UI bestätigte Videopfad-Auswahl in finaler Reihenfolge.
    /// </summary>
    IReadOnlyList<string> PlannedVideoPaths { get; }

    /// <summary>
    /// Bereits bekannte Detection-Hinweise, die unverändert in den Mux-Plan übernommen werden können.
    /// </summary>
    IReadOnlyList<string> DetectionNotes { get; }

    /// <summary>
    /// Originalsprache der Serie (aus TVDB-Metadaten), z. B. <c>swe</c> für Schwedisch oder <c>de</c> für Deutsch.
    /// Null oder leer, wenn unbekannt; in diesem Fall wird der <c>--original-flag</c> wie bisher auf <c>yes</c> gesetzt.
    /// </summary>
    string? OriginalLanguage { get; }
}

/// <summary>
/// Erzeugt Mux-Pläne aus den aktuell sichtbaren Eingaben eines Moduls.
/// </summary>
internal sealed class EpisodePlanCoordinator
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
            input.ManualAttachmentPaths,
            input.HasPrimaryVideoSource,
            input.PlannedVideoPaths,
            input.DetectionNotes,
            input.OriginalLanguage),
            cancellationToken);
    }
}
