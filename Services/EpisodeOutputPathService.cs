namespace MkvToolnixAutomatisierung.Services;

public sealed class EpisodeOutputPathService
{
    private readonly SeriesArchiveService _archiveService;

    public EpisodeOutputPathService(SeriesArchiveService archiveService)
    {
        _archiveService = archiveService;
    }

    public string BuildOutputPath(
        string fallbackDirectory,
        string seriesName,
        string seasonNumber,
        string episodeNumber,
        string title,
        string? outputRootOverride = null)
    {
        var suggestedOutputPath = _archiveService.BuildSuggestedOutputPath(
            fallbackDirectory,
            seriesName,
            seasonNumber,
            episodeNumber,
            title);

        if (string.IsNullOrWhiteSpace(outputRootOverride))
        {
            return suggestedOutputPath;
        }

        if (IsArchivePath(suggestedOutputPath))
        {
            if (string.Equals(outputRootOverride, SeriesArchiveService.ArchiveRootDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return suggestedOutputPath;
            }

            var relativePath = Path.GetRelativePath(SeriesArchiveService.ArchiveRootDirectory, suggestedOutputPath);
            return Path.Combine(outputRootOverride, relativePath);
        }

        return Path.Combine(outputRootOverride, Path.GetFileName(suggestedOutputPath));
    }

    public bool IsArchivePath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && path.StartsWith(SeriesArchiveService.ArchiveRootDirectory, StringComparison.OrdinalIgnoreCase);
    }
}
