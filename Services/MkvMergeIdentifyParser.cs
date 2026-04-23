using System.Globalization;
using System.Text.Json;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Normalisiert rohe <c>mkvmerge --identify</c>-Antworten auf die projektweit verwendeten Metadatenmodelle.
/// </summary>
internal static class MkvMergeIdentifyParser
{
    public static MediaTrackMetadata CreatePrimaryVideoMetadata(JsonDocument trackDocument, string inputFilePath)
    {
        var tracks = GetTracksElement(trackDocument, inputFilePath);

        JsonElement? videoTrack = null;
        JsonElement? fallbackAudioTrack = null;
        JsonElement? preferredAudioTrack = null;
        JsonElement fallbackAudioProperties = default;
        JsonElement preferredAudioProperties = default;

        foreach (var track in tracks.EnumerateArray())
        {
            var type = track.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;

            if (type == "video" && videoTrack is null)
            {
                videoTrack = track;
            }
            else if (type == "audio")
            {
                var candidateAudioProperties = track.TryGetProperty("properties", out var audioPropertiesElement)
                    ? audioPropertiesElement
                    : default;

                if (fallbackAudioTrack is null)
                {
                    fallbackAudioTrack = track;
                    fallbackAudioProperties = candidateAudioProperties;
                }

                if (preferredAudioTrack is null && !LooksLikeAudioDescriptionTrack(candidateAudioProperties))
                {
                    preferredAudioTrack = track;
                    preferredAudioProperties = candidateAudioProperties;
                }
            }
        }

        if (videoTrack is null)
        {
            throw new InvalidOperationException("In der Quelldatei wurde keine Videospur gefunden.");
        }

        var audioTrack = preferredAudioTrack ?? fallbackAudioTrack;
        if (audioTrack is null)
        {
            throw new InvalidOperationException("In der Quelldatei wurde keine Tonspur gefunden.");
        }

        var videoProperties = videoTrack.Value.TryGetProperty("properties", out var videoPropertiesElement)
            ? videoPropertiesElement
            : default;

        var audioProperties = preferredAudioTrack is not null
            ? preferredAudioProperties
            : fallbackAudioProperties;

        var width = videoProperties.ValueKind != JsonValueKind.Undefined && videoProperties.TryGetProperty("pixel_dimensions", out var pixelDimensionsElement)
            ? ParseWidth(pixelDimensionsElement.GetString())
            : 0;

        return new MediaTrackMetadata(
            VideoTrackId: ReadTrackId(videoTrack.Value),
            AudioTrackId: ReadTrackId(audioTrack.Value),
            VideoWidth: width,
            ResolutionLabel: ResolutionLabel.FromWidth(width),
            VideoCodecLabel: NormalizeVideoCodecName(ReadCodec(videoTrack.Value, videoProperties)),
            AudioCodecLabel: NormalizeAudioCodecName(ReadCodec(audioTrack.Value, audioProperties)),
            VideoLanguage: NormalizeLanguage(videoProperties),
            AudioLanguage: NormalizeLanguage(audioProperties));
    }

    public static AudioTrackMetadata CreateFirstAudioTrackMetadata(JsonDocument trackDocument, string inputFilePath)
    {
        var tracks = GetTracksElement(trackDocument, inputFilePath);

        foreach (var track in tracks.EnumerateArray())
        {
            if (track.TryGetProperty("type", out var typeElement) && typeElement.GetString() == "audio")
            {
                var properties = track.TryGetProperty("properties", out var propertiesElement)
                    ? propertiesElement
                    : default;

                return new AudioTrackMetadata(
                    TrackId: ReadTrackId(track),
                    CodecLabel: NormalizeAudioCodecName(ReadCodec(track, properties)),
                    Language: NormalizeLanguage(properties),
                    TrackName: ReadTrackName(properties),
                    IsVisualImpaired: ReadBooleanProperty(properties, "flag_visual_impaired"));
            }
        }

        throw new InvalidOperationException("In der Datei wurde keine Audiospur gefunden.");
    }

