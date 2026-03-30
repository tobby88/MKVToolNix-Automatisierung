namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Vereinheitlicht die im Projekt relevanten Sprachcodes für MKV-Spuren und lesbare Tracknamen.
/// </summary>
internal static class MediaLanguageHelper
{
    /// <summary>
    /// Reduziert erkannte Sprachangaben auf die im Projekt bewusst unterstützten MKV-Codes.
    /// Unbekannte oder fehlende Werte fallen absichtlich auf <c>de</c> zurück, weil die Mediathek deutschzentriert ist.
    /// </summary>
    /// <param name="languageCode">Rohwert aus Tool-Metadaten oder externer Erkennung.</param>
    /// <returns>Normalisierter MKV-Sprachcode für den finalen Mux.</returns>
    public static string NormalizeMuxLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return "de";
        }

        var normalized = languageCode.Trim().ToLowerInvariant().Replace('_', '-');
        if (normalized is "de" or "deu" or "ger" || normalized.StartsWith("de-", StringComparison.Ordinal))
        {
            return "de";
        }

        if (normalized is "nds" or "nds-de" || normalized.StartsWith("nds-", StringComparison.Ordinal))
        {
            return "nds";
        }

        if (normalized is "en" or "eng" || normalized.StartsWith("en-", StringComparison.Ordinal))
        {
            return "en";
        }

        return "de";
    }

    /// <summary>
    /// Liefert den zur normalisierten Sprache passenden lesbaren Tracknamen für die GUI und mkvmerge-Metadaten.
    /// </summary>
    /// <param name="languageCode">Rohwert oder bereits normalisierter Sprachcode.</param>
    /// <returns>Projektweit verwendeter Anzeigename der Sprache.</returns>
    public static string GetLanguageDisplayName(string? languageCode)
    {
        return NormalizeMuxLanguageCode(languageCode) switch
        {
            "nds" => "Plattdeutsch",
            "en" => "Englisch",
            _ => "Deutsch"
        };
    }
}
