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

    public SeriesEpisodeMuxPlanner(
        MkvToolNixLocator locator,
        MkvMergeProbeService probeService,
        SeriesArchiveService archiveService,
        IMediaDurationProbe durationProbe)
    {
        _locator = locator;
        _probeService = probeService;
        _archiveService = archiveService;
        _durationProbe = durationProbe;
    }

    public AutoDetectedEpisodeFiles DetectFromMainVideo(
        string mainVideoPath,
        Action<DetectionProgressUpdate>? onProgress = null,
        IReadOnlyCollection<string>? excludedSourcePaths = null)
    {
        return DetectFromMainVideo(mainVideoPath, directoryContext: null, onProgress, excludedSourcePaths);
    }

    internal AutoDetectedEpisodeFiles DetectFromMainVideo(
        string mainVideoPath,
        DirectoryDetectionContext? directoryContext,
        Action<DetectionProgressUpdate>? onProgress = null,
        IReadOnlyCollection<string>? excludedSourcePaths = null)
    {
        if (!File.Exists(mainVideoPath))
        {
            throw new FileNotFoundException($"Videodatei nicht gefunden: {mainVideoPath}");
        }

        ReportProgress(onProgress, "Bereite Erkennung vor...", 0);

        var excludedPathSet = excludedSourcePaths is null || excludedSourcePaths.Count == 0
            ? null
            : new HashSet<string>(excludedSourcePaths, StringComparer.OrdinalIgnoreCase);

        var detected = EpisodeFileNameHelper.LooksLikeAudioDescription(mainVideoPath)
            ? DetectFromAudioDescription(mainVideoPath, directoryContext, onProgress, excludedPathSet)
            : DetectFromNormalVideo(mainVideoPath, directoryContext, onProgress, excludedPathSet);

        return detected;
    }

    public void InvalidatePlanningCaches()
    {
        // Bewusst leer: Erkennungsergebnisse werden nicht mehr global nach Dateipfad gecacht,
        // damit nachträglich auftauchende Quellen oder Begleitdateien sofort sichtbar bleiben.
    }

    public void InvalidateProbeCaches(IEnumerable<string?> filePaths)
    {
        _probeService.Invalidate(filePaths);
    }

}

