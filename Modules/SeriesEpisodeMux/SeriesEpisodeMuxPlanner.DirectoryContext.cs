using System.Collections.Concurrent;
using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

// Dieser Partial kapselt die wiederverwendbare Ordnerbasis für Batch-Scans: Seeds, Companion-Lookups und pro Datei gecachte Kandidaten.
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
        var subtitleOnlySeeds = BuildSubtitleOnlySeeds(allFiles, videoSeeds, cancellationToken);
        var metadataOnlySeeds = BuildMetadataOnlySeeds(allFiles, videoSeeds, subtitleOnlySeeds, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return new DirectoryDetectionContext(this, sourceDirectory, companionFilesByBaseName, videoSeeds, subtitleOnlySeeds, metadataOnlySeeds);
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

    private IReadOnlyList<CandidateSeed> BuildSubtitleOnlySeeds(
        IReadOnlyList<string> allFiles,
        IReadOnlyList<CandidateSeed> videoSeeds,
        CancellationToken cancellationToken)
    {
        // Reine Untertitelpakete haben keine MP4 als natürlichen Einstiegspunkt. Wir bauen
        // deshalb pro Begleitdatei-Basis einen Seed auf dem besten Untertitelformat. Wenn
        // zur selben Basis doch ein Video existiert, bleibt der Untertitel dessen Companion
        // und wird nicht zusätzlich als eigene Batch-Zeile angeboten.
        var videoLookupKeys = videoSeeds
            .Select(seed => BuildCompanionLookupKey(seed.FilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var subtitleOnlySeeds = new List<CandidateSeed>();

        foreach (var group in allFiles
                     .Where(path => SupportedSubtitleExtensions.Contains(Path.GetExtension(path)))
                     .GroupBy(BuildCompanionLookupKey, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (videoLookupKeys.Contains(group.Key))
            {
                continue;
            }

            var representativeSubtitlePath = group
                .OrderBy(path => SubtitleKind.FromExtension(Path.GetExtension(path)).SortRank)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .First();
            subtitleOnlySeeds.Add(BuildCandidateSeed(representativeSubtitlePath));
        }

        return subtitleOnlySeeds;
    }

    private IReadOnlyList<CandidateSeed> BuildMetadataOnlySeeds(
        IReadOnlyList<string> allFiles,
        IReadOnlyList<CandidateSeed> videoSeeds,
        IReadOnlyList<CandidateSeed> subtitleOnlySeeds,
        CancellationToken cancellationToken)
    {
        // Manche Sender hinterlassen zusätzliche TXT-Begleiter mit abweichender ID, obwohl
        // die eigentliche MP4/VTT derselben Folge schon verarbeitet wurde oder in einer
        // anderen Varianten-Gruppe steckt. Solche TXT-Dateien sollen keine eigene GUI-Zeile
        // erzeugen, aber am fachlich passenden Episodeneintrag hängen bleiben, damit das
        // spätere Cleanup keine Reste liegen lässt.
        var seededLookupKeys = videoSeeds
            .Select(seed => BuildCompanionLookupKey(seed.FilePath))
            .Concat(subtitleOnlySeeds.Select(seed => BuildCompanionLookupKey(seed.FilePath)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var metadataOnlySeeds = new List<CandidateSeed>();

        foreach (var textPath in allFiles
                     .Where(path => Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (seededLookupKeys.Contains(BuildCompanionLookupKey(textPath)))
            {
                continue;
            }

            metadataOnlySeeds.Add(BuildCandidateSeed(textPath));
        }

        return metadataOnlySeeds;
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

    private static IReadOnlyList<string> BuildBatchEntryFiles(
        IReadOnlyList<CandidateSeed> candidateSeeds,
        IReadOnlyList<CandidateSeed> subtitleOnlySeeds)
    {
        var normalSeeds = candidateSeeds
            .Where(seed => !EpisodeFileNameHelper.LooksLikeAudioDescription(seed.FilePath))
            .ToList();
        var normalEpisodeGroups = BuildEpisodeSeedGroups(normalSeeds);
        var normalEntrySeeds = normalEpisodeGroups
            .Select(group => group
                .OrderBy(seed => Path.GetFileName(seed.FilePath), StringComparer.OrdinalIgnoreCase)
                .First())
            .ToList();
        var audioDescriptionOnlyCandidates = candidateSeeds
            .Where(seed => EpisodeFileNameHelper.LooksLikeAudioDescription(seed.FilePath))
            .Where(seed => !normalEpisodeGroups.Any(group => group.Any(normalSeed => SeedsBelongToSameEpisode(normalSeed, seed))))
            .ToList();
        var audioDescriptionOnlySeeds = BuildEpisodeSeedGroups(audioDescriptionOnlyCandidates)
            .Select(group => group
                .OrderBy(seed => Path.GetFileName(seed.FilePath), StringComparer.OrdinalIgnoreCase)
                .First())
            .ToList();
        // Untertitel-only-Gruppen werden nur dann eigene Batch-Einstiege, wenn es keine
        // Video- oder AD-Datei derselben erkannten Episode gibt. Sonst würde ein einzelner
        // verwaister Untertitel trotz gemeinsamer Episodenidentität als zweite GUI-Zeile
        // erscheinen, statt beim eigentlichen Episodeneintrag mitgeplant zu werden.
        var subtitleOnlyEntrySeeds = BuildEpisodeSeedGroups(subtitleOnlySeeds
                .Where(seed => !candidateSeeds.Any(candidateSeed => SeedsBelongToSameEpisode(candidateSeed, seed)))
                .ToList())
            .Select(group => group
                .OrderBy(seed => Path.GetFileName(seed.FilePath), StringComparer.OrdinalIgnoreCase)
                .First())
            .ToList();

        return normalEntrySeeds
            .Concat(audioDescriptionOnlySeeds)
            .Concat(subtitleOnlyEntrySeeds)
            .Select(seed => seed.FilePath)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<IReadOnlyList<CandidateSeed>> BuildEpisodeSeedGroups(IReadOnlyList<CandidateSeed> seeds)
    {
        var groups = new List<List<CandidateSeed>>();

        foreach (var seed in seeds.OrderBy(seed => Path.GetFileName(seed.FilePath), StringComparer.OrdinalIgnoreCase))
        {
            var matchingGroup = groups.FirstOrDefault(group => group.Any(existingSeed => SeedsBelongToSameEpisode(existingSeed, seed)));
            if (matchingGroup is null)
            {
                groups.Add([seed]);
                continue;
            }

            matchingGroup.Add(seed);
        }

        return groups;
    }

    private static bool SeedsBelongToSameEpisode(CandidateSeed left, CandidateSeed right)
    {
        if (left.Identity.Matches(right.Identity))
        {
            return true;
        }

        if (ShouldMergeSupplementSeedWithEquivalentEpisode(left, right))
        {
            return true;
        }

        if (!HaveSameSeriesAndTitle(left.Identity, right.Identity)
            || !HasAmbiguousMediathekEpisodeCodePair(left.Identity, right.Identity))
        {
            return false;
        }

        // "Die Toten vom Bodensee" liefert dieselbe Folge teils mit TVDB-nahem Code
        // und teils mit jahrgangsbasiertem Mediathek-Code. Damit solche Varianten nicht
        // mit echten Wiederholungen gleichen Titels kollidieren, reicht der Titel allein
        // nicht: die deklarierte TXT-Laufzeit muss ebenfalls praktisch identisch sein.
        return HaveCompatibleDeclaredDurations(left.TextMetadata.Duration, right.TextMetadata.Duration);
    }

    private static bool ShouldMergeSupplementSeedWithEquivalentEpisode(CandidateSeed left, CandidateSeed right)
    {
        if (!HaveSameSeriesAndTitle(left.Identity, right.Identity)
            || !HasKnownEpisodeCode(left.Identity)
            || !HasKnownEpisodeCode(right.Identity)
            || !string.Equals(left.Identity.SeasonNumber, right.Identity.SeasonNumber, StringComparison.OrdinalIgnoreCase)
            || !HaveCompatibleDeclaredDurations(left.TextMetadata.Duration, right.TextMetadata.Duration))
        {
            return false;
        }

        return IsSupplementOnlySeed(left) || IsSupplementOnlySeed(right);
    }

    private static bool HaveSameSeriesAndTitle(EpisodeIdentity left, EpisodeIdentity right)
    {
        return string.Equals(BuildSeriesIdentityKey(left.SeriesName), BuildSeriesIdentityKey(right.SeriesName), StringComparison.Ordinal)
            && string.Equals(BuildTitleIdentityKey(left.Title), BuildTitleIdentityKey(right.Title), StringComparison.Ordinal);
    }

    private static bool HasAmbiguousMediathekEpisodeCodePair(EpisodeIdentity left, EpisodeIdentity right)
    {
        if (!HasKnownEpisodeCode(left) || !HasKnownEpisodeCode(right))
        {
            return false;
        }

        if (string.Equals(left.SeasonNumber, right.SeasonNumber, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.EpisodeNumber, right.EpisodeNumber, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsYearLikeSeason(left.SeasonNumber) != IsYearLikeSeason(right.SeasonNumber);
    }

    private static bool HasKnownEpisodeCode(EpisodeIdentity identity)
    {
        return identity.SeasonNumber != "xx" && identity.EpisodeNumber != "xx";
    }

    private static bool IsSupplementOnlySeed(CandidateSeed seed)
    {
        return SupportedSubtitleExtensions.Contains(Path.GetExtension(seed.FilePath))
            || EpisodeFileNameHelper.LooksLikeAudioDescription(seed.FilePath);
    }

    private static bool IsYearLikeSeason(string seasonNumber)
    {
        return int.TryParse(seasonNumber, out var season) && season is >= 1900 and <= 2100;
    }

    private static bool HaveCompatibleDeclaredDurations(TimeSpan? left, TimeSpan? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        return Math.Abs((left.Value - right.Value).TotalSeconds) <= 120;
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
        private readonly IReadOnlyList<CandidateSeed> _subtitleOnlySeeds;
        private readonly IReadOnlyList<CandidateSeed> _metadataOnlySeeds;
        private readonly ConcurrentDictionary<string, NormalVideoCandidate> _normalVideoCandidates;
        private readonly ConcurrentDictionary<string, AudioDescriptionCandidate> _audioDescriptionCandidates;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _normalVideoCandidateGates;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _audioDescriptionCandidateGates;

        internal DirectoryDetectionContext(
            SeriesEpisodeMuxPlanner owner,
            string sourceDirectory,
            IReadOnlyDictionary<string, IReadOnlyList<string>> companionFilesByBaseName,
            IReadOnlyList<CandidateSeed> candidateSeeds,
            IReadOnlyList<CandidateSeed> subtitleOnlySeeds,
            IReadOnlyList<CandidateSeed> metadataOnlySeeds)
        {
            _owner = owner;
            SourceDirectory = sourceDirectory;
            _companionFilesByBaseName = companionFilesByBaseName.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
            _candidateSeedsByPath = candidateSeeds
                .Concat(subtitleOnlySeeds)
                .Concat(metadataOnlySeeds)
                .ToDictionary(seed => seed.FilePath, seed => seed, StringComparer.OrdinalIgnoreCase);
            _candidateSeeds = candidateSeeds.ToList();
            _subtitleOnlySeeds = subtitleOnlySeeds.ToList();
            _metadataOnlySeeds = metadataOnlySeeds.ToList();
            _normalVideoCandidates = new ConcurrentDictionary<string, NormalVideoCandidate>(StringComparer.OrdinalIgnoreCase);
            _audioDescriptionCandidates = new ConcurrentDictionary<string, AudioDescriptionCandidate>(StringComparer.OrdinalIgnoreCase);
            _normalVideoCandidateGates = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
            _audioDescriptionCandidateGates = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
            MainVideoFiles = BuildBatchEntryFiles(candidateSeeds, subtitleOnlySeeds);
        }

        /// <summary>
        /// Ursprünglicher Quellordner, aus dem dieser Kontext vorbereitet wurde.
        /// </summary>
        public string SourceDirectory { get; }

        /// <summary>
        /// Beim Vorbereiten erkannte Batch-Einstiegsdateien: reguläre Hauptvideos sowie Zusatzmaterial-only-Fälle ohne passende Hauptvideoquelle.
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
                .Where(seed => SeedsBelongToSameEpisode(selectedSeed, seed))
                .ToList();
            var matchingSubtitleOnlySeeds = _subtitleOnlySeeds
                .Where(seed => SeedsBelongToSameEpisode(selectedSeed, seed))
                .ToList();
            var matchingMetadataOnlySeeds = _metadataOnlySeeds
                .Where(seed => SeedsBelongToSameEpisode(selectedSeed, seed))
                .ToList();

            if (matchingSeeds.Count > 0 || matchingSubtitleOnlySeeds.Count > 0 || matchingMetadataOnlySeeds.Count > 0)
            {
                if (!matchingSeeds.Any(seed => PathComparisonHelper.AreSamePath(seed.FilePath, selectedSeed.FilePath))
                    && !matchingSubtitleOnlySeeds.Any(seed => PathComparisonHelper.AreSamePath(seed.FilePath, selectedSeed.FilePath))
                    && !matchingMetadataOnlySeeds.Any(seed => PathComparisonHelper.AreSamePath(seed.FilePath, selectedSeed.FilePath)))
                {
                    if (SupportedSubtitleExtensions.Contains(Path.GetExtension(selectedSeed.FilePath)))
                    {
                        matchingSubtitleOnlySeeds.Insert(0, selectedSeed);
                    }
                    else if (Path.GetExtension(selectedSeed.FilePath).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        matchingMetadataOnlySeeds.Insert(0, selectedSeed);
                    }
                    else
                    {
                        matchingSeeds.Insert(0, selectedSeed);
                    }
                }

                return new EpisodeSeedCollection(
                    matchingSeeds,
                    matchingSeeds.Where(seed => !EpisodeFileNameHelper.LooksLikeAudioDescription(seed.FilePath)).ToList(),
                    matchingSeeds.Where(seed => EpisodeFileNameHelper.LooksLikeAudioDescription(seed.FilePath)).ToList(),
                    matchingSubtitleOnlySeeds,
                    matchingMetadataOnlySeeds);
            }

            return new EpisodeSeedCollection(
                [selectedSeed],
                EpisodeFileNameHelper.LooksLikeAudioDescription(selectedSeed.FilePath) || SupportedSubtitleExtensions.Contains(Path.GetExtension(selectedSeed.FilePath)) ? [] : [selectedSeed],
                EpisodeFileNameHelper.LooksLikeAudioDescription(selectedSeed.FilePath) ? [selectedSeed] : [],
                SupportedSubtitleExtensions.Contains(Path.GetExtension(selectedSeed.FilePath)) ? [selectedSeed] : [],
                Path.GetExtension(selectedSeed.FilePath).Equals(".txt", StringComparison.OrdinalIgnoreCase) ? [selectedSeed] : []);
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
