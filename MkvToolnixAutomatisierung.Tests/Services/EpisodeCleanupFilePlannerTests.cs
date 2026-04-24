using System.IO;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

[Collection("PortableStorage")]
public sealed class EpisodeCleanupFilePlannerTests : IDisposable
{
    private readonly PortableStorageFixture _storageFixture;
    private readonly string _tempDirectory;

    public EpisodeCleanupFilePlannerTests(PortableStorageFixture storageFixture)
    {
        _storageFixture = storageFixture;
        _storageFixture.Reset();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void BuildCleanupFileList_FiltersOutput_WorkingCopy_ArchivePaths_AndOutsideRoot()
    {
        var archiveRoot = Path.Combine(_tempDirectory, "archive-root");
        var sourceRoot = Path.Combine(_tempDirectory, "source-root");
        Directory.CreateDirectory(archiveRoot);
        Directory.CreateDirectory(sourceRoot);

        var archiveService = new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(new AppSettingsStore()));
        archiveService.ConfigureArchiveRootDirectory(archiveRoot);
        var planner = new EpisodeCleanupFilePlanner(new EpisodeOutputPathService(archiveService));

        var keepFile = CreateFile(Path.Combine(sourceRoot, "episode.ass"));
        var outputFile = CreateFile(Path.Combine(sourceRoot, "episode.mkv"));
        var workingCopyFile = CreateFile(Path.Combine(sourceRoot, "work", "episode-copy.mkv"));
        var archiveFile = CreateFile(Path.Combine(archiveRoot, "Serie", "Season 1", "episode.mkv"));
        var outsideRootFile = CreateFile(Path.Combine(_tempDirectory, "outside", "episode.txt"));

        var cleanupFiles = planner.BuildCleanupFileList(
            [keepFile, keepFile, outputFile, workingCopyFile, archiveFile, outsideRootFile],
            outputFile,
            workingCopyPath: workingCopyFile,
            sourceRoot: sourceRoot);

        Assert.Single(cleanupFiles);
        Assert.Equal(keepFile, cleanupFiles[0]);
    }

    [Fact]
    public void BuildCleanupFileList_ExcludesRejectedSourceAndExactCompanions()
    {
        var sourceRoot = Path.Combine(_tempDirectory, "source-root");
        Directory.CreateDirectory(sourceRoot);

        var archiveService = new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(new AppSettingsStore()));
        var planner = new EpisodeCleanupFilePlanner(new EpisodeOutputPathService(archiveService));

        var selectedVideo = CreateFile(Path.Combine(sourceRoot, "selected.mp4"));
        var selectedText = CreateFile(Path.Combine(sourceRoot, "selected.txt"));
        var rejectedVideo = CreateFile(Path.Combine(sourceRoot, "rejected.mp4"));
        var rejectedText = CreateFile(Path.Combine(sourceRoot, "rejected.txt"));
        var rejectedSubtitle = CreateFile(Path.Combine(sourceRoot, "rejected.srt"));
        var outputFile = CreateFile(Path.Combine(sourceRoot, "output.mkv"));

        var cleanupFiles = planner.BuildCleanupFileList(
            [selectedVideo, selectedText, rejectedVideo, rejectedText, rejectedSubtitle],
            outputFile,
            sourceRoot: sourceRoot,
            excludedSourcePaths: [rejectedVideo]);

        Assert.Contains(selectedVideo, cleanupFiles);
        Assert.Contains(selectedText, cleanupFiles);
        Assert.DoesNotContain(rejectedVideo, cleanupFiles);
        Assert.DoesNotContain(rejectedText, cleanupFiles);
        Assert.DoesNotContain(rejectedSubtitle, cleanupFiles);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private static string CreateFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "data");
        return path;
    }
}
