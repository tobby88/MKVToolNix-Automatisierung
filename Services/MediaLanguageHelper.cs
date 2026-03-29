namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Vereinheitlicht die im Projekt relevanten Sprachcodes für MKV-Spuren und lesbare Tracknamen.
/// </summary>
internal static class MediaLanguageHelper
{
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
