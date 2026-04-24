namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Bestimmt, welche Quelldateien nach einem erfolgreichen Lauf gefahrlos in den Done-Ordner verschoben werden dürfen.
/// </summary>
internal sealed class EpisodeCleanupFilePlanner
{
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

        private readonly string _sourcePath;
        private readonly string? _sourceDirectory;
        private readonly string _sourceStem;
        private readonly bool _sourceCanOwnCompanions;

        public CleanupExclusion(string sourcePath)
        {
            _sourcePath = sourcePath;
            _sourceDirectory = Path.GetDirectoryName(sourcePath);
            _sourceStem = Path.GetFileNameWithoutExtension(sourcePath);
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

            return CompanionExtensions.Contains(Path.GetExtension(candidatePath))
                && PathComparisonHelper.AreSamePath(Path.GetDirectoryName(candidatePath), _sourceDirectory)
                && string.Equals(Path.GetFileNameWithoutExtension(candidatePath), _sourceStem, StringComparison.OrdinalIgnoreCase);
        }
    }
}
