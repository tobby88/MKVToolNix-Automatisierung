using System.IO;
using System.Text.Json;

namespace MkvToolnixAutomatisierung.IntegrationTests.TestInfrastructure;

internal static class FakeMkvMergeTestHelper
{
    public static string ResolveExecutablePath()
    {
        var candidatePaths = new[] { "Release", "Debug" }
            .Select(configuration => Path.GetFullPath(Path.Combine(
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
                "FakeMkvMerge.exe")))
            .Where(File.Exists)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();

        if (candidatePaths.Count > 0)
        {
            return candidatePaths[0].FullName;
        }

        throw new FileNotFoundException("FakeMkvMerge.exe wurde nicht gefunden.");
    }

    public static void WriteProbeFile(
        string mediaFilePath,
        params object[] tracks)
    {
        WriteProbeFileWithAttachments(mediaFilePath, [], tracks);
    }

    public static void WriteProbeFileWithDelay(
        string mediaFilePath,
        int delayBeforeOutputMilliseconds,
        params object[] tracks)
    {
        WriteProbeFileWithAttachments(mediaFilePath, [], delayBeforeOutputMilliseconds, tracks);
    }

    public static void WriteProbeFileWithAttachments(
        string mediaFilePath,
        IReadOnlyList<object> attachments,
        params object[] tracks)
    {
        WriteProbeFileWithAttachments(mediaFilePath, attachments, delayBeforeOutputMilliseconds: 0, tracks);
    }

    public static void WriteProbeFileWithAttachments(
        string mediaFilePath,
        IReadOnlyList<object> attachments,
        int delayBeforeOutputMilliseconds,
        params object[] tracks)
    {
        WriteJsonFile(
            mediaFilePath + ".mkvmerge.json",
            new
            {
                delayBeforeOutputMilliseconds,
                tracks,
                attachments
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
