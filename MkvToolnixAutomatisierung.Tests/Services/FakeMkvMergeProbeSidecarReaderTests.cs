using System.IO;
using System.Text.Json;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class FakeMkvMergeProbeSidecarReaderTests : IDisposable
{
    private readonly string _tempDirectory;

    public FakeMkvMergeProbeSidecarReaderTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-fake-sidecar-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void TryReadAttachmentTextContent_ReturnsNull_ForRealMkvMergePath()
    {
        var containerPath = CreateContainerWithSidecar(new
        {
            attachments = new object[]
            {
                new
                {
                    id = 12,
                    file_name = "episode.txt",
                    text_content = "Sender: Test"
                }
            }
        });

        var result = FakeMkvMergeProbeSidecarReader.TryReadAttachmentTextContent(
            @"C:\Tools\mkvmerge.exe",
            containerPath,
            new ContainerAttachmentMetadata(12, "episode.txt"));

        Assert.Null(result);
    }

    [Fact]
    public void TryReadAttachmentTextContent_ReadsMatchingAttachment_FromFakeMkvMergeSidecar()
    {
        var containerPath = CreateContainerWithSidecar(new
        {
            attachments = new object[]
            {
                new
                {
                    id = 7,
                    file_name = "cover.jpg"
                },
                new
                {
                    id = 8,
                    file_name = "episode.txt",
                    text_content = "Titel: Pilot"
                }
            }
        });

        var result = FakeMkvMergeProbeSidecarReader.TryReadAttachmentTextContent(
            @"C:\Tests\FakeMkvMerge.exe",
            containerPath,
            new ContainerAttachmentMetadata(8, "episode.txt"));

        Assert.Equal("Titel: Pilot", result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string CreateContainerWithSidecar(object sidecarContent)
    {
        var containerPath = Path.Combine(_tempDirectory, "episode.mkv");
        File.WriteAllText(containerPath, "container");
        File.WriteAllText(
            containerPath + ".mkvmerge.json",
            JsonSerializer.Serialize(sidecarContent));
        return containerPath;
    }
}
