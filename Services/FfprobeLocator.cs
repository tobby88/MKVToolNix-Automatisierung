namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Sucht eine benutzbare ffprobe.exe aus Settings oder naheliegenden Standardpfaden.
/// </summary>
public sealed class FfprobeLocator : IFfprobeLocator
{
    private const string DownloadsFolderName = "Downloads";
    private const string ExecutableName = "ffprobe.exe";
    private readonly AppToolPathStore _toolPathStore;

    /// <summary>
    /// Initialisiert den ffprobe-Locator mit dem standardmäßigen Toolpfad-Store.
    /// </summary>
    public FfprobeLocator()
        : this(new AppToolPathStore())
    {
    }

    /// <summary>
    /// Initialisiert den ffprobe-Locator mit einem expliziten Toolpfad-Store.
    /// </summary>
    /// <param name="toolPathStore">Persistente Quelle für manuell gesetzte Toolpfade.</param>
    public FfprobeLocator(AppToolPathStore toolPathStore)
    {
        _toolPathStore = toolPathStore;
    }

    /// <inheritdoc />
    public string? TryFindFfprobePath()
    {
        var settings = _toolPathStore.Load();
        var configuredPath = settings.FfprobePath;
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var managedPath = settings.ManagedFfprobe.InstalledPath;
        if (!string.IsNullOrWhiteSpace(managedPath) && File.Exists(managedPath))
        {
            return managedPath;
        }

        var fromPath = TryFindInPath();
        if (!string.IsNullOrWhiteSpace(fromPath))
        {
            return fromPath;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloadsDirectory = Path.Combine(userProfile, DownloadsFolderName);
        if (!Directory.Exists(downloadsDirectory))
        {
            return null;
        }

        return Directory
            .EnumerateDirectories(downloadsDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path).Contains("ffmpeg", StringComparison.OrdinalIgnoreCase))
            .Select(path => new DirectoryInfo(path))
            .OrderByDescending(directory => directory.LastWriteTimeUtc)
            .SelectMany(FindCandidatesInDirectory)
            .FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> FindCandidatesInDirectory(DirectoryInfo directory)
    {
        var directCandidates = new[]
        {
            Path.Combine(directory.FullName, "bin", ExecutableName),
            Path.Combine(directory.FullName, ExecutableName)
        };

        foreach (var candidate in directCandidates)
        {
            yield return candidate;
        }

        IEnumerable<string> recursiveCandidates;

        try
        {
            recursiveCandidates = Directory
                .EnumerateFiles(directory.FullName, ExecutableName, SearchOption.AllDirectories)
                .OrderBy(path => path.Count(character => character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar))
                .ThenBy(path => path.Length);
        }
        catch
        {
            yield break;
        }

        foreach (var candidate in recursiveCandidates)
        {
            yield return candidate;
        }
    }

    private static string? TryFindInPath()
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var rawPath in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(rawPath, ExecutableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }
}
