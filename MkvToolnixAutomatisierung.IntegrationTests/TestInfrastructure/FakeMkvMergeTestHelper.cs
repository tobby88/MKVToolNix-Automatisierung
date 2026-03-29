using System.IO;
using System.Text.Json;

namespace MkvToolnixAutomatisierung.IntegrationTests.TestInfrastructure;

internal static class FakeMkvMergeTestHelper
{
    public static string ResolveExecutablePath()
    {
        foreach (var configuration in new[] { "Release", "Debug" })
        {
            var repoExePath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "TestTools",
                "FakeMkvMerge",
                "bin",
                configuration,
                "net9.0",
                "FakeMkvMerge.exe"));
            if (File.Exists(repoExePath))
            {
                return repoExePath;
            }
        }

        throw new FileNotFoundException("FakeMkvMerge.exe wurde nicht gefunden.");
    }

    public static void WriteProbeFile(
        string mediaFilePath,
        params object[] tracks)
    {
        WriteJsonFile(
            mediaFilePath + ".mkvmerge.json",
            new
            {
                tracks,
                attachments = Array.Empty<object>()
            });
    }

    public static void WriteMuxRunFile(
        string outputFilePath,
        int exitCode = 0,
        bool createOutput = true,
        string? outputContent = null,
        int lineDelayMilliseconds = 0,
        int delayBeforeExitMilliseconds = 0,
        params string[] lines)
    {
        WriteJsonFile(
            outputFilePath + ".mkvmerge.run.json",
            new
            {
                exitCode,
                createOutput,
                outputContent = outputContent ?? "muxed by FakeMkvMerge",
                lineDelayMilliseconds,
                delayBeforeExitMilliseconds,
                lines = lines.Length == 0
                    ? new[]
                    {
                        "Progress: 10%",
                        "Progress: 100%"
                    }
                    : lines
            });
    }

    private static void WriteJsonFile(string filePath, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(
            filePath,
            JsonSerializer.Serialize(value, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
    }
}
