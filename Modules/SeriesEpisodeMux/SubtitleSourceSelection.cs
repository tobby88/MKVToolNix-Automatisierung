namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

/// <summary>
/// Verdichtet externe Untertitelquellen auf die fachlich darstellbaren Slots.
/// </summary>
internal static class SubtitleSourceSelection
{
    /// <summary>
    /// Behält pro Untertiteltyp genau den zuerst angebotenen Pfad.
    /// </summary>
    /// <param name="paths">
    /// Nach fachlicher Präferenz sortierte Pfade. Der erste Pfad eines Typs gewinnt.
    /// </param>
    /// <returns>Eine deterministisch nach Typ und Pfad sortierte Auswahl ohne doppelte Slots.</returns>
    public static IReadOnlyList<string> SelectPreferredPathsByKind(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var selectedPathsByKind = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var kind = SubtitleKind.FromExtension(Path.GetExtension(path));
            selectedPathsByKind.TryAdd(kind.DisplayName, path);
        }

        return selectedPathsByKind.Values
            .OrderBy(path => SubtitleKind.FromExtension(Path.GetExtension(path)).SortRank)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
