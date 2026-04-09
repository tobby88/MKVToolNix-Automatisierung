using System.Text;
using System.Text.RegularExpressions;
using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

/// <summary>
/// Herzstück der fachlichen Planung: erkennt Dateien, bereitet Archivintegration vor und erzeugt Mux-Pläne.
/// </summary>
public sealed partial class SeriesEpisodeMuxPlanner
{
    private static readonly HashSet<string> SupportedSubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt",
        ".ass",
        ".vtt"
    };
    private static readonly HashSet<string> CleanupCompanionExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ttml"
    };
    private readonly MkvToolNixLocator _locator;
    private readonly MkvMergeProbeService _probeService;
    private readonly SeriesArchiveService _archiveService;
    private readonly IMediaDurationProbe _durationProbe;
    private readonly FfprobeDurationProbe? _ffprobeDurationProbe;

    /// <summary>
    /// Initialisiert den zentralen Planer für Dateierkennung, Archivabgleich und Mux-Planerzeugung.
    /// </summary>
    /// <param name="locator">Liefert den aktuell verwendbaren Pfad zur <c>mkvmerge.exe</c>.</param>
    /// <param name="probeService">Liest Container- und Track-Metadaten aus vorhandenen Dateien.</param>
    /// <param name="archiveService">Entscheidet, wie vorhandene Archivdateien in neue Pläne integriert werden.</param>
    /// <param name="durationProbe">Liefert optionale Laufzeiten für Qualitäts- und Kandidatenvergleiche.</param>
    /// <param name="ffprobeDurationProbe">
    /// Optionaler direkter ffprobe-Zugriff für kurze Best-Effort-Laufzeitprüfungen im Planbau.
    /// Solche Prüfungen dürfen die UI nicht blockieren und umgehen deshalb bewusst den breiteren
    /// Windows-Fallback des allgemeinen Laufzeit-Probes.
    /// </param>
    public SeriesEpisodeMuxPlanner(
        MkvToolNixLocator locator,
        MkvMergeProbeService probeService,
        SeriesArchiveService archiveService,
        IMediaDurationProbe durationProbe,
        FfprobeDurationProbe? ffprobeDurationProbe = null)
    {
        _locator = locator;
        _probeService = probeService;
        _archiveService = archiveService;
        _durationProbe = durationProbe;
        _ffprobeDurationProbe = ffprobeDurationProbe;
    }

    /// <summary>
    /// Führt die lokale Dateierkennung für eine ausgewählte Hauptquelle synchron aus.
    /// </summary>
    /// <param name="mainVideoPath">Pfad zur primären Video- oder AD-Datei der Episode.</param>
    /// <param name="onProgress">Optionaler Callback für Status- und Fortschrittsmeldungen.</param>
    /// <param name="excludedSourcePaths">Optionaler Satz an Pfaden, die bei der Erkennung ignoriert werden sollen.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal für Ordner- und Kandidatenanalyse.</param>
    /// <returns>Automatisch erkannte Episodenquellen inklusive Metadaten- und Zielvorschlägen.</returns>
    public AutoDetectedEpisodeFiles DetectFromMainVideo(
        string mainVideoPath,
        Action<DetectionProgressUpdate>? onProgress = null,
        IReadOnlyCollection<string>? excludedSourcePaths = null,
        CancellationToken cancellationToken = default)
    {
        return DetectFromMainVideo(mainVideoPath, directoryContext: null, onProgress, excludedSourcePaths, cancellationToken);
    }

    internal AutoDetectedEpisodeFiles DetectFromMainVideo(
        string mainVideoPath,
        DirectoryDetectionContext? directoryContext,
        Action<DetectionProgressUpdate>? onProgress = null,
        IReadOnlyCollection<string>? excludedSourcePaths = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(mainVideoPath))
        {
            throw new FileNotFoundException($"Videodatei nicht gefunden: {mainVideoPath}");
        }

        ReportProgress(onProgress, "Bereite Erkennung vor...", 0);

        var excludedPathSet = excludedSourcePaths is null || excludedSourcePaths.Count == 0
            ? null
            : new HashSet<string>(excludedSourcePaths, StringComparer.OrdinalIgnoreCase);

        var detected = EpisodeFileNameHelper.LooksLikeAudioDescription(mainVideoPath)
            ? DetectFromAudioDescription(mainVideoPath, directoryContext, onProgress, excludedPathSet, cancellationToken)
            : DetectFromNormalVideo(mainVideoPath, directoryContext, onProgress, excludedPathSet, cancellationToken);

        return detected;
    }

    /// <summary>
    /// Verwirft gecachte Probe-Ergebnisse für mehrere betroffene Mediendateien.
    /// </summary>
    /// <param name="filePaths">Dateipfade, deren Probe-Ergebnisse nicht weiterverwendet werden sollen.</param>
    public void InvalidateProbeCaches(IEnumerable<string?> filePaths)
    {
        _probeService.Invalidate(filePaths);
    }

}

