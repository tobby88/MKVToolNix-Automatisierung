using System.IO;
using MkvToolnixAutomatisierung.IntegrationTests.TestInfrastructure;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.IntegrationTests.Modules;

[Collection("PortableStorage")]
public sealed class SeriesEpisodeMuxServiceIntegrationTests : IDisposable
{
    private readonly PortableStorageFixture _storageFixture;
    private readonly string _tempDirectory;

    public SeriesEpisodeMuxServiceIntegrationTests(PortableStorageFixture storageFixture)
    {
        _storageFixture = storageFixture;
        _storageFixture.Reset();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-integration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task CreatePlanAsync_FreshTarget_UsesDetectedSources_AndCompanionFiles()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var primaryVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        var alternateVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-2.mp4");
        var audioDescriptionPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02) Audiodeskription.mp4");
        var subtitleSrtPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).srt");
        var subtitleAssPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).ass");
        var primaryAttachmentPath = CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02).txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");
        var alternateAttachmentPath = CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02)-2.txt",
            "Sender: ARD\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02) Audiodeskription.txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");

        FakeMkvMergeTestHelper.WriteProbeFile(
            primaryVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            alternateVideoPath,
            CreateVideoTrack(0, "HEVC/H.265", "1920x1080"),
            CreateAudioTrack(1, "AAC"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            audioDescriptionPath,
            CreateAudioTrack(0, "AAC"));

        var service = CreateMuxService(archiveDirectory);
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");

        var detected = await service.DetectFromSelectedVideoAsync(primaryVideoPath);
        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            detected.MainVideoPath,
            detected.AudioDescriptionPath,
            detected.SubtitlePaths,
            detected.AttachmentPaths,
            outputPath,
            detected.SuggestedTitle));

        Assert.Equal(primaryVideoPath, detected.MainVideoPath);
        Assert.Single(detected.AdditionalVideoPaths);
        Assert.Equal(alternateVideoPath, detected.AdditionalVideoPaths[0]);
        Assert.Equal(audioDescriptionPath, detected.AudioDescriptionPath);
        Assert.Equal(new[] { subtitleAssPath, subtitleSrtPath }, detected.SubtitlePaths);
        Assert.Equal(new[] { alternateAttachmentPath, primaryAttachmentPath }, detected.AttachmentPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));

        Assert.False(plan.SkipMux);
        Assert.Equal(outputPath, plan.OutputFilePath);
        Assert.Equal(2, plan.VideoSources.Count);
        Assert.Equal(primaryVideoPath, plan.VideoSources[0].FilePath);
        Assert.Equal(alternateVideoPath, plan.VideoSources[1].FilePath);
        Assert.Equal(audioDescriptionPath, plan.AudioDescriptionFilePath);
        Assert.Equal(0, plan.AudioDescriptionTrackId);
        Assert.Equal(new[] { subtitleAssPath, subtitleSrtPath }, plan.SubtitleFiles.Select(file => file.FilePath));
        Assert.Equal(new[] { alternateAttachmentPath, primaryAttachmentPath }, plan.AttachmentFilePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePlanAsync_RefreshesDetection_WhenAdditionalVideoAppearsAfterInitialScan()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-refresh");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-refresh");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var primaryVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02).txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");

        FakeMkvMergeTestHelper.WriteProbeFile(
            primaryVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var service = CreateMuxService(archiveDirectory);
        var detected = await service.DetectFromSelectedVideoAsync(primaryVideoPath);

        Assert.Empty(detected.AdditionalVideoPaths);

        var alternateVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-2.mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02)-2.txt",
            "Sender: ARD\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");
        FakeMkvMergeTestHelper.WriteProbeFile(
            alternateVideoPath,
            CreateVideoTrack(0, "HEVC/H.265", "1920x1080"),
            CreateAudioTrack(1, "AAC"));

        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            detected.MainVideoPath,
            detected.AudioDescriptionPath,
            detected.SubtitlePaths,
            detected.AttachmentPaths,
            outputPath,
            detected.SuggestedTitle));

        Assert.Equal(2, plan.VideoSources.Count);
        Assert.Equal(primaryVideoPath, plan.VideoSources[0].FilePath);
        Assert.Equal(alternateVideoPath, plan.VideoSources[1].FilePath);
    }

    [Fact]
    public async Task DetectFromSelectedVideoAsync_DoesNotReuseStaleSingleFileCache()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-detect-refresh");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-detect-refresh");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var primaryVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02).txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");

        FakeMkvMergeTestHelper.WriteProbeFile(
            primaryVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var service = CreateMuxService(archiveDirectory);
        var firstDetection = await service.DetectFromSelectedVideoAsync(primaryVideoPath);

        Assert.Empty(firstDetection.AdditionalVideoPaths);

        var alternateVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-2.mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02)-2.txt",
            "Sender: ARD\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");
        FakeMkvMergeTestHelper.WriteProbeFile(
            alternateVideoPath,
            CreateVideoTrack(0, "HEVC/H.265", "1920x1080"),
            CreateAudioTrack(1, "AAC"));

        var refreshedDetection = await service.DetectFromSelectedVideoAsync(primaryVideoPath);

        Assert.Single(refreshedDetection.AdditionalVideoPaths);
        Assert.Equal(alternateVideoPath, refreshedDetection.AdditionalVideoPaths[0]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private SeriesEpisodeMuxService CreateMuxService(string archiveDirectory)
    {
        var settingsStore = new AppSettingsStore();
        new AppToolPathStore(settingsStore).Save(new AppToolPathSettings
        {
            MkvToolNixDirectoryPath = FakeMkvMergeTestHelper.ResolveExecutablePath()
        });

        var probeService = new MkvMergeProbeService();
        var archiveService = new SeriesArchiveService(probeService, new AppArchiveSettingsStore(settingsStore));
        archiveService.ConfigureArchiveRootDirectory(archiveDirectory);

        return new SeriesEpisodeMuxService(
            new SeriesEpisodeMuxPlanner(
                new MkvToolNixLocator(new AppToolPathStore(settingsStore)),
                probeService,
                archiveService,
                new NullDurationProbe()),
            new MuxExecutionService(),
            new MkvMergeOutputParser());
    }

    private string CreateFile(string directory, string fileName, string content = "data")
    {
        var path = Path.Combine(directory, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static object CreateVideoTrack(int id, string codec, string pixelDimensions)
    {
        return new
        {
            id,
            type = "video",
            codec,
            properties = new
            {
                pixel_dimensions = pixelDimensions,
                language_ietf = "de"
            }
        };
    }

    private static object CreateAudioTrack(int id, string codec)
    {
        return new
        {
            id,
            type = "audio",
            codec,
            properties = new
            {
                language_ietf = "de"
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
