using System.Xml.Linq;

namespace MkvToolnixAutomatisierung.Services.Emby;

/// <summary>
/// Liest und aktualisiert Provider-IDs in Emby-kompatiblen Episoden-NFO-Dateien neben MKV-Dateien.
/// </summary>
internal sealed class EmbyNfoProviderIdService
{
    /// <summary>
    /// Ermittelt den erwarteten NFO-Pfad direkt neben der MKV.
    /// </summary>
    /// <param name="mediaFilePath">Pfad zur MKV-Datei.</param>
    /// <returns>Pfad zur gleichnamigen <c>.nfo</c>-Datei.</returns>
    public string GetNfoPath(string mediaFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaFilePath);
        return Path.ChangeExtension(mediaFilePath, ".nfo");
    }

    /// <summary>
    /// Liest vorhandene TVDB-/IMDB-IDs aus einer NFO, ohne die Datei zu verändern.
    /// </summary>
    /// <param name="mediaFilePath">Pfad zur MKV-Datei.</param>
    /// <returns>Leseergebnis mit NFO-Pfad, Existenzstatus und gefundenen IDs.</returns>
    public EmbyNfoReadResult ReadProviderIds(string mediaFilePath)
    {
        var nfoPath = GetNfoPath(mediaFilePath);
        if (!File.Exists(nfoPath))
        {
            return new EmbyNfoReadResult(nfoPath, NfoExists: false, EmbyProviderIds.Empty, WarningMessage: null);
        }

        try
        {
            var document = XDocument.Load(nfoPath, LoadOptions.PreserveWhitespace);
            var root = document.Root;
            if (root is null)
            {
                return new EmbyNfoReadResult(nfoPath, NfoExists: true, EmbyProviderIds.Empty, "Die NFO enthält kein XML-Wurzelelement.");
            }

            return new EmbyNfoReadResult(
                nfoPath,
                NfoExists: true,
                new EmbyProviderIds(
                    ReadProviderId(root, "tvdb", "tvdbid"),
                    ReadProviderId(root, "imdb", "imdbid")),
                WarningMessage: null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return new EmbyNfoReadResult(nfoPath, NfoExists: true, EmbyProviderIds.Empty, ex.Message);
        }
    }

    /// <summary>
    /// Aktualisiert TVDB-/IMDB-IDs in einer vorhandenen NFO.
    /// </summary>
    /// <remarks>
    /// Die Methode erzeugt bewusst keine neue NFO aus dem Nichts. Der Emby-Scan soll zuerst die
    /// fachlich vollständige Episode-NFO anlegen; dieses Tool ergänzt danach nur die Provider-IDs.
    /// </remarks>
    /// <param name="mediaFilePath">Pfad zur MKV-Datei.</param>
    /// <param name="providerIds">IDs, die in der NFO stehen sollen.</param>
    /// <returns>Ergebnis mit Änderungsstatus und Hinweistext.</returns>
    public EmbyNfoUpdateResult UpdateProviderIds(string mediaFilePath, EmbyProviderIds providerIds)
    {
        var nfoPath = GetNfoPath(mediaFilePath);
        if (!File.Exists(nfoPath))
        {
            return new EmbyNfoUpdateResult(nfoPath, NfoChanged: false, Success: false, "NFO-Datei fehlt. Bitte zuerst Emby scannen lassen.");
        }

        if (!providerIds.HasAny)
        {
            return new EmbyNfoUpdateResult(nfoPath, NfoChanged: false, Success: false, "Keine TVDB- oder IMDB-ID vorhanden.");
        }

        try
        {
            var document = XDocument.Load(nfoPath, LoadOptions.PreserveWhitespace);
            var root = document.Root;
            if (root is null)
            {
                return new EmbyNfoUpdateResult(nfoPath, NfoChanged: false, Success: false, "Die NFO enthält kein XML-Wurzelelement.");
            }

            var changed = false;
            if (!string.IsNullOrWhiteSpace(providerIds.TvdbId))
            {
                changed |= SetUniqueId(root, "tvdb", providerIds.TvdbId!, isDefault: true);
                changed |= SetLegacyProviderElement(root, "tvdbid", providerIds.TvdbId!);
            }

            if (!string.IsNullOrWhiteSpace(providerIds.ImdbId))
            {
                changed |= SetUniqueId(root, "imdb", providerIds.ImdbId!, isDefault: false);
                changed |= SetLegacyProviderElement(root, "imdbid", providerIds.ImdbId!);
            }

            if (!changed)
            {
                return new EmbyNfoUpdateResult(nfoPath, NfoChanged: false, Success: true, "NFO-Provider-IDs waren bereits aktuell.");
            }

            document.Save(nfoPath);
            return new EmbyNfoUpdateResult(nfoPath, NfoChanged: true, Success: true, "NFO-Provider-IDs aktualisiert.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return new EmbyNfoUpdateResult(nfoPath, NfoChanged: false, Success: false, ex.Message);
        }
    }

    private static string? ReadProviderId(XElement root, string uniqueIdType, string legacyElementName)
    {
        var uniqueId = root
            .Elements("uniqueid")
            .FirstOrDefault(element => string.Equals(
                (string?)element.Attribute("type"),
                uniqueIdType,
                StringComparison.OrdinalIgnoreCase));
        var uniqueIdValue = uniqueId?.Value.Trim();
        if (!string.IsNullOrWhiteSpace(uniqueIdValue))
        {
            return uniqueIdValue;
        }

        var legacyValue = root.Element(legacyElementName)?.Value.Trim();
        return string.IsNullOrWhiteSpace(legacyValue) ? null : legacyValue;
    }

    private static bool SetUniqueId(XElement root, string type, string value, bool isDefault)
    {
        var matchingUniqueIds = root
            .Elements("uniqueid")
            .Where(element => string.Equals(
                (string?)element.Attribute("type"),
                type,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        var uniqueId = matchingUniqueIds.FirstOrDefault();

        var changed = false;
        if (uniqueId is null)
        {
            uniqueId = new XElement(
                "uniqueid",
                new XAttribute("type", type),
                value);
            root.Add(uniqueId);
            changed = true;
        }
        else
        {
            foreach (var duplicateUniqueId in matchingUniqueIds.Skip(1).ToList())
            {
                duplicateUniqueId.Remove();
                changed = true;
            }

            if (!string.Equals(uniqueId.Value.Trim(), value, StringComparison.Ordinal))
            {
                uniqueId.Value = value;
                changed = true;
            }
        }

        var expectedDefaultValue = isDefault ? "true" : null;
        var currentDefaultValue = (string?)uniqueId.Attribute("default");
        if (!string.Equals(currentDefaultValue, expectedDefaultValue, StringComparison.OrdinalIgnoreCase))
        {
            uniqueId.SetAttributeValue("default", expectedDefaultValue);
            changed = true;
        }

        return changed;
    }

    private static bool SetLegacyProviderElement(XElement root, string elementName, string value)
    {
        var matchingElements = root.Elements(elementName).ToList();
        var element = matchingElements.FirstOrDefault();
        if (element is null)
        {
            root.Add(new XElement(elementName, value));
            return true;
        }

        var changed = false;
        foreach (var duplicateElement in matchingElements.Skip(1).ToList())
        {
            duplicateElement.Remove();
            changed = true;
        }

        if (string.Equals(element.Value.Trim(), value, StringComparison.Ordinal))
        {
            return changed;
        }

        element.Value = value;
        return true;
    }
}

/// <summary>
/// Provider-IDs, die zwischen NFO, Emby-Item und manueller UI-Korrektur ausgetauscht werden.
/// </summary>
internal sealed record EmbyProviderIds(string? TvdbId, string? ImdbId)
{
    /// <summary>
    /// Leeres Provider-ID-Objekt.
    /// </summary>
    public static EmbyProviderIds Empty { get; } = new(null, null);

    /// <summary>
    /// Kennzeichnet, ob mindestens eine verwertbare Provider-ID vorhanden ist.
    /// </summary>
    public bool HasAny => !string.IsNullOrWhiteSpace(TvdbId) || !string.IsNullOrWhiteSpace(ImdbId);

    /// <summary>
    /// Baut einen neuen ID-Satz, bei dem eigene Werte Vorrang vor Fallback-Werten haben.
    /// </summary>
    public EmbyProviderIds MergeFallback(EmbyProviderIds fallback)
    {
        return new EmbyProviderIds(
            string.IsNullOrWhiteSpace(TvdbId) ? fallback.TvdbId : TvdbId,
            string.IsNullOrWhiteSpace(ImdbId) ? fallback.ImdbId : ImdbId);
    }
}

/// <summary>
/// Ergebnis eines reinen NFO-Lesezugriffs.
/// </summary>
internal sealed record EmbyNfoReadResult(
    string NfoPath,
    bool NfoExists,
    EmbyProviderIds ProviderIds,
    string? WarningMessage);

/// <summary>
/// Ergebnis einer NFO-Provider-ID-Aktualisierung.
/// </summary>
internal sealed record EmbyNfoUpdateResult(
    string NfoPath,
    bool NfoChanged,
    bool Success,
    string Message);
