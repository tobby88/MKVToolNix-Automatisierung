using System.Collections.Concurrent;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Fragt mkvmerge für Container- und Track-Metadaten ab und hält diese dateibezogen im Speicher vor.
/// </summary>
public sealed partial class MkvMergeProbeService
{
    private readonly ConcurrentDictionary<string, CachedFileValue<MediaTrackMetadata>> _mediaTrackCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedFileValue<AudioTrackMetadata>> _audioTrackCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedFileValue<ContainerMetadata>> _containerCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Entfernt alle gecachten Probe-Ergebnisse für eine einzelne Datei.
    /// </summary>
    /// <param name="filePath">Pfad der zu invalidierenden Datei.</param>
    public void Invalidate(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        _mediaTrackCache.TryRemove(filePath, out _);
        _audioTrackCache.TryRemove(filePath, out _);
        _containerCache.TryRemove(filePath, out _);
    }

    /// <summary>
    /// Entfernt alle gecachten Probe-Ergebnisse für mehrere Dateien.
    /// </summary>
    /// <param name="filePaths">Dateiliste, deren Cache-Einträge verworfen werden sollen.</param>
    public void Invalidate(IEnumerable<string?> filePaths)
    {
        foreach (var filePath in filePaths)
        {
            Invalidate(filePath);
        }
    }

    /// <summary>
    /// Liest die primären Video-/Audio-Metadaten einer Datei synchron über <c>mkvmerge --identify</c>.
    /// </summary>
    /// <param name="mkvMergePath">Pfad zur verwendeten mkvmerge-Executable.</param>
    /// <param name="inputFilePath">Zu analysierende Mediendatei.</param>
    /// <returns>Qualitäts- und Sprachmetadaten der Hauptspuren.</returns>
    public MediaTrackMetadata ReadPrimaryVideoMetadata(string mkvMergePath, string inputFilePath)
    {
        var snapshot = FileStateSnapshot.TryCreate(inputFilePath);
        if (_mediaTrackCache.TryGetValue(inputFilePath, out var cachedMetadata) && cachedMetadata.Matches(snapshot))
        {
            return cachedMetadata.Value;
        }

        using var trackDocument = MkvMergeIdentifyRunner.Identify(mkvMergePath, inputFilePath);
        var metadata = MkvMergeIdentifyParser.CreatePrimaryVideoMetadata(trackDocument, inputFilePath);
        StoreCachedValue(_mediaTrackCache, inputFilePath, snapshot, metadata);
        return metadata;
    }

    /// <summary>
    /// Liest die primären Video-/Audio-Metadaten einer Datei asynchron über <c>mkvmerge --identify</c>.
    /// </summary>
    /// <param name="mkvMergePath">Pfad zur verwendeten mkvmerge-Executable.</param>
    /// <param name="inputFilePath">Zu analysierende Mediendatei.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <returns>Qualitäts- und Sprachmetadaten der Hauptspuren.</returns>
    public async Task<MediaTrackMetadata> ReadPrimaryVideoMetadataAsync(
        string mkvMergePath,
        string inputFilePath,
        CancellationToken cancellationToken = default)
    {
        var snapshot = FileStateSnapshot.TryCreate(inputFilePath);
        if (_mediaTrackCache.TryGetValue(inputFilePath, out var cachedMetadata) && cachedMetadata.Matches(snapshot))
        {
            return cachedMetadata.Value;
        }

        using var trackDocument = await MkvMergeIdentifyRunner.IdentifyAsync(mkvMergePath, inputFilePath, cancellationToken);
        var metadata = MkvMergeIdentifyParser.CreatePrimaryVideoMetadata(trackDocument, inputFilePath);
        StoreCachedValue(_mediaTrackCache, inputFilePath, snapshot, metadata);
        return metadata;
    }

    /// <summary>
    /// Liest die erste relevante Audiospur einer Datei asynchron aus.
    /// </summary>
    /// <param name="mkvMergePath">Pfad zur verwendeten mkvmerge-Executable.</param>
    /// <param name="inputFilePath">Zu analysierende Mediendatei.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <returns>Metadaten der ersten gefundenen Audiospur.</returns>
    public async Task<AudioTrackMetadata> ReadFirstAudioTrackMetadataAsync(
        string mkvMergePath,
        string inputFilePath,
        CancellationToken cancellationToken = default)
    {
        var snapshot = FileStateSnapshot.TryCreate(inputFilePath);
        if (_audioTrackCache.TryGetValue(inputFilePath, out var cachedMetadata) && cachedMetadata.Matches(snapshot))
        {
            return cachedMetadata.Value;
        }

        using var trackDocument = await MkvMergeIdentifyRunner.IdentifyAsync(mkvMergePath, inputFilePath, cancellationToken);
        var metadata = MkvMergeIdentifyParser.CreateFirstAudioTrackMetadata(trackDocument, inputFilePath);
        StoreCachedValue(_audioTrackCache, inputFilePath, snapshot, metadata);
        return metadata;
    }

    /// <summary>
    /// Liest alle Tracks und Anhänge eines Containers asynchron aus.
    /// </summary>
    /// <param name="mkvMergePath">Pfad zur verwendeten mkvmerge-Executable.</param>
    /// <param name="inputFilePath">Zu analysierende Containerdatei.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    /// <returns>Vollständige Container-Metadaten inklusive Anhängen.</returns>
    public async Task<ContainerMetadata> ReadContainerMetadataAsync(
        string mkvMergePath,
        string inputFilePath,
        CancellationToken cancellationToken = default)
    {
        var snapshot = FileStateSnapshot.TryCreate(inputFilePath);
        if (_containerCache.TryGetValue(inputFilePath, out var cachedMetadata) && cachedMetadata.Matches(snapshot))
        {
            return cachedMetadata.Value;
        }

        using var trackDocument = await MkvMergeIdentifyRunner.IdentifyAsync(mkvMergePath, inputFilePath, cancellationToken);
        var metadata = MkvMergeIdentifyParser.CreateContainerMetadata(trackDocument, inputFilePath);
        StoreCachedValue(_containerCache, inputFilePath, snapshot, metadata);
        return metadata;
    }

    private static void StoreCachedValue<T>(
        ConcurrentDictionary<string, CachedFileValue<T>> cache,
        string filePath,
        FileStateSnapshot? snapshot,
        T value)
    {
        if (snapshot is null)
        {
            cache.TryRemove(filePath, out _);
            return;
        }

        cache[filePath] = new CachedFileValue<T>(snapshot.Value, value);
    }
}
