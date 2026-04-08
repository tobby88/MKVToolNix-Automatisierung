using System.Collections.Concurrent;
using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

// Dieser Partial kapselt die wiederverwendbare Ordnerbasis fuer Batch-Scans: Seeds, Companion-Lookups und pro Datei gecachte Kandidaten.
public sealed partial class SeriesEpisodeMuxPlanner
{
    /// <summary>
    /// Bereitet alle relevanten Video- und Begleitdateien eines Quellordners einmalig für mehrere Erkennungsläufe vor.
    /// </summary>
    /// <param name="sourceDirectory">Ordner mit den Episodenquellen.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal für die vorbereitende Dateiliste.</param>
    /// <returns>Wiederverwendbarer Kontext für Batch- oder Mehrfacherkennung innerhalb desselben Ordners.</returns>
    public DirectoryDetectionContext CreateDirectoryDetectionContext(
        string sourceDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Quellordner nicht gefunden: {sourceDirectory}");
        }

        var allFiles = Directory.GetFiles(sourceDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        var companionFilesByBaseName = BuildCompanionFileLookup(allFiles);
        var videoSeeds = allFiles
            .Where(path => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            .Select(path =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return BuildCandidateSeed(path);
            })
            .ToList();
        cancellationToken.ThrowIfCancellationRequested();

        return new DirectoryDetectionContext(this, sourceDirectory, companionFilesByBaseName, videoSeeds);
    }

    private CandidateSeed BuildCandidateSeed(string filePath)
    {
        var attachmentPath = File.Exists(Path.ChangeExtension(filePath, ".txt"))
            ? Path.ChangeExtension(filePath, ".txt")
            : null;

        var textMetadata = CompanionTextMetadataReader.Read(attachmentPath);
        var identity = ParseEpisodeIdentity(filePath, textMetadata);
        return new CandidateSeed(filePath, attachmentPath, textMetadata, identity);
    }

