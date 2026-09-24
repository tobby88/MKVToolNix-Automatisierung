using System.IO;
using System.Text.Json;
using MkvToolnixAutomatisierung.IntegrationTests.TestInfrastructure;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.IntegrationTests.Services;

public sealed class MkvMergeProbeServiceIntegrationTests : IDisposable
{
    [Fact]
    public async Task InvalidateDuringIdentifyDoesNotRepublishOldGeneration()
    {
        var media = CreateFile("generation.mp4");
        var started = Path.Combine(_tempDirectory, "invocations.log");
        FakeMkvMergeTestHelper.WriteProbeFileWithDelayAndInvocationLog(media, 1200, started,
            new { id = 0, type = "audio", codec = "AAC" });
        var service = new MkvMergeProbeService();
        var executable = FakeMkvMergeTestHelper.ResolveExecutablePath();
        var pending = service.ReadContainerMetadataAsync(executable, media);
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(started)) await Task.Delay(10, wait.Token);
        service.Invalidate(media);
        await pending;
        FakeMkvMergeTestHelper.WriteProbeFile(media, new { id = 0, type = "audio", codec = "AC-3" });
        var current = await service.ReadContainerMetadataAsync(executable, media);
        Assert.Equal("AC-3", Assert.Single(current.Tracks).CodecLabel);
    }

    [Fact]
    public async Task IdentifyBudgetTerminatesDelayedProcess()
    {
        var media = CreateFile("timeout.mp4");
        FakeMkvMergeTestHelper.WriteProbeFileWithDelay(media, 30000, new { id = 0, type = "audio", codec = "AAC" });
        var error = await Assert.ThrowsAsync<IOException>(() => MkvMergeIdentifyRunner.IdentifyAsync(
            FakeMkvMergeTestHelper.ResolveExecutablePath(), media, CancellationToken.None, TimeSpan.FromMilliseconds(200)));
        Assert.Contains("Zeitlimit", error.Message);
    }

    private readonly string _tempDirectory;

    public MkvMergeProbeServiceIntegrationTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-integration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task ReadFirstAudioTrackMetadataAsync_ReturnsFirstAudioTrackMetadata()
    {
        var mediaFilePath = CreateFile("audio-source.mp4");
        FakeMkvMergeTestHelper.WriteProbeFile(
            mediaFilePath,
            new
            {
                id = 0,
                type = "video",
                codec = "AVC/H.264",
                properties = new
                {
                    pixel_dimensions = "1920x1080",
                    language_ietf = "de"
                }
            },
            new
            {
                id = 1,
                type = "audio",
                codec = "E-AC-3",
                properties = new
                {
                    language_ietf = "de",
                    track_name = "Deutsch Hauptton"
                }
            },
            new
            {
                id = 2,
                type = "audio",
                codec = "AAC",
                properties = new
                {
                    language_ietf = "en",
                    flag_visual_impaired = true
                }
            });

        var service = new MkvMergeProbeService();
        var metadata = await service.ReadFirstAudioTrackMetadataAsync(
            FakeMkvMergeTestHelper.ResolveExecutablePath(),
            mediaFilePath);

        Assert.Equal(1, metadata.TrackId);
        Assert.Equal("E-AC-3", metadata.CodecLabel);
        Assert.Equal("de", metadata.Language);
        Assert.Equal("Deutsch Hauptton", metadata.TrackName);
        Assert.False(metadata.IsVisualImpaired);
    }

    [Fact]
    public async Task ReadContainerMetadataAsync_NormalizesMojibakeInTrackAndAttachmentNames()
    {
        var mediaFilePath = CreateFile("container-source.mkv");
        WriteProbeFile(
            mediaFilePath,
            new[]
            {
                new
                {
                    id = 0,
                    type = "audio",
                    codec = "AAC",
                    properties = new
                    {
                        language_ietf = "de",
                        track_name = "Gr\u00C3\u00BCn"
                    }
                }
            },
            new[]
            {
                new
                {
                    file_name = "Begleittext \u00C3\u009Cberblick.txt"
                }
            });

        var service = new MkvMergeProbeService();
        var metadata = await service.ReadContainerMetadataAsync(
            FakeMkvMergeTestHelper.ResolveExecutablePath(),
            mediaFilePath);

        Assert.Equal("Grün", Assert.Single(metadata.Tracks).TrackName);
        Assert.Equal("Begleittext Überblick.txt", Assert.Single(metadata.Attachments).FileName);
    }

    [Fact]
    public async Task ReadContainerMetadataAsync_HandlesNonAsciiInputPath()
    {
        var mediaFilePath = CreateFile("Grüne Folge.mkv");
        WriteProbeFile(
            mediaFilePath,
            new[]
            {
                new
                {
                    id = 0,
                    type = "audio",
                    codec = "AAC",
                    properties = new
                    {
                        language_ietf = "de",
                        track_name = "Deutsch"
                    }
                }
            },
            Array.Empty<object>());

        var service = new MkvMergeProbeService();
        var metadata = await service.ReadContainerMetadataAsync(
            FakeMkvMergeTestHelper.ResolveExecutablePath(),
            mediaFilePath);

        Assert.Equal("Deutsch", Assert.Single(metadata.Tracks).TrackName);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CachedProbeMethods_RespectPreCanceledToken()
    {
        var mediaPath = CreateFile("cached.mp4");
        FakeMkvMergeTestHelper.WriteProbeFile(mediaPath,
            new { id = 0, type = "video", codec = "AVC/H.264", properties = new { pixel_dimensions = "1920x1080" } },
            new { id = 1, type = "audio", codec = "AAC" });
        var service = new MkvMergeProbeService();
        var executable = FakeMkvMergeTestHelper.ResolveExecutablePath();
        await service.ReadPrimaryVideoMetadataAsync(executable, mediaPath);
        await service.ReadFirstAudioTrackMetadataAsync(executable, mediaPath);
        await service.ReadContainerMetadataAsync(executable, mediaPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadPrimaryVideoMetadataAsync(executable, mediaPath, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadFirstAudioTrackMetadataAsync(executable, mediaPath, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadContainerMetadataAsync(executable, mediaPath, cancellation.Token));
    }

    [Fact]
    public async Task ReadPrimaryVideoMetadataAsync_DoesNotCaptureCallingSynchronizationContext()
    {
        var mediaPath = CreateFile("context.mp4");
        FakeMkvMergeTestHelper.WriteProbeFile(mediaPath,
            new { id = 0, type = "video", codec = "AVC/H.264", properties = new { pixel_dimensions = "1920x1080" } },
            new { id = 1, type = "audio", codec = "AAC" });
        var context = new RecordingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        Task<MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux.MediaTrackMetadata> pending;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            pending = new MkvMergeProbeService().ReadPrimaryVideoMetadataAsync(FakeMkvMergeTestHelper.ResolveExecutablePath(), mediaPath);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, context.PostCount);
    }

    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;
        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref _postCount);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
    }

    private string CreateFile(string fileName, string content = "data")
    {
        var filePath = Path.Combine(_tempDirectory, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    private static void WriteProbeFile(string mediaFilePath, object tracks, object attachments)
    {
        var probeFilePath = mediaFilePath + ".mkvmerge.json";
        File.WriteAllText(
            probeFilePath,
            JsonSerializer.Serialize(new
            {
                tracks,
                attachments
            }, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
    }
}
