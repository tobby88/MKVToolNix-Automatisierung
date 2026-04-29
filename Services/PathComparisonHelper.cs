namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Vereinheitlicht robuste Pfadvergleiche, damit Root-/Relative-Checks nicht an Slash- oder Case-Unterschieden scheitern.
/// </summary>
internal static class PathComparisonHelper
{
    public static bool AreSamePath(string? left, string? right)
    {
        var normalizedLeft = NormalizePath(left);
        var normalizedRight = NormalizePath(right);
        return normalizedLeft is not null
            && normalizedRight is not null
            && string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Prüft, ob ein Zielpfad bereits durch eine andere Datei belegt ist.
    /// </summary>
    /// <remarks>
    /// Windows meldet bei reinen Groß-/Kleinschreibungs-Änderungen den Zielpfad häufig als
    /// existent, obwohl nur die Quelldatei gefunden wurde. Case-sensitive Verzeichnisse können
    /// dagegen beide Schreibweisen parallel enthalten. Deshalb wird bei Pfaden, die sich nur im
    /// Case unterscheiden, zusätzlich nach einem exakt so geschriebenen Verzeichniseintrag gesucht.
    /// </remarks>
    public static bool FileExistsAsDifferentEntry(string sourcePath, string targetPath)
    {
        if (!File.Exists(targetPath))
        {
            return false;
        }

        if (!AreSamePath(sourcePath, targetPath))
        {
            return true;
        }

        return !AreExactlySamePath(sourcePath, targetPath)
            && FileSystemEntryExistsWithExactName(targetPath);
    }

    public static bool IsPathWithinRoot(string? path, string? rootPath)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedRoot = NormalizePath(rootPath);
        if (normalizedPath is null || normalizedRoot is null)
        {
            return false;
        }

        return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(AppendDirectorySeparator(normalizedRoot), StringComparison.OrdinalIgnoreCase);
    }

    public static string? TryGetRelativePathWithinRoot(string? path, string? rootPath)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedRoot = NormalizePath(rootPath);
        if (normalizedPath is null || normalizedRoot is null || !IsPathWithinRoot(normalizedPath, normalizedRoot))
        {
            return null;
        }

        return Path.GetRelativePath(normalizedRoot, normalizedPath);
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch
        {
            return null;
        }
    }

    private static bool AreExactlySamePath(string left, string right)
    {
        var normalizedLeft = NormalizePath(left);
        var normalizedRight = NormalizePath(right);
        return normalizedLeft is not null
            && normalizedRight is not null
            && string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
    }

    private static bool FileSystemEntryExistsWithExactName(string path)
    {
        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateFileSystemEntries(directory, fileName)
                .Any(entry => string.Equals(Path.GetFileName(entry), fileName, StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static string AppendDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
