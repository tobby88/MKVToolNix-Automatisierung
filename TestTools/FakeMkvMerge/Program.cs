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
        if (args.Count >= 4
            && string.Equals(args[0], "--identify", StringComparison.OrdinalIgnoreCase)
            && string.Equals(args[1], "--identification-format", StringComparison.OrdinalIgnoreCase)
            && string.Equals(args[2], "json", StringComparison.OrdinalIgnoreCase))
        {
            var inputFilePath = args[^1];
            var probeResponse = LoadProbeResponse(inputFilePath);
            WriteProbeInvocationLog(probeResponse.InvocationLogFilePath, inputFilePath);
            if (probeResponse.DelayBeforeOutputMilliseconds > 0)
            {
                Thread.Sleep(probeResponse.DelayBeforeOutputMilliseconds);
            }

            Console.Write(probeResponse.Json);
            return 0;
        }

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
