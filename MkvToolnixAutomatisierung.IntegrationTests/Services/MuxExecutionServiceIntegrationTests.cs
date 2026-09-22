using System.IO;
using MkvToolnixAutomatisierung.IntegrationTests.TestInfrastructure;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.IntegrationTests.Services;

public sealed class MuxExecutionServiceIntegrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mux-transaction-tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public async Task ExecuteAsync_OnlyPublishesSuccessfulMuxOutput(int exitCode, bool replacesTarget)
    {
        var (source, output) = CreateFiles();
        FakeMkvMergeTestHelper.WriteMuxRunFile(output, exitCode, createOutput: true, outputContent: "new archive");

        var result = await new MuxExecutionService().ExecuteAsync(
            FakeMkvMergeTestHelper.ResolveExecutablePath(), ["--output", output, source], "mkvmerge");

        Assert.Equal(exitCode, result);
        Assert.Equal(replacesTarget ? "new archive" : "old archive", await File.ReadAllTextAsync(output));
        Assert.Empty(Directory.GetDirectories(_directory));
    }

    [Fact]
    public async Task ExecuteAsync_CancellationLeavesExistingArchiveAndNoTemporaryMedia()
    {
        var (source, output) = CreateFiles();
        FakeMkvMergeTestHelper.WriteMuxRunFile(output, 0, createOutput: true, delayBeforeExitMilliseconds: 30_000);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MuxExecutionService().ExecuteAsync(
            FakeMkvMergeTestHelper.ResolveExecutablePath(), ["--output", output, source], "mkvmerge",
            _ => cancellation.Cancel(), cancellation.Token));

        Assert.Equal("old archive", await File.ReadAllTextAsync(output));
        Assert.Empty(Directory.GetDirectories(_directory));
    }

    [Fact]
    public async Task ExecuteAsync_OutputCallbackFailureStopsProcessAndPreservesArchive()
    {
        var (source, output) = CreateFiles();
        FakeMkvMergeTestHelper.WriteMuxRunFile(output, 0, createOutput: true, delayBeforeExitMilliseconds: 30_000);
        var expected = new InvalidOperationException("output consumer failed");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => new MuxExecutionService().ExecuteAsync(
            FakeMkvMergeTestHelper.ResolveExecutablePath(), ["--output", output, source], "mkvmerge",
            _ => throw expected, timeout.Token));

        Assert.Same(expected, actual);
        Assert.Equal("old archive", await File.ReadAllTextAsync(output));
        Assert.Empty(Directory.GetDirectories(_directory));
    }

    [Fact]
    public async Task ExecuteAsync_SuccessWithoutOutputDoesNotDestroyExistingArchive()
    {
        var (source, output) = CreateFiles();
        FakeMkvMergeTestHelper.WriteMuxRunFile(output, 0, createOutput: false);

        await Assert.ThrowsAsync<IOException>(() => new MuxExecutionService().ExecuteAsync(
            FakeMkvMergeTestHelper.ResolveExecutablePath(), ["--output", output, source], "mkvmerge"));

        Assert.Equal("old archive", await File.ReadAllTextAsync(output));
        Assert.Empty(Directory.GetDirectories(_directory));
    }

    private (string Source, string Output) CreateFiles()
    {
        Directory.CreateDirectory(_directory);
        var source = Path.Combine(_directory, "source.mp4");
        var output = Path.Combine(_directory, "episode.mkv");
        File.WriteAllText(source, "source");
        File.WriteAllText(output, "old archive");
        return (source, output);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
