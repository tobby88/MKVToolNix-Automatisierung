namespace MkvToolnixAutomatisierung.Services;

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
                || path.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase))
            .Where(path => !_outputPaths.IsArchivePath(path))
            .Where(path => !string.Equals(path, outputPath, StringComparison.OrdinalIgnoreCase))
            .Where(path => string.IsNullOrWhiteSpace(workingCopyPath)
                || !string.Equals(path, workingCopyPath, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
