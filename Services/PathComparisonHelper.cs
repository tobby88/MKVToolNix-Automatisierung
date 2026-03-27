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

    private static string AppendDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
