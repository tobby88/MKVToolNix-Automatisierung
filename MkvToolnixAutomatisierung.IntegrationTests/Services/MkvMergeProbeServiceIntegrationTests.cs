using System.IO;
using System.Text.Json;
using MkvToolnixAutomatisierung.IntegrationTests.TestInfrastructure;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.IntegrationTests.Services;

public sealed class MkvMergeProbeServiceIntegrationTests : IDisposable
{
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

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
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
