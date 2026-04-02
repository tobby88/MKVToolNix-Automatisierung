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
    /// <returns>Wiederverwendbarer Kontext für Batch- oder Mehrfacherkennung innerhalb desselben Ordners.</returns>
    public DirectoryDetectionContext CreateDirectoryDetectionContext(string sourceDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Quellordner nicht gefunden: {sourceDirectory}");
        }

        var allFiles = Directory.GetFiles(sourceDirectory);
        var companionFilesByBaseName = BuildCompanionFileLookup(allFiles);
        var videoSeeds = allFiles
            .Where(path => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            .Select(BuildCandidateSeed)
            .ToList();

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
        private readonly ConcurrentDictionary<string, Lazy<NormalVideoCandidate>> _normalVideoCandidates;
        private readonly ConcurrentDictionary<string, Lazy<AudioDescriptionCandidate>> _audioDescriptionCandidates;

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
            _normalVideoCandidates = new ConcurrentDictionary<string, Lazy<NormalVideoCandidate>>(StringComparer.OrdinalIgnoreCase);
            _audioDescriptionCandidates = new ConcurrentDictionary<string, Lazy<AudioDescriptionCandidate>>(StringComparer.OrdinalIgnoreCase);
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

        internal NormalVideoCandidate GetOrCreateNormalVideoCandidate(CandidateSeed seed, string mkvMergePath)
        {
            var lazyCandidate = _normalVideoCandidates.GetOrAdd(
                seed.FilePath,
                _ => new Lazy<NormalVideoCandidate>(
                    () => _owner.BuildNormalVideoCandidate(seed, mkvMergePath, CompanionFilesByBaseName),
                    System.Threading.LazyThreadSafetyMode.ExecutionAndPublication));
            return lazyCandidate.Value;
        }

        internal AudioDescriptionCandidate GetOrCreateAudioDescriptionCandidate(CandidateSeed seed)
        {
            var lazyCandidate = _audioDescriptionCandidates.GetOrAdd(
                seed.FilePath,
                _ => new Lazy<AudioDescriptionCandidate>(
                    () => _owner.BuildAudioDescriptionCandidate(seed),
                    System.Threading.LazyThreadSafetyMode.ExecutionAndPublication));
            return lazyCandidate.Value;
        }
    }
}
