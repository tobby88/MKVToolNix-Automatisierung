using System.Text;
using System.Text.RegularExpressions;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Liest optionale TXT-Begleitdateien für Sender-, Titel- und Laufzeitinformationen mit denselben Encoding-Heuristiken im ganzen Projekt.
/// </summary>
internal static class CompanionTextMetadataReader
{
    /// <summary>
    /// Liest die Begleitdatei direkt von einem TXT-Pfad.
    /// </summary>
    /// <param name="filePath">Pfad zur TXT-Datei.</param>
    /// <returns>Gelesene Metadaten oder <see cref="CompanionTextMetadata.Empty"/>, wenn keine Datei vorliegt.</returns>
    public static CompanionTextMetadata Read(string? filePath)
    {
        var details = ReadDetailed(filePath);
        return new CompanionTextMetadata(details.Sender, details.Topic, details.Title, details.Duration, details.ExpectedSizeBytes);
    }

    /// <summary>
    /// Liest die Begleitdatei direkt von einem TXT-Pfad inklusive zusätzlicher URL-/Web-Metadaten für Heuristiken.
    /// </summary>
    /// <param name="filePath">Pfad zur TXT-Datei.</param>
    /// <returns>Gelesene Detailmetadaten oder <see cref="CompanionTextDetails.Empty"/>, wenn keine Datei vorliegt.</returns>
    public static CompanionTextDetails ReadDetailed(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return CompanionTextDetails.Empty;
        }

        var content = ReadTextWithFallback(filePath);
        return ParseDetailed(content);
    }

    /// <summary>
    /// Liest die optionale TXT-Begleitdatei, die zu einer Mediendatei mit demselben Basisnamen gehört.
    /// </summary>
    /// <param name="mediaFilePath">Pfad zur Medienquelle.</param>
    /// <returns>Gelesene Metadaten oder <see cref="CompanionTextMetadata.Empty"/>, wenn keine Begleitdatei vorliegt.</returns>
    public static CompanionTextMetadata ReadForMediaFile(string? mediaFilePath)
    {
        if (string.IsNullOrWhiteSpace(mediaFilePath))
        {
            return CompanionTextMetadata.Empty;
        }

        return Read(Path.ChangeExtension(mediaFilePath, ".txt"));
    }

    private static string ReadTextWithFallback(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var utf8 = DecodeText(bytes, Encoding.UTF8);
        var normalizedUtf8 = MojibakeRepair.NormalizeLikelyMojibake(utf8);
        if (!MojibakeRepair.LooksLikeMojibake(normalizedUtf8))
        {
            return normalizedUtf8;
        }

        var latin1 = DecodeText(bytes, Encoding.Latin1);
        var normalizedLatin1 = MojibakeRepair.NormalizeLikelyMojibake(latin1);
        return string.IsNullOrWhiteSpace(normalizedLatin1) ? latin1 : normalizedLatin1;
    }

    private static string DecodeText(byte[] bytes, Encoding encoding)
    {
        try
        {
            return encoding.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? ReadLabeledValue(string content, string label)
    {
        var match = Regex.Match(content, $@"^{Regex.Escape(label)}\s*:\s*(.+)$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static CompanionTextDetails ParseDetailed(string content)
    {
        var sender = ReadLabeledValue(content, "Sender");
        var topic = ReadLabeledValue(content, "Thema");
        var title = ReadLabeledValue(content, "Titel");
        var durationText = ReadLabeledValue(content, "Dauer");
        var duration = TimeSpan.TryParse(durationText, out var parsedDuration) ? (TimeSpan?)parsedDuration : null;
        var expectedSizeBytes = TryParseFileSize(ReadLabeledValue(content, "Größe") ?? ReadLabeledValue(content, "Groesse"));
        var websiteUrl = ReadSectionUrl(content, "Website");
        var mediaUrl = ReadSectionUrl(content, "URL");

        return new CompanionTextDetails(sender, topic, title, duration, expectedSizeBytes, websiteUrl, mediaUrl);
    }

    private static long? TryParseFileSize(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var match = Regex.Match(
            rawValue,
            @"(?<value>\d+(?:[.,]\d+)?)\s*(?<unit>Bytes?|[KMGT]i?B)",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var normalizedValue = match.Groups["value"].Value.Replace(',', '.');
        if (!double.TryParse(
                normalizedValue,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value)
            || value < 0)
        {
            return null;
        }

        var multiplier = match.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "KB" or "KIB" => 1024d,
            "MB" or "MIB" => 1024d * 1024d,
            "GB" or "GIB" => 1024d * 1024d * 1024d,
            "TB" or "TIB" => 1024d * 1024d * 1024d * 1024d,
            _ => 1d
        };

        return (long)Math.Round(value * multiplier);
    }

    private static string? ReadSectionUrl(string content, string sectionTitle)
    {
        var match = Regex.Match(
            content,
            $@"^{Regex.Escape(sectionTitle)}\s*$\s*(?:\r?\n)+\s*(https?://\S+)",
            RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }
}

/// <summary>
/// Kleines, projektweit wiederverwendbares Abbild der TXT-Begleitmetadaten einer Quelle.
/// </summary>
internal sealed record CompanionTextMetadata(string? Sender, string? Topic, string? Title, TimeSpan? Duration, long? ExpectedSizeBytes)
{
    public static CompanionTextMetadata Empty { get; } = new(null, null, null, null, null);
}

/// <summary>
/// Erweiterte TXT-Begleitmetadaten inklusive URL-Feldern für spätere Zuordnungsheuristiken.
/// </summary>
internal sealed record CompanionTextDetails(
    string? Sender,
    string? Topic,
    string? Title,
    TimeSpan? Duration,
    long? ExpectedSizeBytes,
    string? WebsiteUrl,
    string? MediaUrl)
{
    public static CompanionTextDetails Empty { get; } = new(null, null, null, null, null, null, null);
}
