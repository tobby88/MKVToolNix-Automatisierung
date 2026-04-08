using System.Text.Json;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Liest optionale Zusatzdaten aus den Probe-Sidecars des testinternen <c>FakeMkvMerge</c>.
/// </summary>
/// <remarks>
/// Diese Hilfsklasse ist bewusst eine Test-Seam. Reale Produktivläufe mit echtem
/// <c>mkvmerge.exe</c> dürfen nicht von sidecar-basierten Zusatzfeldern abhängen und
/// fallen deshalb weiterhin auf <c>mkvextract</c> zurück.
/// </remarks>
internal static class FakeMkvMergeProbeSidecarReader
{
    /// <summary>
    /// Liest eingebetteten TXT-Inhalt aus einem Fake-<c>mkvmerge --identify</c>-Sidecar.
    /// </summary>
    /// <param name="mkvMergePath">Pfad zur aktuell verwendeten mkvmerge-Executable.</param>
    /// <param name="containerPath">Pfad zum zugehörigen Container.</param>
    /// <param name="attachment">Gesuchter Attachment-Metadatensatz.</param>
    /// <returns>
    /// Den eingebetteten Textinhalt des passenden Attachments, wenn die aktuelle Executable
    /// tatsächlich <c>FakeMkvMerge</c> ist und das Sidecar diesen Wert enthält; andernfalls
    /// <see langword="null"/>.
    /// </returns>
    public static string? TryReadAttachmentTextContent(
        string mkvMergePath,
        string containerPath,
        ContainerAttachmentMetadata attachment)
    {
        if (!UsesFakeMkvMerge(mkvMergePath))
        {
            return null;
        }

        var probeSidecarPath = containerPath + ".mkvmerge.json";
        if (!File.Exists(probeSidecarPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(probeSidecarPath));
            if (!document.RootElement.TryGetProperty("attachments", out var attachmentsElement)
                || attachmentsElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var fallbackAttachmentId = 0;
            foreach (var attachmentElement in attachmentsElement.EnumerateArray())
            {
                var candidateId = attachmentElement.TryGetProperty("id", out var idElement)
                    && idElement.TryGetInt32(out var parsedAttachmentId)
                        ? parsedAttachmentId
                        : fallbackAttachmentId;
                var candidateFileName = attachmentElement.TryGetProperty("file_name", out var fileNameElement)
                    ? fileNameElement.GetString()
                    : null;
                if ((candidateId == attachment.Id
                        || string.Equals(candidateFileName, attachment.FileName, StringComparison.OrdinalIgnoreCase))
                    && attachmentElement.TryGetProperty("text_content", out var textContentElement)
                    && textContentElement.ValueKind == JsonValueKind.String)
                {
                    return textContentElement.GetString();
                }

                fallbackAttachmentId++;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static bool UsesFakeMkvMerge(string mkvMergePath)
    {
        return string.Equals(
            Path.GetFileNameWithoutExtension(mkvMergePath),
            "FakeMkvMerge",
            StringComparison.OrdinalIgnoreCase);
    }
}
