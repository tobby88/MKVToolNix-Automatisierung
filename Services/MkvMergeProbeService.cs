using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

public sealed class MkvMergeProbeService
{
    private readonly ConcurrentDictionary<string, MediaTrackMetadata> _mediaTrackCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AudioTrackMetadata> _audioTrackCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ContainerMetadata> _containerCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<MediaTrackMetadata> ReadPrimaryVideoMetadataAsync(string mkvMergePath, string inputFilePath)
    {
        if (_mediaTrackCache.TryGetValue(inputFilePath, out var cachedMetadata))
        {
            return cachedMetadata;
        }

        var trackDocument = await IdentifyAsync(mkvMergePath, inputFilePath);
        var tracks = GetTracksElement(trackDocument, inputFilePath);

        JsonElement? videoTrack = null;
        JsonElement? audioTrack = null;

        foreach (var track in tracks.EnumerateArray())
        {
            var type = track.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;

            if (type == "video" && videoTrack is null)
            {
                videoTrack = track;
            }
            else if (type == "audio" && audioTrack is null)
            {
                audioTrack = track;
            }
        }

        if (videoTrack is null)
        {
            throw new InvalidOperationException("In der Quelldatei wurde keine Videospur gefunden.");
        }

        if (audioTrack is null)
        {
            throw new InvalidOperationException("In der Quelldatei wurde keine Tonspur gefunden.");
        }

        var videoProperties = videoTrack.Value.TryGetProperty("properties", out var videoPropertiesElement)
            ? videoPropertiesElement
            : default;

        var audioProperties = audioTrack.Value.TryGetProperty("properties", out var audioPropertiesElement)
            ? audioPropertiesElement
            : default;

        var width = videoProperties.ValueKind != JsonValueKind.Undefined && videoProperties.TryGetProperty("pixel_dimensions", out var pixelDimensionsElement)
            ? ParseWidth(pixelDimensionsElement.GetString())
            : 0;

        var videoCodec = NormalizeVideoCodecName(ReadCodec(videoTrack.Value, videoProperties));
        var audioCodec = NormalizeAudioCodecName(ReadCodec(audioTrack.Value, audioProperties));

        var metadata = new MediaTrackMetadata(
            VideoTrackId: ReadTrackId(videoTrack.Value),
            AudioTrackId: ReadTrackId(audioTrack.Value),
            VideoWidth: width,
            ResolutionLabel: ResolutionLabel.FromWidth(width),
            VideoCodecLabel: videoCodec,
            AudioCodecLabel: audioCodec,
            VideoLanguage: NormalizeLanguage(videoProperties),
            AudioLanguage: NormalizeLanguage(audioProperties));

        _mediaTrackCache[inputFilePath] = metadata;
        return metadata;
    }

    public async Task<AudioTrackMetadata> ReadFirstAudioTrackMetadataAsync(string mkvMergePath, string inputFilePath)
    {
        if (_audioTrackCache.TryGetValue(inputFilePath, out var cachedMetadata))
        {
            return cachedMetadata;
        }

        var trackDocument = await IdentifyAsync(mkvMergePath, inputFilePath);
        var tracks = GetTracksElement(trackDocument, inputFilePath);

        foreach (var track in tracks.EnumerateArray())
        {
            if (track.TryGetProperty("type", out var typeElement) && typeElement.GetString() == "audio")
            {
                var properties = track.TryGetProperty("properties", out var propertiesElement)
                    ? propertiesElement
                    : default;

                var metadata = new AudioTrackMetadata(
                    TrackId: ReadTrackId(track),
                    CodecLabel: NormalizeAudioCodecName(ReadCodec(track, properties)),
                    Language: NormalizeLanguage(properties),
                    TrackName: ReadTrackName(properties),
                    IsVisualImpaired: ReadBooleanProperty(properties, "flag_visual_impaired"));

                _audioTrackCache[inputFilePath] = metadata;
                return metadata;
            }
        }

        throw new InvalidOperationException("In der Datei wurde keine Audiospur gefunden.");
    }

    public async Task<ContainerMetadata> ReadContainerMetadataAsync(string mkvMergePath, string inputFilePath)
    {
        if (_containerCache.TryGetValue(inputFilePath, out var cachedMetadata))
        {
            return cachedMetadata;
        }

        var trackDocument = await IdentifyAsync(mkvMergePath, inputFilePath);
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

            var codecLabel = type switch
            {
                "video" => NormalizeVideoCodecName(ReadCodec(track, properties)),
                "audio" => NormalizeAudioCodecName(ReadCodec(track, properties)),
                "subtitles" => NormalizeSubtitleCodecName(ReadCodec(track, properties)),
                _ => ReadCodec(track, properties) ?? "Unbekannt"
            };

            var width = type == "video" && properties.ValueKind != JsonValueKind.Undefined && properties.TryGetProperty("pixel_dimensions", out var pixelDimensionsElement)
                ? ParseWidth(pixelDimensionsElement.GetString())
                : 0;

            trackMetadata.Add(new ContainerTrackMetadata(
                TrackId: ReadTrackId(track),
                Type: type,
                CodecLabel: codecLabel,
                Language: NormalizeLanguage(properties),
                TrackName: ReadTrackName(properties),
                VideoWidth: width,
                IsVisualImpaired: ReadBooleanProperty(properties, "flag_visual_impaired"),
                IsHearingImpaired: ReadBooleanProperty(properties, "flag_hearing_impaired"),
                IsDefaultTrack: ReadBooleanProperty(properties, "default_track")));
        }

        var attachments = new List<ContainerAttachmentMetadata>();
        if (trackDocument.RootElement.TryGetProperty("attachments", out var attachmentsElement)
            && attachmentsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var attachment in attachmentsElement.EnumerateArray())
            {
                var fileName = attachment.TryGetProperty("file_name", out var fileNameElement)
                    ? fileNameElement.GetString()
                    : null;

                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    attachments.Add(new ContainerAttachmentMetadata(NormalizeDisplayText(fileName.Trim())));
                }
            }
        }

        var metadata = new ContainerMetadata(trackMetadata, attachments);
        _containerCache[inputFilePath] = metadata;
        return metadata;
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
            $"mkvmerge --identify lieferte fuer {Path.GetFileName(inputFilePath)} keine gueltige Trackliste. Root-Felder: {rootKeys}");
    }

    private static int ReadTrackId(JsonElement track)
    {
        if (!track.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out var trackId))
        {
            throw new InvalidOperationException("mkvmerge hat keine gueltige Track-ID geliefert.");
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

    private static string NormalizeDisplayText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (!value.Contains('Ã') && !value.Contains('â'))
        {
            return value;
        }

        try
        {
            var repaired = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(value));
            return string.IsNullOrWhiteSpace(repaired) ? value : repaired;
        }
        catch
        {
            return value;
        }
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

    private async Task<JsonDocument> IdentifyAsync(string mkvMergePath, string inputFilePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = mkvMergePath,
            Arguments = $"--identify --identification-format json \"{inputFilePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("mkvmerge konnte nicht gestartet werden.");

        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (!string.IsNullOrWhiteSpace(standardOutput))
        {
            try
            {
                return JsonDocument.Parse(standardOutput);
            }
            catch (JsonException)
            {
                if (process.ExitCode == 0)
                {
                    throw;
                }
            }
        }

        var details = string.IsNullOrWhiteSpace(standardError)
            ? "Es wurde keine gueltige JSON-Antwort geliefert."
            : standardError.Trim();

        throw new InvalidOperationException($"mkvmerge --identify ist fehlgeschlagen: {details}");
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
