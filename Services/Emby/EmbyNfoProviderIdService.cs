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
        var metadata = ReadEpisodeMetadata(mediaFilePath);
        return new EmbyNfoReadResult(
            metadata.NfoPath,
            metadata.NfoExists,
            metadata.ProviderIds,
            metadata.WarningMessage);
    }

    /// <summary>
    /// Liest Provider-IDs und die editierbaren Emby-Titelfelder aus einer NFO.
    /// </summary>
    /// <param name="mediaFilePath">Pfad zur MKV-Datei.</param>
    /// <returns>NFO-Metadaten inklusive Titel, Sortiertitel und Provider-IDs.</returns>
    public EmbyNfoMetadataReadResult ReadEpisodeMetadata(string mediaFilePath)
    {
        var nfoPath = GetNfoPath(mediaFilePath);
        if (!File.Exists(nfoPath))
        {
            return new EmbyNfoMetadataReadResult(
                nfoPath,
                NfoExists: false,
                EmbyProviderIds.Empty,
                Title: null,
                SortTitle: null,
                WarningMessage: null);
        }

        try
        {
            var document = XDocument.Load(nfoPath, LoadOptions.PreserveWhitespace);
            var root = document.Root;
            if (root is null)
            {
                return new EmbyNfoMetadataReadResult(
                    nfoPath,
                    NfoExists: true,
                    EmbyProviderIds.Empty,
                    Title: null,
                    SortTitle: null,
                    "Die NFO enthält kein XML-Wurzelelement.");
            }

            return new EmbyNfoMetadataReadResult(
                nfoPath,
                NfoExists: true,
                new EmbyProviderIds(
                    ReadProviderId(root, "tvdb", "tvdbid"),
                    ReadProviderId(root, "imdb", "imdbid")),
                ReadOptionalElementValue(root, "title"),
                ReadOptionalElementValue(root, "sorttitle"),
                WarningMessage: null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return new EmbyNfoMetadataReadResult(
                nfoPath,
                NfoExists: true,
                EmbyProviderIds.Empty,
                Title: null,
                SortTitle: null,
                ex.Message);
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
    /// <param name="removeImdbId">Entfernt vorhandene IMDb-Felder, wenn der Benutzer bewusst keine IMDb-ID vergeben hat.</param>
    /// <returns>Ergebnis mit Änderungsstatus und Hinweistext.</returns>
    public EmbyNfoUpdateResult UpdateProviderIds(string mediaFilePath, EmbyProviderIds providerIds, bool removeImdbId = false)
    {
        var nfoPath = GetNfoPath(mediaFilePath);
        if (!File.Exists(nfoPath))
        {
            return new EmbyNfoUpdateResult(nfoPath, NfoChanged: false, Success: false, "NFO-Datei fehlt. Bitte zuerst Emby scannen lassen.");
        }

        if (!providerIds.HasAny && !removeImdbId)
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
            else if (removeImdbId)
            {
                changed |= RemoveProviderId(root, "imdb", "imdbid");
            }

            if (!changed)
            {
                return new EmbyNfoUpdateResult(nfoPath, NfoChanged: false, Success: true, "NFO-Provider-IDs waren bereits aktuell.");
            }

            SaveAtomically(document, nfoPath);
            return new EmbyNfoUpdateResult(nfoPath, NfoChanged: true, Success: true, "NFO-Provider-IDs aktualisiert.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return new EmbyNfoUpdateResult(nfoPath, NfoChanged: false, Success: false, ex.Message);
        }
    }

    /// <summary>
    /// Aktualisiert den sichtbaren Episodentitel und Sortiertitel in einer vorhandenen NFO.
    /// Geänderte Felder werden per <c>lockedfields</c> vor Emby-Überschreibungen geschützt.
    /// </summary>
    /// <param name="mediaFilePath">Pfad zur MKV-Datei.</param>
    /// <param name="textFields">Zielwerte für die editierbaren NFO-Titelfelder.</param>
    /// <returns>Ergebnis mit Änderungsstatus und Hinweistext.</returns>
    public EmbyNfoUpdateResult UpdateTextFields(string mediaFilePath, EmbyNfoTextFields textFields)
    {
        ArgumentNullException.ThrowIfNull(textFields);

        var nfoPath = GetNfoPath(mediaFilePath);
        if (!File.Exists(nfoPath))
        {
            return new EmbyNfoUpdateResult(nfoPath, NfoChanged: false, Success: false, "NFO-Datei fehlt. Bitte zuerst Emby scannen lassen.");
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
            var titleChanged = textFields.Title is not null
                               && !string.Equals(ReadOptionalElementValue(root, "title") ?? string.Empty, textFields.Title, StringComparison.Ordinal);
            var sortTitleChanged = textFields.SortTitle is not null
                                   && !string.Equals(ReadOptionalElementValue(root, "sorttitle") ?? string.Empty, textFields.SortTitle, StringComparison.Ordinal);

            if (titleChanged)
            {
                changed |= SetTextElement(root, "title", textFields.Title!);
            }

            if (sortTitleChanged)
            {
                changed |= SetTextElement(root, "sorttitle", textFields.SortTitle!);
            }

            if (titleChanged || sortTitleChanged)
            {
                changed |= EnsureLockedFields(root, lockName: titleChanged, lockSortName: sortTitleChanged);
            }

            if (!changed)
            {
                return new EmbyNfoUpdateResult(nfoPath, NfoChanged: false, Success: true, "NFO-Titelfelder waren bereits aktuell.");
            }

            SaveAtomically(document, nfoPath);
            return new EmbyNfoUpdateResult(nfoPath, NfoChanged: true, Success: true, "NFO-Titelfelder aktualisiert.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return new EmbyNfoUpdateResult(nfoPath, NfoChanged: false, Success: false, ex.Message);
        }
    }

    private static string? ReadProviderId(XElement root, string uniqueIdType, string legacyElementName)
    {
        var matchingUniqueIds = root
            .Elements("uniqueid")
            .Where(element => string.Equals(
                (string?)element.Attribute("type"),
                uniqueIdType,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        var uniqueId = matchingUniqueIds.FirstOrDefault(IsDefaultUniqueId)
                       ?? matchingUniqueIds.FirstOrDefault();
        var uniqueIdValue = uniqueId?.Value.Trim();
        if (!string.IsNullOrWhiteSpace(uniqueIdValue))
        {
            return uniqueIdValue;
        }

        var legacyValue = root.Element(legacyElementName)?.Value.Trim();
        return string.IsNullOrWhiteSpace(legacyValue) ? null : legacyValue;
    }

    private static string? ReadOptionalElementValue(XElement root, string elementName)
    {
        var value = root.Element(elementName)?.Value.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
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

    private static bool SetTextElement(XElement root, string elementName, string value)
    {
        var matchingElements = root.Elements(elementName).ToList();
        var element = matchingElements.FirstOrDefault();
        if (element is null)
        {
            root.AddFirst(new XElement(elementName, value));
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

    private static bool EnsureLockedFields(XElement root, bool lockName, bool lockSortName)
    {
        var desiredFields = new List<string>();
        if (lockName)
        {
            desiredFields.Add("Name");
        }

        if (lockSortName)
        {
            desiredFields.Add("SortName");
        }

        if (desiredFields.Count == 0)
        {
            return false;
        }

        var lockedFieldsElement = root.Element("lockedfields");
        var fields = lockedFieldsElement is null
            ? new List<string>()
            : lockedFieldsElement.Value
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        foreach (var desiredField in desiredFields)
        {
            if (!fields.Contains(desiredField, StringComparer.OrdinalIgnoreCase))
            {
                fields.Add(desiredField);
            }
        }

        var expectedValue = string.Join("|", fields.Distinct(StringComparer.OrdinalIgnoreCase));
        if (lockedFieldsElement is null)
        {
            lockedFieldsElement = new XElement("lockedfields", expectedValue);
            var lockDataElement = root.Element("lockdata");
            var dateAddedElement = root.Element("dateadded");
            if (lockDataElement is not null)
            {
                lockDataElement.AddAfterSelf(lockedFieldsElement);
            }
            else if (dateAddedElement is not null)
            {
                dateAddedElement.AddBeforeSelf(lockedFieldsElement);
            }
            else
            {
                root.Add(lockedFieldsElement);
            }

            return true;
        }

        if (string.Equals(lockedFieldsElement.Value.Trim(), expectedValue, StringComparison.Ordinal))
        {
            return false;
        }

        lockedFieldsElement.Value = expectedValue;
        return true;
    }

    private static bool RemoveProviderId(XElement root, string uniqueIdType, string legacyElementName)
    {
        var changed = false;
        foreach (var uniqueId in root
                     .Elements("uniqueid")
                     .Where(element => string.Equals(
                         (string?)element.Attribute("type"),
                         uniqueIdType,
                         StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            uniqueId.Remove();
            changed = true;
        }

        foreach (var element in root.Elements(legacyElementName).ToList())
        {
            element.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool IsDefaultUniqueId(XElement uniqueId)
    {
        return string.Equals(
            (string?)uniqueId.Attribute("default"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private static void SaveAtomically(XDocument document, string nfoPath)
    {
        var directory = Path.GetDirectoryName(nfoPath);
        var tempPath = Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? "." : directory,
            $".{Path.GetFileName(nfoPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            document.Save(tempPath);
            File.Replace(tempPath, nfoPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
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
/// Vollständiger NFO-Lesezustand für Archivpflege und Emby-Abgleich.
/// </summary>
internal sealed record EmbyNfoMetadataReadResult(
    string NfoPath,
    bool NfoExists,
    EmbyProviderIds ProviderIds,
    string? Title,
    string? SortTitle,
    string? WarningMessage);

/// <summary>
/// Zielwerte für NFO-Titeländerungen.
/// </summary>
internal sealed record EmbyNfoTextFields(
    string? Title,
    string? SortTitle);

/// <summary>
/// Ergebnis einer NFO-Provider-ID-Aktualisierung.
/// </summary>
internal sealed record EmbyNfoUpdateResult(
    string NfoPath,
    bool NfoChanged,
    bool Success,
    string Message);
