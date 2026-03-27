namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Bestimmt, welche Quelldateien nach einem erfolgreichen Lauf gefahrlos in den Done-Ordner verschoben werden dürfen.
/// </summary>
public sealed class EpisodeCleanupFilePlanner
{
    private readonly EpisodeOutputPathService _outputPaths;

    public EpisodeCleanupFilePlanner(EpisodeOutputPathService outputPaths)
    {
        _outputPaths = outputPaths;
    }

    public List<string> BuildCleanupFileList(
        IEnumerable<string> candidatePaths,
        string outputPath,
        string? workingCopyPath = null,
        string? sourceRoot = null)
    {
        return candidatePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(File.Exists)
            .Where(path => string.IsNullOrWhiteSpace(sourceRoot)
                || PathComparisonHelper.IsPathWithinRoot(path, sourceRoot))
            .Where(path => !_outputPaths.IsArchivePath(path))
            .Where(path => !string.Equals(path, outputPath, StringComparison.OrdinalIgnoreCase))
            .Where(path => string.IsNullOrWhiteSpace(workingCopyPath)
                || !string.Equals(path, workingCopyPath, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
