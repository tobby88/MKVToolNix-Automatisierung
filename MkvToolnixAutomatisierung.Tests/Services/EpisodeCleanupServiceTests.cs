using System.IO;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class EpisodeCleanupServiceTests : IDisposable
{
    private readonly string _tempDirectory;

    public EpisodeCleanupServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task MoveFilesToDirectoryAsync_DeduplicatesSources_AndUsesUniqueTargetNames()
    {
        var sourceA = CreateFile("a.txt", "first");
        var sourceB = CreateFile("b.txt", "second");
        var targetDirectory = Path.Combine(_tempDirectory, "done");
        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(Path.Combine(targetDirectory, "a.txt"), "existing");
        var progressCalls = new List<string>();
        var service = new EpisodeCleanupService();

        var result = await service.MoveFilesToDirectoryAsync(
            [sourceA, sourceA, sourceB, Path.Combine(_tempDirectory, "missing.txt")],
            targetDirectory,
            (current, total, filePath) => progressCalls.Add($"{current}/{total}:{Path.GetFileName(filePath)}"));

        Assert.Equal(2, result.MovedFiles.Count);
        Assert.Contains(Path.Combine(targetDirectory, "a (2).txt"), result.MovedFiles);
        Assert.Contains(Path.Combine(targetDirectory, "b.txt"), result.MovedFiles);
        Assert.Empty(result.FailedFiles);
        Assert.Equal(2, progressCalls.Count);
        Assert.False(File.Exists(sourceA));
        Assert.False(File.Exists(sourceB));
    }

    [Fact]
    public void DeleteTemporaryFile_AndDeleteDirectoryIfEmpty_RemoveOnlySafeTargets()
    {
        var service = new EpisodeCleanupService();
        var tempFile = CreateFile("temp.txt", "tmp");
        var emptyDirectory = Path.Combine(_tempDirectory, "empty");
        var nonEmptyDirectory = Path.Combine(_tempDirectory, "non-empty");
        Directory.CreateDirectory(emptyDirectory);
        Directory.CreateDirectory(nonEmptyDirectory);
        File.WriteAllText(Path.Combine(nonEmptyDirectory, "keep.txt"), "data");

        service.DeleteTemporaryFile(tempFile);
        service.DeleteDirectoryIfEmpty(emptyDirectory);
        service.DeleteDirectoryIfEmpty(nonEmptyDirectory);

        Assert.False(File.Exists(tempFile));
        Assert.False(Directory.Exists(emptyDirectory));
        Assert.True(Directory.Exists(nonEmptyDirectory));
    }

    [Fact]
    public void DeleteEmptyParentDirectories_RemovesNestedEmptyFolders_WithinRootOnly()
    {
        var service = new EpisodeCleanupService();
        var sourceRoot = Path.Combine(_tempDirectory, "source");
        var seriesDirectory = Path.Combine(sourceRoot, "Serie");
        var episodeDirectory = Path.Combine(seriesDirectory, "Episode");
        Directory.CreateDirectory(episodeDirectory);
        var movedSourceFile = Path.Combine(episodeDirectory, "episode.mp4");
        File.WriteAllText(movedSourceFile, "data");
        File.Delete(movedSourceFile);

        service.DeleteEmptyParentDirectories([movedSourceFile], sourceRoot);

        Assert.False(Directory.Exists(episodeDirectory));
        Assert.False(Directory.Exists(seriesDirectory));
        Assert.True(Directory.Exists(sourceRoot));
    }

    [Fact]
    public async Task MoveFilesToDirectoryAsync_ThrowsWhenCancellationAlreadyRequested()
    {
        var service = new EpisodeCleanupService();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.MoveFilesToDirectoryAsync(
            [CreateFile("cancel.txt", "data")],
            Path.Combine(_tempDirectory, "done"),
            cancellationToken: cancellationSource.Token));
    }

    [Fact]
    public async Task MoveFilesToDirectoryAsync_ReturnsPartialResult_WhenCancellationArrivesMidRun()
    {
        var service = new EpisodeCleanupService();
        var firstSource = CreateFile("first.txt", "one");
        var secondSource = CreateFile("second.txt", "two");
        var targetDirectory = Path.Combine(_tempDirectory, "done-partial");
        using var cancellationSource = new CancellationTokenSource();

        var result = await service.MoveFilesToDirectoryAsync(
            [firstSource, secondSource],
            targetDirectory,
            (current, _total, _filePath) =>
            {
                if (current == 1)
                {
                    cancellationSource.Cancel();
                }
            },
            cancellationSource.Token);

        Assert.True(result.WasCanceled);
        Assert.Single(result.MovedFiles);
        Assert.Empty(result.FailedFiles);
        Assert.Equal([secondSource], result.PendingFiles);
        Assert.False(File.Exists(firstSource));
        Assert.True(File.Exists(secondSource));
    }

    [Fact]
    public async Task RecycleFilesAsync_ReturnsPartialResult_WhenCancellationArrivesMidRun()
    {
        var service = new EpisodeCleanupService(
            static (sourceFilePath, destinationPath) => File.Move(sourceFilePath, destinationPath),
            static filePath => File.Delete(filePath));
        var firstSource = CreateFile("recycle-first.txt", "one");
        var secondSource = CreateFile("recycle-second.txt", "two");
        using var cancellationSource = new CancellationTokenSource();

        var result = await service.RecycleFilesAsync(
            [firstSource, secondSource],
            (current, _total, _filePath) =>
            {
                if (current == 1)
                {
                    cancellationSource.Cancel();
                }
            },
            cancellationSource.Token);

        Assert.True(result.WasCanceled);
        Assert.Equal([firstSource], result.RecycledFiles);
        Assert.Empty(result.FailedFiles);
        Assert.Equal([secondSource], result.PendingFiles);
        Assert.False(File.Exists(firstSource));
        Assert.True(File.Exists(secondSource));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string CreateFile(string name, string content)
    {
        var path = Path.Combine(_tempDirectory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
