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
    /// Erkennt fachliche Sprachhinweise aus Mediathek-Dateinamen, TXT-Titeln oder vorhandenen Tracknamen.
    /// </summary>
    /// <remarks>
    /// Diese Heuristik ist bewusst eng gehalten: Für die realen Mediathek-Fälle ist vor allem
    /// <c>op Platt</c> wichtig, weil betroffene MP4-Container gelegentlich nur generische oder
    /// falsche Sprachflags liefern. Ohne diesen Override würde Plattdeutsch als Deutsch oder
    /// Englisch in falsche Archivslots einsortiert.
    /// </remarks>
    /// <param name="values">Zu prüfende Rohtexte, z. B. Dateiname, TXT-Titel oder Trackname.</param>
    /// <returns>Ein projektweit normalisierter Sprachcode oder <see langword="null"/>, wenn kein sicherer Hinweis gefunden wurde.</returns>
    public static string? TryInferMuxLanguageCodeFromText(params string?[] values)
    {
        var combined = string.Join(
            " ",
            values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => MojibakeRepair.NormalizeLikelyMojibake(value!).Trim()));
        if (string.IsNullOrWhiteSpace(combined))
        {
            return null;
        }

        var normalized = combined.ToLowerInvariant();
        if (normalized.Contains("op platt", StringComparison.Ordinal)
            || normalized.Contains("plattdüütsch", StringComparison.Ordinal)
            || normalized.Contains("plattdeutsch", StringComparison.Ordinal)
            || normalized.Contains("plattduetsch", StringComparison.Ordinal))
        {
            return "nds";
        }

        return null;
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
            // Tracknamen sollen die Sprache jeweils in ihrer eigenen Bezeichnung tragen,
            // damit Mehrspuren-Sets sprachlich konsistent und direkt lesbar bleiben.
            "nds" => "Plattdüütsch",
            "en" => "English",
            _ => "Deutsch"
        };
    }

    /// <summary>
    /// Liefert die projektweit gewünschte Sortierreihenfolge für Videosprachen.
    /// Deutsch steht vor Plattdüütsch und English; unbekannte Werte landen wegen der Normalisierung ebenfalls bei Deutsch.
    /// </summary>
    /// <param name="languageCode">Rohwert oder bereits normalisierter Sprachcode.</param>
    /// <returns>Kleinerer Wert bedeutet höhere Priorität in Mehrspuren-Video-Sets.</returns>
    public static int GetLanguageSortRank(string? languageCode)
    {
        return NormalizeMuxLanguageCode(languageCode) switch
        {
            "de" => 0,
            "nds" => 1,
            "en" => 2,
            _ => 9
        };
    }
}
