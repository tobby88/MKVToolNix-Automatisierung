using System.Text.RegularExpressions;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Führt die automatische Erkennung für Batch-Quellen parallelisiert aus und sammelt Ergebnisobjekte für das ViewModel.
/// </summary>
public sealed class BatchScanCoordinator
{
    private readonly SeriesEpisodeMuxService _muxService;
    private readonly EpisodeMetadataLookupService _episodeMetadata;
    private readonly EpisodeOutputPathService _outputPaths;

    public BatchScanCoordinator(
        SeriesEpisodeMuxService muxService,
        EpisodeMetadataLookupService episodeMetadata,
        EpisodeOutputPathService outputPaths)
    {
        _muxService = muxService;
        _episodeMetadata = episodeMetadata;
        _outputPaths = outputPaths;
    }

    public IReadOnlyList<string> FindMainVideoFiles(string sourceDirectory)
    {
        return Directory.GetFiles(sourceDirectory, "*.mp4")
            .Where(file => !EpisodeFileNameHelper.LooksLikeAudioDescription(file))
            .OrderBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<BatchScanCoordinatorResult> ScanAsync(
        string sourceFilePath,
        string outputDirectory,
        Action<DetectionProgressUpdate>? onDetectionProgress = null,
        IReadOnlyCollection<string>? excludedSourcePaths = null)
    {
        var detected = await _muxService.DetectFromSelectedVideoAsync(
            sourceFilePath,
            onDetectionProgress,
            excludedSourcePaths);
        var localGuess = new EpisodeMetadataGuess(
            detected.SeriesName,
            detected.SuggestedTitle,
            detected.SeasonNumber,
            detected.EpisodeNumber);

        var metadataResolution = await _episodeMetadata.ResolveAutomaticallyAsync(localGuess);
        if (metadataResolution.Selection is not null)
        {
            detected = EpisodeMetadataMergeHelper.ApplySelection(detected, metadataResolution.Selection);
        }

        var fallbackDirectory = Path.GetDirectoryName(detected.MainVideoPath)
            ?? Path.GetDirectoryName(sourceFilePath)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        var outputPath = _outputPaths.BuildOutputPath(
            fallbackDirectory,
            detected.SeriesName,
            detected.SeasonNumber,
            detected.EpisodeNumber,
            detected.SuggestedTitle,
            outputDirectory);

        return new BatchScanCoordinatorResult(detected, localGuess, metadataResolution, outputPath);
    }
}

/// <summary>
/// Ergebnis eines einzelnen Batch-Scan-Laufs inklusive lokaler und TVDB-basierter Metadaten.
/// </summary>
public sealed record BatchScanCoordinatorResult(
    AutoDetectedEpisodeFiles Detected,
    EpisodeMetadataGuess LocalGuess,
    EpisodeMetadataResolutionResult MetadataResolution,
    string OutputPath);