    private static List<string> FindExactSubtitleFiles(
        string videoFilePath,
        IReadOnlyDictionary<string, IReadOnlyList<string>> companionFilesByBaseName)
    {
        return GetCompanionFiles(videoFilePath, companionFilesByBaseName)
            .Where(path => SupportedSubtitleExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> FindExactCompanionCleanupFiles(
        string videoFilePath,
        IReadOnlyDictionary<string, IReadOnlyList<string>> companionFilesByBaseName)
    {
        return GetCompanionFiles(videoFilePath, companionFilesByBaseName)
            .Where(path =>
            {
                var extension = Path.GetExtension(path);
                return SupportedSubtitleExtensions.Contains(extension)
                    || CleanupCompanionExtensions.Contains(extension);
            })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, IReadOnlyList<string>> BuildCompanionFileLookup(IEnumerable<string> allFiles)
    {
        var lookup = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in allFiles.GroupBy(BuildCompanionLookupKey, StringComparer.OrdinalIgnoreCase))
        {
            lookup[group.Key] = group.ToList();
        }

        return lookup;
    }

    private static IReadOnlyList<string> GetCompanionFiles(
        string videoFilePath,
        IReadOnlyDictionary<string, IReadOnlyList<string>> companionFilesByBaseName)
    {
        var key = BuildCompanionLookupKey(videoFilePath);
        return companionFilesByBaseName.TryGetValue(key, out var files)
            ? files
            : [];
    }

    private static string BuildCompanionLookupKey(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException("Der Ordner der Videodatei konnte nicht bestimmt werden.");
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
        return Path.Combine(directory, fileNameWithoutExtension);
    }

    private static IReadOnlyList<string> BuildBatchEntryFiles(IReadOnlyList<CandidateSeed> candidateSeeds)
    {
        var normalSeeds = candidateSeeds
            .Where(seed => !EpisodeFileNameHelper.LooksLikeAudioDescription(seed.FilePath))
            .ToList();
        var audioDescriptionOnlySeeds = candidateSeeds
            .Where(seed => EpisodeFileNameHelper.LooksLikeAudioDescription(seed.FilePath))
            .Where(seed => !normalSeeds.Any(normalSeed => normalSeed.Identity.Matches(seed.Identity)))
            .GroupBy(seed => seed.Identity)
            .Select(group => group
                .OrderBy(seed => Path.GetFileName(seed.FilePath), StringComparer.OrdinalIgnoreCase)
                .First())
            .ToList();

        return normalSeeds
            .Concat(audioDescriptionOnlySeeds)
            .Select(seed => seed.FilePath)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Wiederverwendbarer Ordnerkontext für mehrere Erkennungsläufe innerhalb desselben Quellverzeichnisses.
    /// </summary>
    public sealed class DirectoryDetectionContext
    {
        private readonly SeriesEpisodeMuxPlanner _owner;
        private readonly Dictionary<string, IReadOnlyList<string>> _companionFilesByBaseName;
        private readonly Dictionary<string, CandidateSeed> _candidateSeedsByPath;
        private readonly IReadOnlyList<CandidateSeed> _candidateSeeds;
        private readonly ConcurrentDictionary<string, NormalVideoCandidate> _normalVideoCandidates;
        private readonly ConcurrentDictionary<string, AudioDescriptionCandidate> _audioDescriptionCandidates;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _normalVideoCandidateGates;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _audioDescriptionCandidateGates;

        internal DirectoryDetectionContext(
            SeriesEpisodeMuxPlanner owner,
            string sourceDirectory,
            IReadOnlyDictionary<string, IReadOnlyList<string>> companionFilesByBaseName,
            IReadOnlyList<CandidateSeed> candidateSeeds)
        {
            _owner = owner;
            SourceDirectory = sourceDirectory;
            _companionFilesByBaseName = companionFilesByBaseName.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
            _candidateSeedsByPath = candidateSeeds.ToDictionary(seed => seed.FilePath, seed => seed, StringComparer.OrdinalIgnoreCase);
            _candidateSeeds = candidateSeeds.ToList();
            _normalVideoCandidates = new ConcurrentDictionary<string, NormalVideoCandidate>(StringComparer.OrdinalIgnoreCase);
            _audioDescriptionCandidates = new ConcurrentDictionary<string, AudioDescriptionCandidate>(StringComparer.OrdinalIgnoreCase);
            _normalVideoCandidateGates = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
            _audioDescriptionCandidateGates = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
            MainVideoFiles = BuildBatchEntryFiles(candidateSeeds);
        }

        /// <summary>
        /// Ursprünglicher Quellordner, aus dem dieser Kontext vorbereitet wurde.
        /// </summary>
        public string SourceDirectory { get; }

        /// <summary>
        /// Beim Vorbereiten erkannte Batch-Einstiegsdateien: reguläre Hauptvideos sowie AD-only-Fälle ohne passende Hauptvideoquelle.
        /// </summary>
        public IReadOnlyList<string> MainVideoFiles { get; }

        internal IReadOnlyDictionary<string, IReadOnlyList<string>> CompanionFilesByBaseName => _companionFilesByBaseName;

        internal CandidateSeed GetSelectedSeed(string selectedPath)
        {
            return _candidateSeedsByPath.TryGetValue(selectedPath, out var selectedSeed)
                ? selectedSeed
                : _owner.BuildCandidateSeed(selectedPath);
        }

        internal EpisodeSeedCollection GetEpisodeSeeds(CandidateSeed selectedSeed)
        {
            var matchingSeeds = _candidateSeeds
                .Where(seed => selectedSeed.Identity.Matches(seed.Identity))
                .ToList();

            if (matchingSeeds.Count > 0)
            {
                if (!matchingSeeds.Any(seed => PathComparisonHelper.AreSamePath(seed.FilePath, selectedSeed.FilePath)))
                {
                    matchingSeeds.Insert(0, selectedSeed);
                }

                return new EpisodeSeedCollection(
                    matchingSeeds,
                    matchingSeeds.Where(seed => !EpisodeFileNameHelper.LooksLikeAudioDescription(seed.FilePath)).ToList(),
                    matchingSeeds.Where(seed => EpisodeFileNameHelper.LooksLikeAudioDescription(seed.FilePath)).ToList());
            }

            return new EpisodeSeedCollection(
                [selectedSeed],
                EpisodeFileNameHelper.LooksLikeAudioDescription(selectedSeed.FilePath) ? [] : [selectedSeed],
                EpisodeFileNameHelper.LooksLikeAudioDescription(selectedSeed.FilePath) ? [selectedSeed] : []);
        }

        internal NormalVideoCandidate GetOrCreateNormalVideoCandidate(
            CandidateSeed seed,
            string mkvMergePath,
            CancellationToken cancellationToken)
        {
            return GetOrCreateCandidate(
                seed.FilePath,
                _normalVideoCandidates,
                _normalVideoCandidateGates,
                () => _owner.BuildNormalVideoCandidate(
                    seed,
                    mkvMergePath,
                    CompanionFilesByBaseName,
                    cancellationToken),
                cancellationToken);
        }

        internal AudioDescriptionCandidate GetOrCreateAudioDescriptionCandidate(
            CandidateSeed seed,
            CancellationToken cancellationToken)
        {
            return GetOrCreateCandidate(
                seed.FilePath,
                _audioDescriptionCandidates,
                _audioDescriptionCandidateGates,
                () => _owner.BuildAudioDescriptionCandidate(seed, cancellationToken),
                cancellationToken);
        }

        /// <summary>
        /// Dedupliziert teure Kandidaten-Builds pro Datei auch dann, wenn mehrere Batch-Tasks
        /// denselben Seed nahezu gleichzeitig anfordern.
        /// </summary>
        /// <typeparam name="TCandidate">Fachtyp des gecachten Kandidaten.</typeparam>
        /// <param name="filePath">Eindeutiger Dateipfad des Seeds.</param>
        /// <param name="cache">Zielcache für fertig gebaute Kandidaten.</param>
        /// <param name="gates">Per-Datei-Gates für konkurrierende Erstzugriffe.</param>
        /// <param name="factory">Factory für den eigentlichen Kandidaten-Build.</param>
        /// <param name="cancellationToken">Abbruchsignal auch für wartende Parallelaufrufe.</param>
        /// <returns>Den bereits vorhandenen oder neu erzeugten Kandidaten.</returns>
        private static TCandidate GetOrCreateCandidate<TCandidate>(
            string filePath,
            ConcurrentDictionary<string, TCandidate> cache,
            ConcurrentDictionary<string, SemaphoreSlim> gates,
            Func<TCandidate> factory,
            CancellationToken cancellationToken)
            where TCandidate : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cache.TryGetValue(filePath, out var cachedCandidate))
            {
                return cachedCandidate;
            }

            var gate = gates.GetOrAdd(filePath, static _ => new SemaphoreSlim(1, 1));
            gate.Wait(cancellationToken);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (cache.TryGetValue(filePath, out cachedCandidate))
                {
                    return cachedCandidate;
                }

                var builtCandidate = factory();
                cache[filePath] = builtCandidate;
                return builtCandidate;
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
