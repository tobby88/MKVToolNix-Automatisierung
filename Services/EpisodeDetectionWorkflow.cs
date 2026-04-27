using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Gemeinsamer Workflow für lokale Dateierkennung, automatische TVDB-Auflösung und
/// konservativen Archiv-Sondermaterial-Fallback. Single- und Batch-Modul unterscheiden
/// sich danach nur noch bei UI-Zuständen, Zielpfad-Policy und Batch-Parallelisierung.
/// </summary>
internal sealed class EpisodeDetectionWorkflow
{
    private readonly SeriesEpisodeMuxService _muxService;
    private readonly EpisodeMetadataLookupService _episodeMetadata;
    private readonly EpisodeOutputPathService _outputPaths;

    /// <summary>
    /// Initialisiert den gemeinsamen Detection-Workflow.
    /// </summary>
    public EpisodeDetectionWorkflow(
        SeriesEpisodeMuxService muxService,
        EpisodeMetadataLookupService episodeMetadata,
        EpisodeOutputPathService outputPaths)
    {
        _muxService = muxService;
        _episodeMetadata = episodeMetadata;
        _outputPaths = outputPaths;
    }

    /// <summary>
    /// Führt Dateierkennung und Metadatenauflösung für eine einzelne Quelle aus.
    /// </summary>
    public async Task<EpisodeDetectionWorkflowResult> DetectAndResolveAsync(
        string selectedVideoPath,
        Action<DetectionProgressUpdate>? onDetectionProgress = null,
        IReadOnlyCollection<string>? excludedSourcePaths = null,
        Action? onDetectionCompleted = null,
        string? specialFallbackOutputRoot = null,
        CancellationToken cancellationToken = default)
    {
        var detected = await _muxService.DetectFromSelectedVideoAsync(
            selectedVideoPath,
            onDetectionProgress,
            excludedSourcePaths,
            cancellationToken);
        onDetectionCompleted?.Invoke();
        return await ResolveMetadataAsync(detected, specialFallbackOutputRoot, cancellationToken);
    }

    /// <summary>
    /// Führt Dateierkennung und Metadatenauflösung mit einem bereits vorbereiteten
    /// Ordnerkontext aus. Dieser Pfad ist vor allem für den Batch-Scan relevant.
    /// </summary>
    public async Task<EpisodeDetectionWorkflowResult> DetectAndResolveAsync(
        string selectedVideoPath,
        SeriesEpisodeMuxPlanner.DirectoryDetectionContext directoryContext,
        Action<DetectionProgressUpdate>? onDetectionProgress = null,
        IReadOnlyCollection<string>? excludedSourcePaths = null,
        Action? onDetectionCompleted = null,
        string? specialFallbackOutputRoot = null,
        CancellationToken cancellationToken = default)
    {
        var detected = await _muxService.DetectFromSelectedVideoAsync(
            selectedVideoPath,
            directoryContext,
            onDetectionProgress,
            excludedSourcePaths,
            cancellationToken);
        onDetectionCompleted?.Invoke();
        return await ResolveMetadataAsync(detected, specialFallbackOutputRoot, cancellationToken);
    }

    private async Task<EpisodeDetectionWorkflowResult> ResolveMetadataAsync(
        AutoDetectedEpisodeFiles detected,
        string? specialFallbackOutputRoot,
        CancellationToken cancellationToken)
    {
        var localGuess = new EpisodeMetadataGuess(
            detected.SeriesName,
            detected.SuggestedTitle,
            detected.SeasonNumber,
            detected.EpisodeNumber);

        cancellationToken.ThrowIfCancellationRequested();
        var metadataResolution = await _episodeMetadata.ResolveAutomaticallyAsync(localGuess, cancellationToken);
        if (metadataResolution.Selection is not null)
        {
            detected = EpisodeMetadataMergeHelper.ApplySelection(detected, metadataResolution.Selection);
        }
        else
        {
            (detected, metadataResolution) = ArchiveSpecialMetadataFallback.ApplyIfAvailable(
                detected,
                metadataResolution,
                _outputPaths,
                _episodeMetadata,
                specialFallbackOutputRoot);
        }

        return new EpisodeDetectionWorkflowResult(detected, localGuess, metadataResolution);
    }
}

/// <summary>
/// Ergebnis des gemeinsamen Detection-Workflows inklusive lokaler Erkennung und finaler
/// automatischer Metadatenentscheidung.
/// </summary>
internal sealed record EpisodeDetectionWorkflowResult(
    AutoDetectedEpisodeFiles Detected,
    EpisodeMetadataGuess LocalGuess,
    EpisodeMetadataResolutionResult MetadataResolution);
