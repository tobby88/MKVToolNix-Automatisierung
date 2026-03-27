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
