using System.Text.RegularExpressions;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>Gemeinsamer strikter Vertrag für manuelle Trackwerte und Werkzeugargumente.</summary>
internal static class TrackHeaderValueValidation
{
    private static readonly HashSet<string> Flags = ["flag-default", "flag-forced", "flag-original", "flag-visual-impaired", "flag-hearing-impaired"];

    public static string FlagToRaw(string value) => value.Trim().ToLowerInvariant() switch
    {
        "ja" or "yes" or "true" or "1" => "1",
        "nein" or "no" or "false" or "0" => "0",
        _ => throw new ArgumentException("Flagwerte müssen Ja oder Nein sein.", nameof(value))
    };

    public static void Validate(string propertyName, string value)
    {
        if (value.Contains('\0')) throw new ArgumentException("Trackwerte dürfen kein NUL-Zeichen enthalten.");
        if (Flags.Contains(propertyName))
        {
            if (value is not ("0" or "1")) throw new ArgumentException($"Ungültiger Flagwert für {propertyName}: {value}");
        }
        else if (propertyName == "language")
        {
            // ISO-639 mit optionalen BCP-47-Subtags. Fachlich unbekannte gültige Sprachen
            // werden nicht still auf Deutsch reduziert; mkvpropedit prüft die Registry.
            if (!Regex.IsMatch(value, @"^[a-zA-Z]{2,3}(?:-[a-zA-Z0-9]{2,8})*$", RegexOptions.CultureInvariant))
                throw new ArgumentException("Die Sprache muss ein ISO-/BCP-47-Code sein, z.B. de oder pt-BR.");
        }
        else if (propertyName != "name") throw new ArgumentException($"Nicht unterstützte Track-Property: {propertyName}");
    }

    public static void ValidateSelector(string selector)
    {
        if (!Regex.IsMatch(selector, @"^track:(?:[avs]?[1-9][0-9]*|=[1-9][0-9]*)$", RegexOptions.CultureInvariant))
            throw new ArgumentException($"Ungültiger Track-Selektor: {selector}");
    }
}
