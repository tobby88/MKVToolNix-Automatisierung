using System.Diagnostics;
using System.IO;
using Xunit;

namespace MkvToolnixAutomatisierung.IntegrationTests.TestInfrastructure;

public sealed class FakeMkvMergeTestHelperTests
{
    [Fact]
    public void ResolveExecutablePath_ReturnsMkvMergeExecutable()
    {
        var executablePath = FakeMkvMergeTestHelper.ResolveExecutablePath();

        Assert.EndsWith("mkvmerge.exe", executablePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FakeMkvMerge_RejectsMuxOptionsWithoutValue()
    {
        using var tempDirectory = new TempDirectory();
        var outputPath = Path.Combine(tempDirectory.Path, "out.mkv");

        var result = await RunFakeMkvMergeAsync("--output", outputPath, "--language");

        Assert.Equal(6, result.ExitCode);
        Assert.Contains("--language", result.StandardError, StringComparison.Ordinal);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task FakeMkvMerge_RejectsMissingMuxInputFiles()
    {
        using var tempDirectory = new TempDirectory();
        var outputPath = Path.Combine(tempDirectory.Path, "out.mkv");
        var missingSourcePath = Path.Combine(tempDirectory.Path, "missing.mp4");

        var result = await RunFakeMkvMergeAsync("--output", outputPath, "--title", "Episode", missingSourcePath);

        Assert.Equal(7, result.ExitCode);
        Assert.Contains("Eingabedatei fehlt", result.StandardError, StringComparison.Ordinal);
        Assert.False(File.Exists(outputPath));
    }

    private static async Task<ProcessRunResult> RunFakeMkvMergeAsync(params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo.FileName = FakeMkvMergeTestHelper.ResolveExecutablePath();
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new ProcessRunResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "mkv-auto-fake-mkvmerge-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateFile(string relativePath)
        {
            var filePath = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, "content");
            return filePath;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
