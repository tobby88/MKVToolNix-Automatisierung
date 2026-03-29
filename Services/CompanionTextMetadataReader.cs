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
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return CompanionTextMetadata.Empty;
        }

        var content = ReadTextWithFallback(filePath);
        var sender = ReadLabeledValue(content, "Sender");
        var topic = ReadLabeledValue(content, "Thema");
        var title = ReadLabeledValue(content, "Titel");
        var durationText = ReadLabeledValue(content, "Dauer");
        var duration = TimeSpan.TryParse(durationText, out var parsedDuration) ? (TimeSpan?)parsedDuration : null;

        return new CompanionTextMetadata(sender, topic, title, duration);
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
}

/// <summary>
/// Kleines, projektweit wiederverwendbares Abbild der TXT-Begleitmetadaten einer Quelle.
/// </summary>
internal sealed record CompanionTextMetadata(string? Sender, string? Topic, string? Title, TimeSpan? Duration)
{
    public static CompanionTextMetadata Empty { get; } = new(null, null, null, null);
}
