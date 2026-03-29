using System.IO;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class EpisodeEditModelManualCheckTests : IDisposable
{
    private readonly string _tempDirectory;

    public EpisodeEditModelManualCheckTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-episode-edit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void SetAudioDescription_RemovesObsoleteReviewTargetAndKeepsApprovedMainSource()
    {
        var mainVideoPath = CreateFile("main.mp4");
        var oldAudioDescriptionPath = CreateFile("old-ad.mp4");
        var newAudioDescriptionPath = CreateFile("new-ad.mp4");
        var item = new TestEpisodeEditModel(
            audioDescriptionPath: oldAudioDescriptionPath,
            requiresManualCheck: true,
            manualCheckFilePaths: [mainVideoPath, oldAudioDescriptionPath]);

        item.ApproveCurrentReviewTarget();

        Assert.Equal(oldAudioDescriptionPath, item.CurrentReviewTargetPath);

        item.SetAudioDescription(newAudioDescriptionPath);

        Assert.Equal([mainVideoPath], item.ManualCheckFilePaths);
        Assert.Null(item.CurrentReviewTargetPath);
        Assert.True(item.IsManualCheckApproved);
        Assert.True(item.RequiresManualCheck);
    }

    [Fact]
    public void SetAudioDescription_AddsNewReviewTargetWhenCompanionTextRequiresManualCheck()
    {
        var newAudioDescriptionPath = CreateFile("new-ad.mp4");
        File.WriteAllText(Path.ChangeExtension(newAudioDescriptionPath, ".txt"), "Sender: SRF");
        var item = new TestEpisodeEditModel(
            audioDescriptionPath: null,
            requiresManualCheck: false,
            manualCheckFilePaths: []);

        item.SetAudioDescription(newAudioDescriptionPath);

        Assert.True(item.RequiresManualCheck);
        Assert.False(item.IsManualCheckApproved);
        Assert.Equal([newAudioDescriptionPath], item.ManualCheckFilePaths);
        Assert.Equal(newAudioDescriptionPath, item.CurrentReviewTargetPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string CreateFile(string fileName)
    {
        var path = Path.Combine(_tempDirectory, fileName);
        File.WriteAllText(path, "data");
        return path;
    }

    private sealed class TestEpisodeEditModel : EpisodeEditModel
    {
        public TestEpisodeEditModel(string? audioDescriptionPath, bool requiresManualCheck, IReadOnlyList<string> manualCheckFilePaths)
            : base(
                requestedMainVideoPath: @"C:\Temp\requested.mp4",
                mainVideoPath: @"C:\Temp\main.mp4",
                localSeriesName: "Serie",
                localSeasonNumber: "01",
                localEpisodeNumber: "01",
                localTitle: "Pilot",
                seriesName: "Serie",
                seasonNumber: "01",
                episodeNumber: "01",
                additionalVideoPaths: [],
                audioDescriptionPath: audioDescriptionPath,
                subtitlePaths: [],
                attachmentPaths: [],
                relatedEpisodeFilePaths: [],
                outputPath: string.Empty,
                title: "Pilot",
                metadataStatusText: string.Empty,
                requiresMetadataReview: false,
                isMetadataReviewApproved: true,
                planSummaryText: string.Empty,
                usageSummary: null,
                requiresManualCheck: requiresManualCheck,
                manualCheckFilePaths: manualCheckFilePaths,
                notes: [])
        {
        }
    }
}
