using System.Text;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Bündelt die Reparatur typischer Mojibake-Muster aus externen Metadaten und Begleittexten.
/// </summary>
internal static class MojibakeRepair
{
    private static readonly char[] SuspiciousMarkers = ['Ã', 'â', 'Â', '\uFFFD'];

    /// <summary>
    /// Repariert offensichtliche UTF-8-/Windows-1252-/Latin-1-Fehlinterpretationen, ohne saubere Texte anzufassen.
    /// </summary>
    /// <param name="value">Zu normalisierender Text.</param>
    /// <returns>Wenn möglich bereinigter Text, sonst der ursprüngliche Eingabewert.</returns>
    public static string NormalizeLikelyMojibake(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !LooksLikeMojibake(value))
        {
            return value;
        }

        var repairedCommonSequences = RepairCommonSequences(value);
        if (!LooksLikeMojibake(repairedCommonSequences))
        {
            return repairedCommonSequences;
        }

        var windows1252 = TryGetWindows1252Encoding();
        var candidates = new[]
        {
            TryRepairWithEncoding(value, windows1252),
            TryRepairWithEncoding(value, Encoding.Latin1),
            TryRepairWithEncoding(repairedCommonSequences, windows1252),
            TryRepairWithEncoding(repairedCommonSequences, Encoding.Latin1)
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && !LooksLikeMojibake(candidate))
            {
                return candidate;
            }
        }

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return repairedCommonSequences;
    }

    /// <summary>
    /// Prüft auf Marker, die in diesem Projekt typischerweise auf fehlgedeutete UTF-8-Metadaten hindeuten.
    /// </summary>
    /// <param name="value">Zu prüfender Text.</param>
    /// <returns><see langword="true"/>, wenn der Text nach Mojibake aussieht.</returns>
    public static bool LooksLikeMojibake(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.IndexOfAny(SuspiciousMarkers) >= 0;
    }

    private static string RepairCommonSequences(string value)
    {
        return value
            .Replace("Ã¤", "ä", StringComparison.Ordinal)
            .Replace("Ã¶", "ö", StringComparison.Ordinal)
            .Replace("Ã¼", "ü", StringComparison.Ordinal)
            .Replace("Ã„", "Ä", StringComparison.Ordinal)
            .Replace("Ã–", "Ö", StringComparison.Ordinal)
            .Replace("Ãœ", "Ü", StringComparison.Ordinal)
            .Replace("ÃŸ", "ß", StringComparison.Ordinal)
            .Replace("Ãž", "ß", StringComparison.Ordinal);
    }

    private static string? TryRepairWithEncoding(string value, Encoding? encoding)
    {
        if (encoding is null)
        {
            return null;
        }

        try
        {
            var repaired = Encoding.UTF8.GetString(encoding.GetBytes(value));
            return string.IsNullOrWhiteSpace(repaired) ? null : repaired;
        }
        catch
        {
            return null;
        }
    }

    private static Encoding? TryGetWindows1252Encoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252);
        }
        catch
        {
            return null;
        }
    }
}
