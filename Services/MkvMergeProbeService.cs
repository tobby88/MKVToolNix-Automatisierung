using System.Diagnostics;
using System.Text.Json;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

public sealed class MkvMergeProbeService
{
    public async Task<MediaTrackMetadata> ReadPrimaryVideoMetadataAsync(string mkvMergePath, string inputFilePath)
    {
        var trackDocument = await IdentifyAsync(mkvMergePath, inputFilePath);
        var tracks = trackDocument.RootElement.GetProperty("tracks");

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

        var codec = NormalizeCodecName(ReadCodec(videoTrack.Value, videoProperties));

        return new MediaTrackMetadata(
            VideoTrackId: ReadTrackId(videoTrack.Value),
            AudioTrackId: ReadTrackId(audioTrack.Value),
            ResolutionLabel: ResolutionLabel.FromWidth(width),
            VideoCodecLabel: codec,
            VideoLanguage: NormalizeLanguage(videoProperties),
            AudioLanguage: NormalizeLanguage(audioProperties));
    }

    public async Task<int> ReadFirstAudioTrackIdAsync(string mkvMergePath, string inputFilePath)
    {
        var trackDocument = await IdentifyAsync(mkvMergePath, inputFilePath);
        var tracks = trackDocument.RootElement.GetProperty("tracks");

        foreach (var track in tracks.EnumerateArray())
        {
            if (track.TryGetProperty("type", out var typeElement) && typeElement.GetString() == "audio")
            {
                return ReadTrackId(track);
            }
        }

        throw new InvalidOperationException("In der Datei wurde keine Audiospur gefunden.");
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

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"mkvmerge --identify ist fehlgeschlagen: {standardError}");
        }

        return JsonDocument.Parse(standardOutput);
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

    private static string NormalizeCodecName(string? codecId)
    {
        if (string.IsNullOrWhiteSpace(codecId))
        {
            return "Unbekannt";
        }

        if (codecId.Contains("HEVC", StringComparison.OrdinalIgnoreCase) || codecId.Contains("H/265", StringComparison.OrdinalIgnoreCase))
        {
            return "H.265";
        }

        if (codecId.Contains("AVC", StringComparison.OrdinalIgnoreCase) || codecId.Contains("H.264", StringComparison.OrdinalIgnoreCase) || codecId.Contains("H/264", StringComparison.OrdinalIgnoreCase))
        {
            return "H.264";
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