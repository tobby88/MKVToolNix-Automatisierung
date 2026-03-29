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
    private readonly object _cacheSync = new();
    private Dictionary<string, AutoDetectedEpisodeFiles> _autoDetectionCache = new(StringComparer.OrdinalIgnoreCase);

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
        IReadOnlyCollection<string>? excludedSourcePaths = null,
        bool allowCachedResult = true)
    {
        if (allowCachedResult
            && directoryContext is null
            && onProgress is null
            && (excludedSourcePaths is null || excludedSourcePaths.Count == 0)
            && _autoDetectionCache.TryGetValue(mainVideoPath, out var cachedDetection))
        {
            return cachedDetection;
        }

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

        if (directoryContext is null
            && onProgress is null
            && (excludedSourcePaths is null || excludedSourcePaths.Count == 0))
        {
            lock (_cacheSync)
            {
                _autoDetectionCache[mainVideoPath] = detected;
            }
        }

        return detected;
    }

    public void InvalidatePlanningCaches()
    {
        lock (_cacheSync)
        {
            _autoDetectionCache = new Dictionary<string, AutoDetectedEpisodeFiles>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void InvalidateProbeCaches(IEnumerable<string?> filePaths)
    {
        _probeService.Invalidate(filePaths);
    }

}

