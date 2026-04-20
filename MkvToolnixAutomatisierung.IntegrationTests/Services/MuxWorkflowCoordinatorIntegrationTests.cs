using System.IO;
using MkvToolnixAutomatisierung.IntegrationTests.TestInfrastructure;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.IntegrationTests.Services;

[Collection("PortableStorage")]
public sealed class MuxWorkflowCoordinatorIntegrationTests : IDisposable
{
    private readonly PortableStorageFixture _storageFixture;
    private readonly string _tempDirectory;

    public MuxWorkflowCoordinatorIntegrationTests(PortableStorageFixture storageFixture)
    {
        _storageFixture = storageFixture;
        _storageFixture.Reset();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-integration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task PrepareAndExecuteMuxAsync_UsesWorkingCopy_ParsesProgress_AndCleansTemporaryCopy()
    {
        var sourceVideoPath = CreateFile("episode.mp4");
        var outputPath = Path.Combine(_tempDirectory, "out", "Episode.mkv");
        var workingCopyDestination = Path.Combine(_tempDirectory, "work", "Episode - Arbeitskopie.mkv");
        FakeMkvMergeTestHelper.WriteProbeFile(
            sourceVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteMuxRunFile(
            outputPath,
            exitCode: 0,
            createOutput: true,
            outputContent: "muxed episode",
            lines:
            [
                "Progress: 12%",
                "Warning: Zusatzspur nicht optimal.",
                "Progress: 100%"
            ]);

        var muxService = CreateMuxService();
        var coordinator = new MuxWorkflowCoordinator(muxService, new FileCopyService(), new EpisodeCleanupService());
        var workingCopySource = CreateFile("existing-archive.mkv", "archive");
        var plan = new SeriesEpisodeMuxPlan(
            FakeMkvMergeTestHelper.ResolveExecutablePath(),
            outputPath,
            "Episode",
            [
                new VideoSourcePlan(sourceVideoPath, 0, "Deutsch - 1080p - H.264", IsDefaultTrack: true)
            ],
            [
                new AudioSourcePlan(sourceVideoPath, 1, "Deutsch - E-AC-3", IsDefaultTrack: true)
            ],
            [1],
            [],
            null,
            includePrimarySourceAttachments: false,
            attachmentSourcePath: null,
            null,
            audioDescriptionFilePath: null,
            audioDescriptionTrackId: null,
            audioDescriptionTrackName: null,
            audioDescriptionLanguageCode: null,
            subtitleFiles: [],
            attachmentFilePaths: [],
            preservedAttachmentNames: [],
            usageComparison: ArchiveUsageComparison.Empty,
            workingCopy: new FileCopyPlan(
                workingCopySource,
                workingCopyDestination,
                new FileInfo(workingCopySource).Length,
                File.GetLastWriteTimeUtc(workingCopySource)),
            notes: []);
        var copyUpdates = new List<WorkingCopyPreparationUpdate>();
        var muxUpdates = new List<MuxExecutionUpdate>();
        var outputLines = new List<string>();

        await coordinator.PrepareWorkingCopyAsync(plan, copyUpdates.Add);
        var result = await coordinator.ExecuteMuxAsync(plan, outputLines.Add, muxUpdates.Add);

        Assert.True(File.Exists(outputPath));
        Assert.False(File.Exists(workingCopyDestination));
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.HasWarning);
        Assert.Equal(100, result.LastProgressPercent);
        Assert.Contains(copyUpdates, update => update.ProgressPercent == 100);
        Assert.Contains(muxUpdates, update => update.ProgressPercent == 100 && update.HasWarning);
        Assert.Contains(outputLines, line => line.Contains("Warning:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteMuxAsync_CreatesMissingOutputDirectory_OnlyWhenExecutionStarts()
    {
        var sourceVideoPath = CreateFile("missing-output-dir-source.mp4");
        var outputPath = Path.Combine(_tempDirectory, "missing-output-dir", "Episode.mkv");
        FakeMkvMergeTestHelper.WriteProbeFile(
            sourceVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var muxService = CreateMuxService();
        var coordinator = new MuxWorkflowCoordinator(muxService, new FileCopyService(), new EpisodeCleanupService());
        var plan = new SeriesEpisodeMuxPlan(
            FakeMkvMergeTestHelper.ResolveExecutablePath(),
            outputPath,
            "Episode",
            [
                new VideoSourcePlan(sourceVideoPath, 0, "Deutsch - 1080p - H.264", IsDefaultTrack: true)
            ],
            [
                new AudioSourcePlan(sourceVideoPath, 1, "Deutsch - E-AC-3", IsDefaultTrack: true)
            ],
            [1],
            [],
            null,
            includePrimarySourceAttachments: false,
            attachmentSourcePath: null,
            null,
            audioDescriptionFilePath: null,
            audioDescriptionTrackId: null,
            audioDescriptionTrackName: null,
            audioDescriptionLanguageCode: null,
            subtitleFiles: [],
            attachmentFilePaths: [],
            preservedAttachmentNames: [],
            usageComparison: ArchiveUsageComparison.Empty,
            workingCopy: null,
            notes: []);

        Assert.False(Directory.Exists(Path.GetDirectoryName(outputPath)!));

        var result = await coordinator.ExecuteMuxAsync(plan);

        Assert.Equal(0, result.ExitCode);
        Assert.True(Directory.Exists(Path.GetDirectoryName(outputPath)!));
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public async Task ExecuteMuxAsync_HeaderEditOnly_UsesMkvPropEditWithoutWorkingCopy()
    {
        var outputPath = CreateFile("header-edit", "archive");
        FakeMkvMergeTestHelper.WriteProbeFileWithContainerTitle(
            outputPath,
            "Alter Episodentitel",
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", trackName: "Deutsch - FHD - H.264"),
            CreateAudioTrack(1, "E-AC-3", trackName: "Alter Audiotitel"));

        var muxService = CreateMuxService();
        var coordinator = new MuxWorkflowCoordinator(muxService, new FileCopyService(), new EpisodeCleanupService());
        var mkvPropEditPath = Path.Combine(
            Path.GetDirectoryName(FakeMkvMergeTestHelper.ResolveExecutablePath())
                ?? throw new DirectoryNotFoundException("Kein Verzeichnis für FakeMkvMerge gefunden."),
            "mkvpropedit.exe");
        var plan = new SeriesEpisodeMuxPlan(
            mkvMergePath: FakeMkvMergeTestHelper.ResolveExecutablePath(),
            outputFilePath: outputPath,
            title: "Episode",
            videoSources:
            [
                new VideoSourcePlan(outputPath, 0, "Deutsch - FHD - H.264", IsDefaultTrack: true)
            ],
            audioSources:
            [
                new AudioSourcePlan(outputPath, 1, "Deutsch - E-AC-3", IsDefaultTrack: true)
            ],
            primarySourceAudioTrackIds: [1],
            primarySourceSubtitleTrackIds: [],
            primarySourceAttachmentIds: null,
            includePrimarySourceAttachments: false,
            attachmentSourcePath: null,
            attachmentSourceAttachmentIds: null,
            audioDescriptionFilePath: null,
            audioDescriptionTrackId: null,
            audioDescriptionTrackName: null,
            audioDescriptionLanguageCode: null,
            subtitleFiles: [],
            attachmentFilePaths: [],
            preservedAttachmentNames: [],
            usageComparison: ArchiveUsageComparison.Empty,
            workingCopy: null,
            mkvPropEditPath: mkvPropEditPath,
            containerTitleEdit: new ContainerTitleEditOperation("Alter Episodentitel", "Episode"),
            trackHeaderEdits:
            [
                new TrackHeaderEditOperation("track:2", "Alter Audiotitel", "Alter Audiotitel", "Deutsch - E-AC-3")
            ],
            notes: ["Nur Header-Update"]);

        var result = await coordinator.ExecuteMuxAsync(plan);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.HasWarning);
        Assert.Equal("archive", File.ReadAllText(outputPath));
        var updatedProbeJson = File.ReadAllText(outputPath + ".mkvmerge.json");
        Assert.Contains("\"title\": \"Episode\"", updatedProbeJson, StringComparison.Ordinal);
        Assert.Contains("Deutsch - E-AC-3", updatedProbeJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Alter Audiotitel", updatedProbeJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteMuxAsync_CancellationStopsProcess_AndCleansTemporaryCopy()
    {
        var sourceVideoPath = CreateFile("cancel-source.mp4");
        var outputPath = Path.Combine(_tempDirectory, "cancel", "Episode.mkv");
        var workingCopySource = CreateFile("existing-cancel-archive.mkv", "archive");
        var workingCopyDestination = Path.Combine(_tempDirectory, "cancel-work", "Episode - Arbeitskopie.mkv");
        FakeMkvMergeTestHelper.WriteProbeFile(
            sourceVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteMuxRunFile(
            outputPath,
            exitCode: 0,
            createOutput: true,
            outputContent: "should not be written",
            delayBeforeExitMilliseconds: 5000,
            lines: ["Progress: 5%"]);

        var muxService = CreateMuxService();
        var coordinator = new MuxWorkflowCoordinator(muxService, new FileCopyService(), new EpisodeCleanupService());
        var plan = new SeriesEpisodeMuxPlan(
            FakeMkvMergeTestHelper.ResolveExecutablePath(),
            outputPath,
            "Episode",
            [
                new VideoSourcePlan(sourceVideoPath, 0, "Deutsch - 1080p - H.264", IsDefaultTrack: true)
            ],
            [
                new AudioSourcePlan(sourceVideoPath, 1, "Deutsch - E-AC-3", IsDefaultTrack: true)
            ],
            [1],
            [],
            null,
            includePrimarySourceAttachments: false,
            attachmentSourcePath: null,
            null,
            audioDescriptionFilePath: null,
            audioDescriptionTrackId: null,
            audioDescriptionTrackName: null,
            audioDescriptionLanguageCode: null,
            subtitleFiles: [],
            attachmentFilePaths: [],
            preservedAttachmentNames: [],
            usageComparison: ArchiveUsageComparison.Empty,
            workingCopy: new FileCopyPlan(
                workingCopySource,
                workingCopyDestination,
                new FileInfo(workingCopySource).Length,
                File.GetLastWriteTimeUtc(workingCopySource)),
            notes: []);

        await coordinator.PrepareWorkingCopyAsync(plan);

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.ExecuteMuxAsync(plan, cancellationToken: cancellationSource.Token));

        Assert.False(File.Exists(outputPath));
        Assert.False(File.Exists(workingCopyDestination));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private SeriesEpisodeMuxService CreateMuxService()
    {
        var settingsStore = new AppSettingsStore();
        new AppToolPathStore(settingsStore).Save(new AppToolPathSettings
        {
            MkvToolNixDirectoryPath = FakeMkvMergeTestHelper.ResolveExecutablePath()
        });

        var probeService = new MkvMergeProbeService();
        var archiveService = new SeriesArchiveService(probeService, new AppArchiveSettingsStore(settingsStore));

        return new SeriesEpisodeMuxService(
            new SeriesEpisodeMuxPlanner(
                new MkvToolNixLocator(new AppToolPathStore(settingsStore)),
                probeService,
                archiveService,
                new NullDurationProbe()),
            new MuxExecutionService(),
            new MkvMergeOutputParser());
    }

    private string CreateFile(string fileName, string content = "video")
    {
        var path = Path.Combine(_tempDirectory, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static object CreateVideoTrack(int id, string codec, string pixelDimensions, string trackName = "")
    {
        return new
        {
            id,
            type = "video",
            codec,
            properties = new
            {
                pixel_dimensions = pixelDimensions,
                language_ietf = "de",
                track_name = trackName
            }
        };
    }

    private static object CreateAudioTrack(int id, string codec, string trackName = "")
    {
        return new
        {
            id,
            type = "audio",
            codec,
            properties = new
            {
                language_ietf = "de",
                track_name = trackName
            }
        };
    }

    private sealed class NullDurationProbe : IMediaDurationProbe
    {
        public TimeSpan? TryReadDuration(string filePath)
        {
            return null;
        }
    }
}
