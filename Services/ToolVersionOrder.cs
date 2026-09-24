using System.Text.RegularExpressions;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>Ordnet bekannte Versionsordner numerisch; Zeitstempel sind nur der Rückfall für unversionierte Builds.</summary>
internal static partial class ToolVersionOrder
{
    public static IOrderedEnumerable<DirectoryInfo> OrderByToolVersion(this IEnumerable<DirectoryInfo> directories) =>
        directories.OrderByDescending(directory => Parse(directory.Name))
            .ThenByDescending(directory => !PrereleasePattern().IsMatch(directory.Name))
            .ThenByDescending(directory => directory.LastWriteTimeUtc)
            .ThenBy(directory => directory.Name, StringComparer.OrdinalIgnoreCase);

    private static Version? Parse(string name)
    {
        var match = VersionPattern().Match(name);
        if (!match.Success || !Version.TryParse(match.Value, out var version)) return null;
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    }

    [GeneratedRegex(@"(?<!\d)\d+\.\d+(?:\.\d+){0,2}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"(?:[-_.])(?:alpha|beta|rc|preview)(?:[-_.\d]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PrereleasePattern();
}
