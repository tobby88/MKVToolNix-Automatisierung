using System.Text.Json;

namespace FakeMkvMerge;

/// <summary>
/// Marker type so integration tests can locate the built helper executable.
/// </summary>
public static class FakeMkvMergeMarker
{
}

internal static class Program
{
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

    private static int ExecuteIdentify(string inputFilePath)
    {
        var probeResponse = LoadProbeResponse(inputFilePath);
        WriteProbeInvocationLog(probeResponse.InvocationLogFilePath, inputFilePath);
        if (probeResponse.DelayBeforeOutputMilliseconds > 0)
        {
            Thread.Sleep(probeResponse.DelayBeforeOutputMilliseconds);
        }

        Console.Write(probeResponse.Json);
        return 0;
    }

    private static int ExecuteAttachmentExtraction(IReadOnlyList<string> args)
    {
        var containerPath = args[1];
        var probeResponse = LoadProbeResponse(containerPath);
        using var document = JsonDocument.Parse(probeResponse.Json);

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

    private static int ExecuteMux(IReadOnlyList<string> args)
    {
        var outputFilePath = FindArgumentValue(args, "--output");
        if (string.IsNullOrWhiteSpace(outputFilePath))
        {
            Console.Error.WriteLine("FakeMkvMerge: --output fehlt.");
            return 2;
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

    private static FakeProbeResponse LoadProbeResponse(string inputFilePath)
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

        return new FakeProbeResponse(
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

    private static string? FindArgumentValue(IReadOnlyList<string> args, string argumentName)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], argumentName, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
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

internal sealed record FakeProbeResponse(
    int DelayBeforeOutputMilliseconds,
    string? InvocationLogFilePath,
    string Json);
