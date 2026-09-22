using System.Text.RegularExpressions;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Bestimmt, welche Quelldateien nach einem erfolgreichen Lauf gefahrlos in den Done-Ordner verschoben werden dürfen.
/// </summary>
internal sealed class EpisodeCleanupFilePlanner
{
    private static readonly Regex CompanionSuffixPattern = new(
        @"^(?:\.(?:de|deu|ger|en|eng|nds|fr|fra|fre|es|spa|it|ita|nl|nld|dut|sv|swe|da|dan|no|nor|fi|fin|pl|pol|pt|por|tr|tur|forced|sdh|cc|hoh|hi))*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> CompanionExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
        ".srt",
        ".ass",
        ".vtt",
        ".ttml"
    };

    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".mkv",
        ".mov",
        ".webm",
        ".avi",
        ".m4v"
    };

    private readonly EpisodeOutputPathService _outputPaths;

    public EpisodeCleanupFilePlanner(EpisodeOutputPathService outputPaths)
    {
        _outputPaths = outputPaths;
    }

    /// <summary>
    /// Filtert eine Kandidatenliste auf die Dateien, die nach einem erfolgreichen Lauf gefahrlos verschoben werden dürfen.
    /// </summary>
    /// <param name="candidatePaths">Mögliche Quell- und Begleitdateien der Episode.</param>
    /// <param name="outputPath">Zieldatei des aktuellen Mux-Laufs.</param>
    /// <param name="workingCopyPath">Optionaler Pfad einer temporären Arbeitskopie, die nicht verschoben werden darf.</param>
    /// <param name="sourceRoot">Optionaler Quellwurzelpfad, auf den die Kandidaten eingeschränkt werden.</param>
    /// <param name="excludedSourcePaths">Optional bewusst ausgeschlossene Quellen, deren Begleitdateien nicht aufgeräumt werden dürfen.</param>
    /// <returns>Bereinigte Liste der tatsächlich verschiebbaren Dateien.</returns>
    public List<string> BuildCleanupFileList(
        IEnumerable<string> candidatePaths,
        string outputPath,
        string? workingCopyPath = null,
        string? sourceRoot = null,
        IEnumerable<string>? excludedSourcePaths = null)
    {
        var exclusions = BuildCleanupExclusions(excludedSourcePaths);

        return candidatePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Where(path => string.IsNullOrWhiteSpace(sourceRoot)
                || PathComparisonHelper.IsPathWithinRoot(path, sourceRoot))
            .Where(path => !_outputPaths.IsArchivePath(path))
            .Where(path => !PathComparisonHelper.AreSamePath(path, outputPath))
            .Where(path => string.IsNullOrWhiteSpace(workingCopyPath)
                || !PathComparisonHelper.AreSamePath(path, workingCopyPath))
            .Where(path => !IsExcludedCleanupCandidate(path, exclusions))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Baut Cleanup-Kandidaten für Quellen, die im Pflichtcheck ausdrücklich als nicht
    /// in Ordnung verworfen wurden. Anders als normale Excludes dürfen diese Dateien
    /// nach erfolgreichem Ersatz-Mux aufgeräumt werden, inklusive direkter Sidecars
    /// mit demselben Basenamen.
    /// </summary>
    /// <param name="rejectedSourcePaths">Im Review verworfene Medienquellen.</param>
    /// <param name="outputPath">Zieldatei des aktuellen Mux-Laufs.</param>
    /// <param name="workingCopyPath">Optionaler Pfad einer temporären Arbeitskopie, die nicht verschoben werden darf.</param>
    /// <param name="sourceRoot">Optionaler Quellwurzelpfad, auf den die Kandidaten eingeschränkt werden.</param>
    /// <returns>Bereinigte Liste der verschiebbaren abgelehnten Quellen und Begleitdateien.</returns>
    public List<string> BuildRejectedSourceCleanupFileList(
        IEnumerable<string> rejectedSourcePaths,
        string outputPath,
        string? workingCopyPath = null,
        string? sourceRoot = null)
    {
        // Zuerst die Medienquellen selbst schützen. Sonst würden bei einem versehentlich
        // als verworfen markierten Output zwar die MKV, aber nicht ihre Sidecars herausfallen.
        var safeRejectedSources = BuildCleanupFileList(rejectedSourcePaths, outputPath, workingCopyPath, sourceRoot);
        return BuildCleanupFileList(
            ExpandRejectedSourceCleanupCandidates(safeRejectedSources),
            outputPath,
            workingCopyPath,
            sourceRoot,
            excludedSourcePaths: null);
    }

    private static IEnumerable<string> ExpandRejectedSourceCleanupCandidates(IEnumerable<string> rejectedSourcePaths)
    {
        foreach (var rejectedSourcePath in rejectedSourcePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            yield return rejectedSourcePath;

            if (!MediaExtensions.Contains(Path.GetExtension(rejectedSourcePath)))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(rejectedSourcePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var stem = Path.GetFileNameWithoutExtension(rejectedSourcePath);
            foreach (var companionExtension in CompanionExtensions)
            {
                yield return Path.Combine(directory, stem + companionExtension);
            }

            if (Directory.Exists(directory))
            {
                foreach (var candidate in Directory.EnumerateFiles(directory, stem + ".*", SearchOption.TopDirectoryOnly))
                {
                    if (IsCompanion(candidate, stem))
                    {
                        yield return candidate;
                    }
                }
            }
        }
    }

    private static bool IsCompanion(string candidatePath, string sourceStem)
    {
        var candidateStem = Path.GetFileNameWithoutExtension(candidatePath);
        return CompanionExtensions.Contains(Path.GetExtension(candidatePath))
            && candidateStem.StartsWith(sourceStem, StringComparison.OrdinalIgnoreCase)
            && CompanionSuffixPattern.IsMatch(candidateStem[sourceStem.Length..]);
    }

    private static IReadOnlyList<CleanupExclusion> BuildCleanupExclusions(IEnumerable<string>? excludedSourcePaths)
    {
        return excludedSourcePaths?
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => new CleanupExclusion(path))
            .ToArray() ?? [];
    }

    private static bool IsExcludedCleanupCandidate(string candidatePath, IReadOnlyList<CleanupExclusion> exclusions)
    {
        if (exclusions.Count == 0)
        {
            return false;
        }

        return exclusions.Any(exclusion => exclusion.Matches(candidatePath));
    }

    private sealed class CleanupExclusion
    {
        private readonly string _sourcePath;
        private readonly string? _sourceDirectory;
        private readonly string _sourceStem;
        private readonly bool _sourceCanOwnCompanions;

        public CleanupExclusion(string sourcePath)
        {
            _sourcePath = Path.GetFullPath(sourcePath);
            _sourceDirectory = Path.GetDirectoryName(_sourcePath);
            _sourceStem = Path.GetFileNameWithoutExtension(_sourcePath);
            _sourceCanOwnCompanions = MediaExtensions.Contains(Path.GetExtension(sourcePath));
        }

        public bool Matches(string candidatePath)
        {
            if (PathComparisonHelper.AreSamePath(candidatePath, _sourcePath))
            {
                return true;
            }

            if (!_sourceCanOwnCompanions || string.IsNullOrWhiteSpace(_sourceDirectory))
            {
                return false;
            }

            return IsCompanion(candidatePath, _sourceStem)
                && PathComparisonHelper.AreSamePath(Path.GetDirectoryName(candidatePath), _sourceDirectory);
        }
    }
}
