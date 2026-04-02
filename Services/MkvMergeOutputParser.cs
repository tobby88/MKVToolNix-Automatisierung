using System.Text.RegularExpressions;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Übersetzt mkvmerge-Konsolenzeilen in strukturierte Fortschritts- und Warnereignisse.
/// </summary>
public sealed class MkvMergeOutputParser
{
    private static readonly Regex NamedProgressRegex = new(
        @"\b(?:Fortschritt|Progress)\b\s*:\s*(?<percent>\d{1,3})%",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Übersetzt eine einzelne mkvmerge-Konsolenzeile in Fortschritts- und Warninformationen.
    /// </summary>
    /// <param name="line">Rohzeile aus Standardausgabe oder Standardfehler von mkvmerge.</param>
    /// <returns>Strukturiertes Statusereignis für GUI und Logik.</returns>
    public MkvMergeOutputEvent Parse(string line)
    {
        var progressPercent = TryReadProgressPercent(line);
        var isWarning = line.Contains("Warnung:", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Warning:", StringComparison.OrdinalIgnoreCase);

        return new MkvMergeOutputEvent(progressPercent, isWarning);
    }

    private static int? TryReadProgressPercent(string line)
    {
        var match = NamedProgressRegex.Match(line);

        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups["percent"].Value, out var progressPercent)
            ? Math.Clamp(progressPercent, 0, 100)
            : null;
    }
}

/// <summary>
/// Einzelnes, aus der Prozessausgabe abgeleitetes Statusereignis.
/// </summary>
public sealed record MkvMergeOutputEvent(int? ProgressPercent, bool IsWarning);
