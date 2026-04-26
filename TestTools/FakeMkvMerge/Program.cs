using System.Text.Json;
using System.Text.Json.Nodes;

namespace FakeMkvMerge;

/// <summary>
/// Marker type so integration tests can locate the built helper executable.
/// </summary>
public static class FakeMkvMergeMarker
{
}

internal static class Program
{
    private static readonly HashSet<string> MuxOptionsWithValue = new(StringComparer.OrdinalIgnoreCase)
    {
        "--output",
        "--title",
        "--audio-tracks",
        "--subtitle-tracks",
        "--attachments",
        "--video-tracks",
        "--language",
        "--track-name",
        "--default-track-flag",
        "--stereo-mode",
        "--original-flag",
        "--visual-impaired-flag",
        "--hearing-impaired-flag",
        "--forced-display-flag",
        "--attachment-mime-type",
        "--attach-file"
    };

    private static readonly HashSet<string> MuxSwitchesWithoutValue = new(StringComparer.OrdinalIgnoreCase)
    {
        "--no-audio",
        "--no-video",
        "--no-subtitles",
        "--no-attachments"
    };

    private static int Main(string[] args)
    {
        try
        {
            return Execute(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 99;
        }
    }

    private static int Execute(IReadOnlyList<string> args)
    {
        if (IsIdentifyCommand(args))
        {
            return ExecuteIdentify(args[^1]);
        }

        if (IsAttachmentExtractionCommand(args))
        {
            return ExecuteAttachmentExtraction(args);
        }

        if (IsHeaderEditCommand(args))
        {
            return ExecuteHeaderEdit(args);
        }

        return ExecuteMux(args);
    }

    private static bool IsIdentifyCommand(IReadOnlyList<string> args)
    {
        return args.Count >= 4
            && string.Equals(args[0], "--identify", StringComparison.OrdinalIgnoreCase)
            && string.Equals(args[1], "--identification-format", StringComparison.OrdinalIgnoreCase)
            && string.Equals(args[2], "json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAttachmentExtractionCommand(IReadOnlyList<string> args)
    {
        return args.Count >= 3
            && string.Equals(args[0], "attachments", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHeaderEditCommand(IReadOnlyList<string> args)
    {
        return args.Count >= 3
            && !args[0].StartsWith("-", StringComparison.Ordinal)
            && args.Contains("--edit", StringComparer.OrdinalIgnoreCase)
            && args.Contains("--set", StringComparer.OrdinalIgnoreCase);
    }

    private static int ExecuteIdentify(string inputFilePath)
    {
        var (delayBeforeOutputMilliseconds, invocationLogFilePath, probeJson) = LoadProbeResponse(inputFilePath);
        WriteProbeInvocationLog(invocationLogFilePath, inputFilePath);
        if (delayBeforeOutputMilliseconds > 0)
        {
            Thread.Sleep(delayBeforeOutputMilliseconds);
        }

        Console.Write(probeJson);
        return 0;
    }

    private static int ExecuteAttachmentExtraction(IReadOnlyList<string> args)
    {
        var containerPath = args[1];
        var (_, _, probeJson) = LoadProbeResponse(containerPath);
        using var document = JsonDocument.Parse(probeJson);

        for (var index = 2; index < args.Count; index++)
        {
            if (!TryParseAttachmentExtractionArgument(args[index], out var attachmentId, out var outputPath))
            {
                Console.Error.WriteLine($"FakeMkvMerge: Ungültiges attachments-Argument '{args[index]}'.");
                return 3;
            }

            var textContent = FindAttachmentTextContent(document.RootElement, attachmentId);
            WriteExtractedAttachment(outputPath, textContent ?? string.Empty);
        }

        return 0;
    }

    private static bool TryParseAttachmentExtractionArgument(
        string value,
        out int attachmentId,
        out string outputPath)
    {
        attachmentId = 0;
        outputPath = string.Empty;

        var separatorIndex = value.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(value[..separatorIndex], out attachmentId))
        {
            return false;
        }

        outputPath = value[(separatorIndex + 1)..];
        return !string.IsNullOrWhiteSpace(outputPath);
    }

    private static string? FindAttachmentTextContent(JsonElement rootElement, int attachmentId)
    {
        if (!rootElement.TryGetProperty("attachments", out var attachmentsElement)
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
            if (candidateId == attachmentId)
            {
                return attachmentElement.TryGetProperty("text_content", out var textContentElement)
                    && textContentElement.ValueKind == JsonValueKind.String
                    ? textContentElement.GetString()
                    : null;
            }

            fallbackAttachmentId++;
        }

        return null;
    }

    private static void WriteExtractedAttachment(string outputPath, string content)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputPath, content);
    }

    private static int ExecuteHeaderEdit(IReadOnlyList<string> args)
    {
        var inputFilePath = args[0];
        var probeFilePath = inputFilePath + ".mkvmerge.json";
        if (!File.Exists(probeFilePath))
        {
            Console.Error.WriteLine($"FakeMkvMerge: Probe-Datei für mkvpropedit fehlt: {probeFilePath}");
            return 4;
        }

        var rootNode = JsonNode.Parse(File.ReadAllText(probeFilePath))?.AsObject();
        var tracks = rootNode?["tracks"]?.AsArray();
        if (rootNode is null || tracks is null)
        {
            Console.Error.WriteLine($"FakeMkvMerge: Ungültige Probe-Datei für mkvpropedit: {probeFilePath}");
            return 4;
        }

        string? currentSelector = null;
        for (var index = 1; index < args.Count; index++)
        {
            if (string.Equals(args[index], "--edit", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Count)
                {
                    Console.Error.WriteLine("FakeMkvMerge: --edit ohne Selektor.");
                    return 5;
                }

                currentSelector = args[++index];
                continue;
            }

            if (!string.Equals(args[index], "--set", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (currentSelector is null || index + 1 >= args.Count)
            {
                Console.Error.WriteLine("FakeMkvMerge: --set ohne vorherigen Selektor oder Wert.");
                return 5;
            }

            var assignment = args[++index];
            var separatorIndex = assignment.IndexOf('=');
            if (separatorIndex <= 0)
            {
                Console.Error.WriteLine($"FakeMkvMerge: Ungültiger --set-Wert '{assignment}'.");
                return 5;
            }

            var propertyName = assignment[..separatorIndex];
            var propertyValue = assignment[(separatorIndex + 1)..];
            if (string.Equals(currentSelector, "info", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(propertyName, "title", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"FakeMkvMerge: Nicht unterstützte Container-Eigenschaft '{propertyName}'.");
                    return 5;
                }

                var containerNode = rootNode["container"] as JsonObject ?? new JsonObject();
                rootNode["container"] = containerNode;
                var containerProperties = containerNode["properties"] as JsonObject ?? new JsonObject();
                containerNode["properties"] = containerProperties;
                containerProperties["title"] = propertyValue;
                continue;
            }

            if (!string.Equals(propertyName, "name", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"FakeMkvMerge: Nicht unterstützte Eigenschaft '{propertyName}'.");
                return 5;
            }

            if (!TryResolveTrackArrayIndex(currentSelector, out var trackArrayIndex))
            {
                Console.Error.WriteLine($"FakeMkvMerge: Nicht unterstützter Track-Selektor '{currentSelector}'.");
                return 5;
            }

            if (trackArrayIndex < 0 || trackArrayIndex >= tracks.Count || tracks[trackArrayIndex] is not JsonObject trackNode)
            {
                Console.Error.WriteLine($"FakeMkvMerge: Track-Selektor '{currentSelector}' liegt außerhalb der vorhandenen Trackliste.");
                return 5;
            }

            var properties = trackNode["properties"] as JsonObject ?? new JsonObject();
            trackNode["properties"] = properties;
            properties["track_name"] = propertyValue;
        }

        File.WriteAllText(
            probeFilePath,
            rootNode.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        return 0;
    }

    private static bool TryResolveTrackArrayIndex(string selector, out int trackArrayIndex)
    {
        trackArrayIndex = -1;
        if (!selector.StartsWith("track:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var trackText = selector["track:".Length..];
        return int.TryParse(trackText, out var oneBasedTrackIndex)
            && oneBasedTrackIndex > 0
            && (trackArrayIndex = oneBasedTrackIndex - 1) >= 0;
    }

    private static int ExecuteMux(IReadOnlyList<string> args)
    {
        if (!TryValidateMuxArguments(args, out var outputFilePath, out var validationExitCode))
        {
            return validationExitCode;
        }

        var muxConfig = LoadMuxConfig(outputFilePath);
        foreach (var line in muxConfig.Lines)
        {
            Console.WriteLine(line);
            if (muxConfig.LineDelayMilliseconds > 0)
            {
                Thread.Sleep(muxConfig.LineDelayMilliseconds);
            }
        }

        if (muxConfig.DelayBeforeExitMilliseconds > 0)
        {
            Thread.Sleep(muxConfig.DelayBeforeExitMilliseconds);
        }

        if (muxConfig.CreateOutput)
        {
            var directory = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(outputFilePath, muxConfig.OutputContent);
        }

        return muxConfig.ExitCode;
    }

    private static bool TryValidateMuxArguments(
        IReadOnlyList<string> args,
        out string outputFilePath,
        out int exitCode)
    {
        outputFilePath = string.Empty;
        exitCode = 0;
        var inputFilePaths = new List<string>();

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (MuxOptionsWithValue.Contains(argument))
            {
                if (index + 1 >= args.Count || IsOptionToken(args[index + 1]))
                {
                    Console.Error.WriteLine($"FakeMkvMerge: Option '{argument}' hat keinen Wert.");
                    exitCode = 6;
                    return false;
                }

                var value = args[++index];
                if (string.Equals(argument, "--output", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = value;
                }
                else if (string.Equals(argument, "--attach-file", StringComparison.OrdinalIgnoreCase))
                {
                    inputFilePaths.Add(value);
                }

                continue;
            }

            if (MuxSwitchesWithoutValue.Contains(argument))
            {
                continue;
            }

            if (IsOptionToken(argument))
            {
                Console.Error.WriteLine($"FakeMkvMerge: Nicht unterstützte Mux-Option '{argument}'.");
                exitCode = 6;
                return false;
            }

            inputFilePaths.Add(argument);
        }

        if (string.IsNullOrWhiteSpace(outputFilePath))
        {
            Console.Error.WriteLine("FakeMkvMerge: --output fehlt.");
            exitCode = 2;
            return false;
        }

        if (inputFilePaths.Count == 0)
        {
            Console.Error.WriteLine("FakeMkvMerge: Eingabedatei fehlt.");
            exitCode = 6;
            return false;
        }

        foreach (var inputFilePath in inputFilePaths)
        {
            if (!File.Exists(inputFilePath))
            {
                Console.Error.WriteLine($"FakeMkvMerge: Eingabedatei fehlt: {inputFilePath}");
                exitCode = 7;
                return false;
            }
        }

        return true;
    }

    private static bool IsOptionToken(string value)
    {
        return value.StartsWith("--", StringComparison.Ordinal);
    }

    private static (int DelayBeforeOutputMilliseconds, string? InvocationLogFilePath, string Json) LoadProbeResponse(string inputFilePath)
    {
        var probeFilePath = inputFilePath + ".mkvmerge.json";
        if (!File.Exists(probeFilePath))
        {
            throw new FileNotFoundException($"FakeMkvMerge-Probe-Datei fehlt: {probeFilePath}");
        }

        var json = File.ReadAllText(probeFilePath);
        using var document = JsonDocument.Parse(json);
        var delayBeforeOutputMilliseconds = 0;
        string? invocationLogFilePath = null;
        if (document.RootElement.TryGetProperty("delayBeforeOutputMilliseconds", out var delayElement)
            && delayElement.ValueKind == JsonValueKind.Number)
        {
            delayBeforeOutputMilliseconds = delayElement.GetInt32();
        }

        if (document.RootElement.TryGetProperty("invocationLogFilePath", out var invocationLogElement)
            && invocationLogElement.ValueKind == JsonValueKind.String)
        {
            invocationLogFilePath = invocationLogElement.GetString();
        }

        return (
            delayBeforeOutputMilliseconds,
            invocationLogFilePath,
            json);
    }

    private static void WriteProbeInvocationLog(string? invocationLogFilePath, string inputFilePath)
    {
        if (string.IsNullOrWhiteSpace(invocationLogFilePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(invocationLogFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.AppendAllText(invocationLogFilePath, inputFilePath + Environment.NewLine);
    }

    private static FakeMuxRunConfiguration LoadMuxConfig(string outputFilePath)
    {
        var configFilePath = outputFilePath + ".mkvmerge.run.json";
        if (!File.Exists(configFilePath))
        {
            return FakeMuxRunConfiguration.Default;
        }

        var json = File.ReadAllText(configFilePath);
        var config = JsonSerializer.Deserialize<FakeMuxRunConfiguration>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return config ?? FakeMuxRunConfiguration.Default;
    }
}

internal sealed class FakeMuxRunConfiguration
{
    public static FakeMuxRunConfiguration Default { get; } = new()
    {
        ExitCode = 0,
        CreateOutput = true,
        OutputContent = "muxed by FakeMkvMerge",
        Lines =
        [
            "Progress: 5%",
            "Progress: 65%",
            "Progress: 100%"
        ]
    };

    public int ExitCode { get; init; }

    public bool CreateOutput { get; init; } = true;

    public string OutputContent { get; init; } = "muxed by FakeMkvMerge";

    public List<string> Lines { get; init; } = [];

    public int LineDelayMilliseconds { get; init; }

    public int DelayBeforeExitMilliseconds { get; init; }
}
