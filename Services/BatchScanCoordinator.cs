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

    /// <summary>
    /// Bereitet einen Batch-Ordner einmalig für mehrere Einzelscans vor.
    /// </summary>
    /// <param name="sourceDirectory">Zu scannender Quellordner.</param>
    /// <returns>Wiederverwendbarer Ordnerkontext inklusive vorgefilterter Hauptvideos.</returns>
    public BatchScanDirectoryContext CreateDirectoryContext(string sourceDirectory)
    {
        var detectionContext = _muxService.CreateDirectoryDetectionContext(sourceDirectory);
        return new BatchScanDirectoryContext(sourceDirectory, detectionContext.MainVideoFiles, detectionContext);
    }

    /// <summary>
    /// Führt Erkennung, TVDB-Auflösung und finale Ausgabepfad-Bildung für eine einzelne Batch-Quelle aus.
    /// </summary>
    /// <param name="sourceFilePath">Pfad zur primären Videodatei.</param>
    /// <param name="outputDirectory">Aktuelle Zielwurzel für die Ausgabedatei.</param>
    /// <param name="onDetectionProgress">Optionaler Callback für Fortschrittsmeldungen der Dateierkennung.</param>
    /// <param name="excludedSourcePaths">Optionaler Satz an Dateipfaden, die bei der Erkennung ignoriert werden sollen.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <returns>Gesamtergebnis aus lokaler Erkennung, Metadatenauflösung und Ausgabepfad.</returns>
    public async Task<BatchScanCoordinatorResult> ScanAsync(
        string sourceFilePath,
        string outputDirectory,
        Action<DetectionProgressUpdate>? onDetectionProgress = null,
        IReadOnlyCollection<string>? excludedSourcePaths = null,
        CancellationToken cancellationToken = default)
    {
        var sourceDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Der Ordner der Batch-Quelle konnte nicht bestimmt werden.");
        return await ScanAsync(
            CreateDirectoryContext(sourceDirectory),
            sourceFilePath,
            outputDirectory,
            onDetectionProgress,
            excludedSourcePaths,
            cancellationToken);
    }

    /// <summary>
    /// Führt Erkennung, TVDB-Auflösung und finale Ausgabepfad-Bildung für eine einzelne Batch-Quelle mit vorbereitetem Ordnerkontext aus.
    /// </summary>
    /// <param name="directoryContext">Vorbereiteter Batch-Ordnerkontext.</param>
    /// <param name="sourceFilePath">Pfad zur primären Videodatei.</param>
    /// <param name="outputDirectory">Aktuelle Zielwurzel für die Ausgabedatei.</param>
    /// <param name="onDetectionProgress">Optionaler Callback für Fortschrittsmeldungen der Dateierkennung.</param>
    /// <param name="excludedSourcePaths">Optionaler Satz an Dateipfaden, die bei der Erkennung ignoriert werden sollen.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <returns>Gesamtergebnis aus lokaler Erkennung, Metadatenauflösung und Ausgabepfad.</returns>
    public async Task<BatchScanCoordinatorResult> ScanAsync(
        BatchScanDirectoryContext directoryContext,
        string sourceFilePath,
        string outputDirectory,
        Action<DetectionProgressUpdate>? onDetectionProgress = null,
        IReadOnlyCollection<string>? excludedSourcePaths = null,
        CancellationToken cancellationToken = default)
    {
        var detected = await _muxService.DetectFromSelectedVideoAsync(
            sourceFilePath,
            directoryContext.DetectionContext,
            onDetectionProgress,
            excludedSourcePaths,
            cancellationToken);
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

/// <summary>
/// Vorbereiteter Batch-Ordnerkontext mit Hauptvideo-Liste und wiederverwendbarer Detection-Grundlage.
/// </summary>
public sealed record BatchScanDirectoryContext(
    string SourceDirectory,
    IReadOnlyList<string> MainVideoFiles,
    SeriesEpisodeMuxPlanner.DirectoryDetectionContext DetectionContext);
