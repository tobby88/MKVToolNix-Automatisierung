using System.IO;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class FileCopyServiceTests : IDisposable
{
    private readonly string _tempDirectory;

    public FileCopyServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task CopyAsync_CopiesFileAndReportsProgress()
    {
        var sourcePath = Path.Combine(_tempDirectory, "source.bin");
        var destinationPath = Path.Combine(_tempDirectory, "nested", "copy.bin");
        var content = Enumerable.Range(0, 5000).Select(index => (byte)(index % 251)).ToArray();
        await File.WriteAllBytesAsync(sourcePath, content);
        var progressUpdates = new List<long>();
        var service = new FileCopyService();

        await service.CopyAsync(
            new FileCopyPlan(sourcePath, destinationPath, content.Length, File.GetLastWriteTimeUtc(sourcePath)),
            (copiedBytes, totalBytes) =>
            {
                Assert.Equal(content.Length, totalBytes);
                progressUpdates.Add(copiedBytes);
            });

        Assert.True(File.Exists(destinationPath));
        Assert.Equal(content, await File.ReadAllBytesAsync(destinationPath));
        Assert.NotEmpty(progressUpdates);
        Assert.Equal(content.Length, progressUpdates[^1]);
    }

    [Fact]
    public async Task CopyAsync_ReplacesExistingDestinationAfterSuccessfulCopy()
    {
        var sourcePath = Path.Combine(_tempDirectory, "source.bin");
        var destinationPath = Path.Combine(_tempDirectory, "copy.bin");
        var newContent = Enumerable.Range(0, 8000).Select(index => (byte)(index % 199)).ToArray();
        await File.WriteAllBytesAsync(sourcePath, newContent);
        await File.WriteAllTextAsync(destinationPath, "old");
        var service = new FileCopyService();

        await service.CopyAsync(
            new FileCopyPlan(sourcePath, destinationPath, newContent.Length, File.GetLastWriteTimeUtc(sourcePath)));

        Assert.Equal(newContent, await File.ReadAllBytesAsync(destinationPath));
    }

    [Fact]
    public async Task CopyAsync_WhenCancelled_KeepsExistingDestinationAndRemovesTemporaryCopy()
    {
        var sourcePath = Path.Combine(_tempDirectory, "source.bin");
        var destinationPath = Path.Combine(_tempDirectory, "copy.bin");
        await File.WriteAllBytesAsync(sourcePath, Enumerable.Repeat((byte)7, 2048).ToArray());
        await File.WriteAllTextAsync(destinationPath, "old");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new FileCopyService();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.CopyAsync(
                new FileCopyPlan(sourcePath, destinationPath, new FileInfo(sourcePath).Length, File.GetLastWriteTimeUtc(sourcePath)),
                cancellationToken: cancellation.Token));

        Assert.Equal("old", await File.ReadAllTextAsync(destinationPath));
        Assert.Empty(Directory.GetFiles(_tempDirectory, ".copy.bin.copy-*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
