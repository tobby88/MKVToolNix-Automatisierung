using System.IO;
using System.IO.Compression;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class ManagedToolArchiveExtractorTests : IDisposable
{
    private readonly string _tempDirectory;

    public ManagedToolArchiveExtractorTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "mkv-auto-managed-tool-archive-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void ExtractArchive_ExtractsZipEntriesAndReportsProgress()
    {
        var sourceDirectory = CreateDirectory("source");
        File.WriteAllText(Path.Combine(sourceDirectory, "ffprobe.exe"), "tool");
        Directory.CreateDirectory(Path.Combine(sourceDirectory, "nested"));
        File.WriteAllText(Path.Combine(sourceDirectory, "nested", "readme.txt"), "info");

        var archivePath = Path.Combine(_tempDirectory, "ffprobe.zip");
        ZipFile.CreateFromDirectory(sourceDirectory, archivePath);

        var destinationDirectory = Path.Combine(_tempDirectory, "extracted");
        var progressEvents = new List<ManagedToolExtractionProgress>();
        var extractor = new ManagedToolArchiveExtractor();

        extractor.ExtractArchive(archivePath, destinationDirectory, new CollectingProgress(progressEvents));

        Assert.True(File.Exists(Path.Combine(destinationDirectory, "ffprobe.exe")));
        Assert.True(File.Exists(Path.Combine(destinationDirectory, "nested", "readme.txt")));
        Assert.NotEmpty(progressEvents);
        Assert.Equal(0, progressEvents[0].ExtractedEntryCount);
        Assert.Equal(progressEvents[^1].TotalEntryCount, progressEvents[^1].ExtractedEntryCount);
        Assert.Contains(progressEvents, entry => string.Equals(entry.CurrentEntryPath, "ffprobe.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExtractArchive_ThrowsWhenArchiveIsCorrupt()
    {
        var archivePath = Path.Combine(_tempDirectory, "broken.zip");
        File.WriteAllText(archivePath, "not-a-real-archive");
        var destinationDirectory = Path.Combine(_tempDirectory, "broken-extracted");
        var extractor = new ManagedToolArchiveExtractor();

        var exception = Record.Exception(() => extractor.ExtractArchive(archivePath, destinationDirectory));

        Assert.NotNull(exception);
        Assert.Empty(Directory.EnumerateFileSystemEntries(destinationDirectory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(_tempDirectory, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class CollectingProgress(List<ManagedToolExtractionProgress> events) : IProgress<ManagedToolExtractionProgress>
    {
        public void Report(ManagedToolExtractionProgress value)
        {
            events.Add(value);
        }
    }
}
