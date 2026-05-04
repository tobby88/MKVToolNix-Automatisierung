using System.Text.RegularExpressions;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Bereinigt sichtbare UI-Protokolle vor der Dateipersistenz um reine Fortschritts- und Werkzeug-Routinezeilen.
/// </summary>
internal static partial class PersistedLogTextCleaner
{
    private static readonly HashSet<string> RoutineToolLines = new(StringComparer.OrdinalIgnoreCase)
    {
        "The file is being analyzed.",
        "The changes are written to the file.",
        "Done.",
        "Die Datei wird analysiert.",
        "Die Änderungen werden in die Datei geschrieben.",
        "Fertig."
    };

    /// <summary>
    /// Normalisiert Encoding-Artefakte und entfernt Zeilen, die für spätere Fehlersuche keinen Mehrwert haben.
    /// </summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(MojibakeRepair.NormalizeLikelyMojibake)
            .Where(IsPersistableLine);

        return string.Join(Environment.NewLine, lines);
    }

    private static bool IsPersistableLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmedLine = line.Trim();
        return !RoutineToolLines.Contains(trimmedLine)
               && !PureProgressLineRegex().IsMatch(trimmedLine)
               && !BarePercentLineRegex().IsMatch(trimmedLine);
    }

    [GeneratedRegex(@"^(?:Fortschritt|Progress)\s*:\s*\d{1,3}%$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PureProgressLineRegex();

    [GeneratedRegex(@"^\d{1,3}%$", RegexOptions.CultureInvariant)]
    private static partial Regex BarePercentLineRegex();
}