    public static ContainerMetadata CreateContainerMetadata(JsonDocument trackDocument, string inputFilePath)
    {
        var tracks = GetTracksElement(trackDocument, inputFilePath);
        var trackMetadata = new List<ContainerTrackMetadata>();

        foreach (var track in tracks.EnumerateArray())
        {
            var type = track.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString() ?? string.Empty
                : string.Empty;

            var properties = track.TryGetProperty("properties", out var propertiesElement)
                ? propertiesElement
                : default;

            var width = type == "video" && properties.ValueKind != JsonValueKind.Undefined && properties.TryGetProperty("pixel_dimensions", out var pixelDimensionsElement)
                ? ParseWidth(pixelDimensionsElement.GetString())
                : 0;

            trackMetadata.Add(new ContainerTrackMetadata(
                TrackId: ReadTrackId(track),
                Type: type,
                CodecLabel: NormalizeTrackCodecName(type, track, properties),
                Language: NormalizeLanguage(properties),
                TrackName: ReadTrackName(properties),
                VideoWidth: width,
                IsVisualImpaired: ReadBooleanProperty(properties, "flag_visual_impaired"),
                IsHearingImpaired: ReadBooleanProperty(properties, "flag_hearing_impaired"),
                IsDefaultTrack: ReadBooleanProperty(properties, "default_track"),
                Duration: ReadTrackDuration(properties)));
        }

        var attachments = new List<ContainerAttachmentMetadata>();
        if (trackDocument.RootElement.TryGetProperty("attachments", out var attachmentsElement)
            && attachmentsElement.ValueKind == JsonValueKind.Array)
        {
            var fallbackAttachmentId = 0;
            foreach (var attachment in attachmentsElement.EnumerateArray())
            {
                var attachmentId = attachment.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var parsedAttachmentId)
                    ? parsedAttachmentId
                    : fallbackAttachmentId;
                var fileName = attachment.TryGetProperty("file_name", out var fileNameElement)
                    ? fileNameElement.GetString()
                    : null;

                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    attachments.Add(new ContainerAttachmentMetadata(
                        attachmentId,
                        NormalizeDisplayText(fileName.Trim())));
                }

                fallbackAttachmentId++;
            }
        }

        return new ContainerMetadata(
            ReadContainerTitle(trackDocument.RootElement),
            trackMetadata,
            attachments);
    }

    private static string NormalizeTrackCodecName(string type, JsonElement track, JsonElement properties)
    {
        return type switch
        {
            "video" => NormalizeVideoCodecName(ReadCodec(track, properties)),
            "audio" => NormalizeAudioCodecName(ReadCodec(track, properties)),
            "subtitles" => NormalizeSubtitleCodecName(ReadCodec(track, properties)),
            _ => ReadCodec(track, properties) ?? "Unbekannt"
        };
    }

    private static JsonElement GetTracksElement(JsonDocument trackDocument, string inputFilePath)
    {
        if (trackDocument.RootElement.TryGetProperty("tracks", out var tracks)
            && tracks.ValueKind == JsonValueKind.Array)
        {
            return tracks;
        }

        var rootKeys = trackDocument.RootElement.ValueKind == JsonValueKind.Object
            ? string.Join(", ", trackDocument.RootElement.EnumerateObject().Select(property => property.Name))
            : trackDocument.RootElement.ValueKind.ToString();

        throw new InvalidOperationException(
            $"mkvmerge --identify lieferte für {Path.GetFileName(inputFilePath)} keine gültige Trackliste. Root-Felder: {rootKeys}");
    }

    private static int ReadTrackId(JsonElement track)
    {
        if (!track.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out var trackId))
        {
            throw new InvalidOperationException("mkvmerge hat keine gültige Track-ID geliefert.");
        }

        return trackId;
    }

    private static string? ReadCodec(JsonElement track, JsonElement properties)
    {
        if (track.TryGetProperty("codec", out var codecElement))
        {
            var codec = codecElement.GetString();
            if (!string.IsNullOrWhiteSpace(codec))
            {
                return codec;
            }
        }

        if (properties.ValueKind != JsonValueKind.Undefined && properties.TryGetProperty("codec_id", out var codecIdElement))
        {
            var codecId = codecIdElement.GetString();
            if (!string.IsNullOrWhiteSpace(codecId))
            {
                return codecId;
            }
        }

        return null;
    }

    private static string ReadTrackName(JsonElement properties)
    {
        if (properties.ValueKind != JsonValueKind.Undefined && properties.TryGetProperty("track_name", out var nameElement))
        {
            var value = nameElement.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return NormalizeDisplayText(value.Trim());
            }
        }

        return string.Empty;
    }

    private static bool LooksLikeAudioDescriptionTrack(JsonElement properties)
    {
        return AudioTrackClassifier.IsAudioDescriptionTrack(
            ReadTrackName(properties),
            ReadBooleanProperty(properties, "flag_visual_impaired"));
    }

    private static string NormalizeDisplayText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return MojibakeRepair.NormalizeLikelyMojibake(value);
    }

    private static string ReadContainerTitle(JsonElement rootElement)
    {
        if (!rootElement.TryGetProperty("container", out var containerElement)
            || containerElement.ValueKind != JsonValueKind.Object
            || !containerElement.TryGetProperty("properties", out var propertiesElement)
            || propertiesElement.ValueKind != JsonValueKind.Object
            || !propertiesElement.TryGetProperty("title", out var titleElement))
        {
            return string.Empty;
        }

        var title = titleElement.GetString();
        return string.IsNullOrWhiteSpace(title)
            ? string.Empty
            : NormalizeDisplayText(title.Trim());
    }

    private static bool ReadBooleanProperty(JsonElement properties, string propertyName)
    {
        if (properties.ValueKind == JsonValueKind.Undefined || !properties.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => property.TryGetInt32(out var number) && number != 0,
            JsonValueKind.String => bool.TryParse(property.GetString(), out var booleanValue) && booleanValue,
            _ => false
        };
    }

    private static TimeSpan? ReadTrackDuration(JsonElement properties)
    {
        if (properties.ValueKind == JsonValueKind.Undefined
            || !properties.TryGetProperty("tag_duration", out var durationElement))
        {
            return null;
        }

        var durationText = durationElement.GetString();
        if (string.IsNullOrWhiteSpace(durationText))
        {
            return null;
        }

        return TimeSpan.TryParse(NormalizeTrackDurationText(durationText), CultureInfo.InvariantCulture, out var duration)
            ? duration
            : null;
    }

    private static string NormalizeTrackDurationText(string durationText)
    {
        var trimmed = durationText.Trim();
        var separatorIndex = trimmed.IndexOf('.', StringComparison.Ordinal);
        if (separatorIndex < 0 || separatorIndex == trimmed.Length - 1)
        {
            return trimmed;
        }

        var fraction = trimmed[(separatorIndex + 1)..];
        return fraction.Length <= 7
            ? trimmed
            : trimmed[..(separatorIndex + 1)] + fraction[..7];
    }

    private static int ParseWidth(string? pixelDimensions)
    {
        if (string.IsNullOrWhiteSpace(pixelDimensions))
        {
            return 0;
        }

        var firstPart = pixelDimensions.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(firstPart, out var width) ? width : 0;
    }

    private static string NormalizeVideoCodecName(string? codecId)
    {
        if (string.IsNullOrWhiteSpace(codecId))
        {
            return "Unbekannt";
        }

        if (codecId.Contains("HEVC", StringComparison.OrdinalIgnoreCase) || codecId.Contains("H/265", StringComparison.OrdinalIgnoreCase))
        {
            return "H.265";
        }

        if (codecId.Contains("AVC", StringComparison.OrdinalIgnoreCase)
            || codecId.Contains("H.264", StringComparison.OrdinalIgnoreCase)
            || codecId.Contains("H/264", StringComparison.OrdinalIgnoreCase))
        {
            return "H.264";
        }

        return codecId;
    }

    private static string NormalizeAudioCodecName(string? codecId)
    {
        if (string.IsNullOrWhiteSpace(codecId))
        {
            return "Audio";
        }

        if (codecId.Contains("E-AC-3", StringComparison.OrdinalIgnoreCase)
            || codecId.Contains("EAC3", StringComparison.OrdinalIgnoreCase))
        {
            return "E-AC-3";
        }

        if (codecId.Contains("AC-3", StringComparison.OrdinalIgnoreCase)
            || codecId.Contains("AC3", StringComparison.OrdinalIgnoreCase))
        {
            return "AC-3";
        }

        if (codecId.Contains("AAC", StringComparison.OrdinalIgnoreCase))
        {
            return "AAC";
        }

        if (codecId.Contains("Opus", StringComparison.OrdinalIgnoreCase))
        {
            return "Opus";
        }

        if (codecId.Contains("Vorbis", StringComparison.OrdinalIgnoreCase))
        {
            return "Vorbis";
        }

        if (codecId.Contains("MP3", StringComparison.OrdinalIgnoreCase))
        {
            return "MP3";
        }

        if (codecId.Contains("MP2", StringComparison.OrdinalIgnoreCase))
        {
            return "MP2";
        }

        return codecId;
    }

    private static string NormalizeSubtitleCodecName(string? codecId)
    {
        if (string.IsNullOrWhiteSpace(codecId))
        {
            return "Unbekannt";
        }

        if (codecId.Contains("SubRip", StringComparison.OrdinalIgnoreCase)
            || codecId.Contains("SRT", StringComparison.OrdinalIgnoreCase))
        {
            return "SRT";
        }

        if (codecId.Contains("ASS", StringComparison.OrdinalIgnoreCase)
            || codecId.Contains("SSA", StringComparison.OrdinalIgnoreCase)
            || codecId.Contains("SubStation", StringComparison.OrdinalIgnoreCase))
        {
            return "SSA";
        }

        if (codecId.Contains("WebVTT", StringComparison.OrdinalIgnoreCase)
            || codecId.Contains("VTT", StringComparison.OrdinalIgnoreCase))
        {
            return "WebVTT";
        }

        return codecId;
    }

    private static string NormalizeLanguage(JsonElement properties)
    {
        var trackNameLanguage = MediaLanguageHelper.TryInferMuxLanguageCodeFromText(ReadTrackName(properties));
        if (!string.IsNullOrWhiteSpace(trackNameLanguage))
        {
            return trackNameLanguage;
        }

        if (properties.ValueKind != JsonValueKind.Undefined && properties.TryGetProperty("language_ietf", out var ietfElement))
        {
            var value = ietfElement.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        if (properties.ValueKind != JsonValueKind.Undefined && properties.TryGetProperty("language", out var languageElement))
        {
            var value = languageElement.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "de";
    }
}
