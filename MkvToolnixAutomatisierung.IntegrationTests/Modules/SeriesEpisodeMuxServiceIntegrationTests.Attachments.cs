using System.IO;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.IntegrationTests.TestInfrastructure;
using Xunit;

namespace MkvToolnixAutomatisierung.IntegrationTests.Modules;

public sealed partial class SeriesEpisodeMuxServiceIntegrationTests
{
    [Fact]
    public async Task CreatePlanAsync_ReplacingSingleArchiveVideo_DropsOnlyTheUnambiguousSingleTextAttachment()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-archive-single-text-drop");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-archive-single-text-drop");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var newPrimaryVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        var newPrimaryAttachmentPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).txt", "new-primary");
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");

        FakeMkvMergeTestHelper.WriteProbeFile(
            newPrimaryVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "de"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFileWithAttachments(
            outputPath,
            [CreateAttachment("alte-spur.txt", id: 10), CreateAttachment("cover.jpg", id: 11)],
            CreateVideoTrack(0, "AVC/H.264", "1280x720", language: "de"),
            CreateAudioTrack(1, "E-AC-3"));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            newPrimaryVideoPath,
            AudioDescriptionPath: null,
            SubtitlePaths: [],
            AttachmentPaths: [newPrimaryAttachmentPath],
            outputPath,
            Title: "Pilot"));

        Assert.False(plan.SkipMux);
        Assert.Equal(outputPath, plan.AttachmentSourcePath);
        Assert.Equal(["cover.jpg"], plan.PreservedAttachmentNames);
        Assert.Contains(newPrimaryAttachmentPath, plan.AttachmentFilePaths);

        var arguments = plan.BuildArguments();
        var runtimeArchivePath = plan.WorkingCopy!.DestinationFilePath;
        AssertContainsSequence(arguments, "--attachments", "11", runtimeArchivePath);
        Assert.DoesNotContain("10", arguments);
        Assert.Contains("Aus Zieldatei: cover.jpg", plan.BuildUsageSummary().Attachments.CurrentText, StringComparison.Ordinal);
        Assert.DoesNotContain("alte-spur.txt", plan.BuildUsageSummary().Attachments.CurrentText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreatePlanAsync_ReplacingSingleArchiveVideo_KeepsSingleTextAttachment_WhenNoFreshTextExists()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-archive-single-text-keep-without-replacement");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-archive-single-text-keep-without-replacement");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var newPrimaryVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");

        FakeMkvMergeTestHelper.WriteProbeFile(
            newPrimaryVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "de"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFileWithAttachments(
            outputPath,
            [CreateAttachment("alte-spur.txt", id: 10), CreateAttachment("cover.jpg", id: 11)],
            CreateVideoTrack(0, "AVC/H.264", "1280x720", language: "de"),
            CreateAudioTrack(1, "E-AC-3"));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            newPrimaryVideoPath,
            AudioDescriptionPath: null,
            SubtitlePaths: [],
            AttachmentPaths: [],
            outputPath,
            Title: "Pilot"));

        Assert.False(plan.SkipMux);
        Assert.Equal(outputPath, plan.AttachmentSourcePath);
        Assert.Equal(["alte-spur.txt", "cover.jpg"], plan.PreservedAttachmentNames);
        Assert.Empty(plan.AttachmentFilePaths);

        var arguments = plan.BuildArguments();
        var runtimeArchivePath = plan.WorkingCopy!.DestinationFilePath;
        AssertContainsSequence(arguments, "--attachments", "10,11", runtimeArchivePath);
    }

    [Fact]
    public async Task CreatePlanAsync_ReplacingArchiveVideo_KeepsExistingTextAttachments_WhenMappingIsAmbiguous()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-archive-ambiguous-text-keep");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-archive-ambiguous-text-keep");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var freshGermanH264Path = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        var freshGermanAttachmentPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).txt", "fresh-primary");
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");

        FakeMkvMergeTestHelper.WriteProbeFile(
            freshGermanH264Path,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "de"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFileWithAttachments(
            outputPath,
            [CreateAttachment("spur-a.txt", id: 20), CreateAttachment("spur-b.txt", id: 21)],
            CreateVideoTrack(0, "AVC/H.264", "1280x720", language: "de"),
            CreateAudioTrack(1, "E-AC-3"),
            CreateVideoTrack(2, "HEVC/H.265", "1280x720", language: "de"));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            freshGermanH264Path,
            AudioDescriptionPath: null,
            SubtitlePaths: [],
            AttachmentPaths: [freshGermanAttachmentPath],
            outputPath,
            Title: "Pilot"));

        Assert.False(plan.SkipMux);
        Assert.Equal([freshGermanH264Path, outputPath], plan.VideoSources.Select(source => source.FilePath).Distinct().ToList());
        Assert.Contains(plan.VideoSources, source => string.Equals(source.FilePath, outputPath, StringComparison.OrdinalIgnoreCase) && source.TrackId == 2);
        Assert.Equal(["spur-a.txt", "spur-b.txt"], plan.PreservedAttachmentNames);

        var arguments = plan.BuildArguments();
        var runtimeArchivePath = plan.WorkingCopy!.DestinationFilePath;
        AssertContainsSequence(arguments, "--attachments", "20,21", runtimeArchivePath);
        Assert.Contains("Aus Zieldatei: spur-a.txt", plan.BuildUsageSummary().Attachments.CurrentText, StringComparison.Ordinal);
        Assert.Contains("Aus Zieldatei: spur-b.txt", plan.BuildUsageSummary().Attachments.CurrentText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatePlanAsync_ReplacingArchiveVideo_DropsOnlyTextAttachment_WithUniqueContentMatchToRemovedTrack()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-archive-content-matched-text-drop");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-archive-content-matched-text-drop");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var freshGermanH264Path = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        var freshGermanAttachmentPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).txt", "fresh-primary");
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");

        FakeMkvMergeTestHelper.WriteProbeFile(
            freshGermanH264Path,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "de"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFileWithAttachments(
            outputPath,
            [
                CreateAttachment(
                    "de-alt.txt",
                    id: 30,
                    textContent: BuildAttachmentTextContent(
                        "Pilot",
                        "https://media.example.invalid/beispielserie-pilot-hd.mp4")),
                CreateAttachment(
                    "nds-hevc.txt",
                    id: 31,
                    textContent: BuildAttachmentTextContent(
                        "Büttenwarder op Platt: Pilot",
                        "https://media.example.invalid/beispielserie-pilot-1080.hevc.mp4")),
                CreateAttachment(
                    "nds-h264.txt",
                    id: 32,
                    textContent: BuildAttachmentTextContent(
                        "Büttenwarder op Platt: Pilot",
                        "https://media.example.invalid/beispielserie-pilot-hd.mp4"))
            ],
            CreateVideoTrack(0, "AVC/H.264", "1280x720", language: "de"),
            CreateAudioTrack(1, "E-AC-3"),
            CreateVideoTrack(2, "HEVC/H.265", "1920x1080", language: "nds"),
            CreateVideoTrack(3, "AVC/H.264", "1280x720", language: "nds"));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            freshGermanH264Path,
            AudioDescriptionPath: null,
            SubtitlePaths: [],
            AttachmentPaths: [freshGermanAttachmentPath],
            outputPath,
            Title: "Pilot"));

        Assert.False(plan.SkipMux);
        Assert.Equal(["nds-hevc.txt", "nds-h264.txt"], plan.PreservedAttachmentNames);
        Assert.Contains(plan.VideoSources, source => string.Equals(source.FilePath, outputPath, StringComparison.OrdinalIgnoreCase) && source.TrackId == 2);
        Assert.Contains(plan.VideoSources, source => string.Equals(source.FilePath, outputPath, StringComparison.OrdinalIgnoreCase) && source.TrackId == 3);

        var arguments = plan.BuildArguments();
        var runtimeArchivePath = plan.WorkingCopy!.DestinationFilePath;
        AssertContainsSequence(arguments, "--attachments", "31,32", runtimeArchivePath);
        Assert.DoesNotContain("30", arguments);
        Assert.DoesNotContain("de-alt.txt", plan.BuildUsageSummary().Attachments.CurrentText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Aus Zieldatei: nds-hevc.txt", plan.BuildUsageSummary().Attachments.CurrentText, StringComparison.Ordinal);
        Assert.Contains("Aus Zieldatei: nds-h264.txt", plan.BuildUsageSummary().Attachments.CurrentText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatePlanAsync_ReplacingArchiveVideo_KeepsTextAttachments_WhenContentOnlyIdentifiesSharedLanguage()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-archive-language-only-text-keep");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-archive-language-only-text-keep");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var freshPlattH264Path = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-nds.mp4");
        var freshPlattAttachmentPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-nds.txt", "fresh-platt");
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");

        FakeMkvMergeTestHelper.WriteProbeFile(
            freshPlattH264Path,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "nds"),
            CreateAudioTrack(1, "E-AC-3", language: "nds"));
        FakeMkvMergeTestHelper.WriteProbeFileWithAttachments(
            outputPath,
            [
                CreateAttachment(
                    "spur-a.txt",
                    id: 40,
                    textContent: BuildAttachmentTextContent("Büttenwarder op Platt: Pilot")),
                CreateAttachment(
                    "spur-b.txt",
                    id: 41,
                    textContent: BuildAttachmentTextContent("Büttenwarder op Platt: Pilot"))
            ],
            CreateVideoTrack(0, "AVC/H.264", "1280x720", language: "nds"),
            CreateAudioTrack(1, "E-AC-3", language: "nds"),
            CreateVideoTrack(2, "HEVC/H.265", "1920x1080", language: "nds"));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            freshPlattH264Path,
            AudioDescriptionPath: null,
            SubtitlePaths: [],
            AttachmentPaths: [freshPlattAttachmentPath],
            outputPath,
            Title: "Pilot"));

        Assert.False(plan.SkipMux);
        Assert.Equal(["spur-a.txt", "spur-b.txt"], plan.PreservedAttachmentNames);
        Assert.Contains(plan.VideoSources, source => string.Equals(source.FilePath, outputPath, StringComparison.OrdinalIgnoreCase) && source.TrackId == 2);

        var arguments = plan.BuildArguments();
        var runtimeArchivePath = plan.WorkingCopy!.DestinationFilePath;
        AssertContainsSequence(arguments, "--attachments", "40,41", runtimeArchivePath);
        Assert.Contains("Aus Zieldatei: spur-a.txt", plan.BuildUsageSummary().Attachments.CurrentText, StringComparison.Ordinal);
        Assert.Contains("Aus Zieldatei: spur-b.txt", plan.BuildUsageSummary().Attachments.CurrentText, StringComparison.Ordinal);
    }
}
