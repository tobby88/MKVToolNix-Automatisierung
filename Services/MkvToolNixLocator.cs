namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Findet die fuer das Projekt benoetigten MKVToolNix-Executables aus den gespeicherten Toolpfaden.
/// </summary>
public sealed class MkvToolNixLocator : IMkvToolNixLocator
{
    private const string DownloadsFolderName = "Downloads";
    private const string DirectoryPrefix = "mkvtoolnix-64-bit-";
    private const string RelativeToolDirectory = "mkvtoolnix";
    private readonly AppToolPathStore _toolPathStore;

    /// <summary>
    /// Initialisiert den MKVToolNix-Locator mit dem standardmäßigen Toolpfad-Store.
    /// </summary>
    public MkvToolNixLocator()
        : this(new AppToolPathStore())
    {
    }

    /// <summary>
    /// Initialisiert den MKVToolNix-Locator mit einem expliziten Toolpfad-Store.
    /// </summary>
    /// <param name="toolPathStore">Persistente Quelle für manuell gesetzte Toolpfade.</param>
    public MkvToolNixLocator(AppToolPathStore toolPathStore)
    {
        _toolPathStore = toolPathStore;
    }

    /// <summary>
    /// Ermittelt den Pfad zur verwendbaren <c>mkvmerge.exe</c> aus Settings oder Download-Ordnern.
    /// </summary>
    /// <returns>Vollständiger Pfad zur auszuführenden mkvmerge-Executable.</returns>
    public string FindMkvMergePath()
    {
        return FindToolPath("mkvmerge.exe");
    }

    /// <summary>
    /// Ermittelt den Pfad zur verwendbaren <c>mkvpropedit.exe</c> aus Settings oder Download-Ordnern.
    /// </summary>
    /// <returns>Vollständiger Pfad zur auszuführenden mkvpropedit-Executable.</returns>
    public string FindMkvPropEditPath()
    {
        return FindToolPath("mkvpropedit.exe");
    }

    private string FindToolPath(string executableName)
    {
        var configuredPath = _toolPathStore.Load().MkvToolNixDirectoryPath;
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            foreach (var configuredExecutable in EnumerateConfiguredExecutableCandidates(configuredPath, executableName))
            {
                if (File.Exists(configuredExecutable))
                {
                    return configuredExecutable;
                }
            }
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloadsDirectory = Path.Combine(userProfile, DownloadsFolderName);

        if (!Directory.Exists(downloadsDirectory))
        {
            throw new DirectoryNotFoundException($"Download-Ordner nicht gefunden: {downloadsDirectory}");
        }

        var candidate = Directory
            .GetDirectories(downloadsDirectory, $"{DirectoryPrefix}*")
            .Select(path => new DirectoryInfo(path))
            .OrderByDescending(directory => directory.LastWriteTimeUtc)
            .Select(directory => Path.Combine(directory.FullName, RelativeToolDirectory, executableName))
            .FirstOrDefault(File.Exists);

        if (candidate is null)
        {
            throw new FileNotFoundException($"Es wurde keine {executableName} in einem mkvtoolnix-Download-Ordner gefunden.");
        }

        return candidate;
    }

    private static IEnumerable<string> EnumerateConfiguredExecutableCandidates(string configuredPath, string executableName)
    {
        if (configuredPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(Path.GetFileName(configuredPath), executableName, StringComparison.OrdinalIgnoreCase))
            {
                yield return configuredPath;
                yield break;
            }

            if (string.Equals(executableName, "mkvmerge.exe", StringComparison.OrdinalIgnoreCase))
            {
                // Bestehende Konfigurationen und Test-Setups dürfen weiterhin direkt auf eine
                // mkvmerge-kompatible Executable zeigen, auch wenn sie nicht exakt so heißt.
                yield return configuredPath;
            }

            var configuredDirectory = Path.GetDirectoryName(configuredPath);
            if (!string.IsNullOrWhiteSpace(configuredDirectory))
            {
                yield return Path.Combine(configuredDirectory, executableName);
            }

            yield break;
        }

        yield return Path.Combine(configuredPath, executableName);
    }
}
