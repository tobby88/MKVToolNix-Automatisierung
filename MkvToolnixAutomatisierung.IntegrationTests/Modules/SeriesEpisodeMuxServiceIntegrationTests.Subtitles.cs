using System.IO;
using MkvToolnixAutomatisierung.IntegrationTests.TestInfrastructure;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using Xunit;

namespace MkvToolnixAutomatisierung.IntegrationTests.Modules;

public sealed partial class SeriesEpisodeMuxServiceIntegrationTests
{
    [Fact]
    public async Task CreatePlanAsync_ExternalSubtitles_RemainGerman_WhenPrimaryAudioUsesDifferentLanguage()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-subtitle-language");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-subtitle-language");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        var subtitlePath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).srt", "subtitle");
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");

        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "AAC", language: "en"));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            mainVideoPath,
            AudioDescriptionPath: null,
            SubtitlePaths: [subtitlePath],
            AttachmentPaths: [],
            outputPath,
            Title: "Pilot"));

        var subtitle = Assert.Single(plan.SubtitleFiles);
        Assert.Equal("de", subtitle.LanguageCode);

        var arguments = plan.BuildArguments();
        AssertContainsSequence(arguments, "--language", "0:de");
        Assert.DoesNotContain("0:en", arguments);
    }

    [Fact]
    public async Task CreatePlanAsync_KeepingArchivePrimary_RemovesUnsupportedSubtitleTracks_WhenAnotherChangeRequiresMux()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-remove-unsupported-subtitle");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-remove-unsupported-subtitle");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var lowerQualityVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        var manualAttachmentPath = CreateFile(sourceDirectory, "manual.txt", "manual");
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");

        FakeMkvMergeTestHelper.WriteProbeFile(
            lowerQualityVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1280x720", language: "de"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            outputPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "de"),
            CreateAudioTrack(1, "E-AC-3"),
            CreateSubtitleTrack(2, "HDMV PGS", trackName: "Deutsch - PGS"));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            lowerQualityVideoPath,
            AudioDescriptionPath: null,
            SubtitlePaths: [],
            AttachmentPaths: [manualAttachmentPath],
            outputPath,
            Title: "Pilot",
            ManualAttachmentPaths: [manualAttachmentPath]));

        Assert.False(plan.SkipMux);
        Assert.Empty(plan.PrimarySourceSubtitleTrackIds!);
        Assert.Contains("--no-subtitles", plan.BuildArguments());
        Assert.Contains("Deutsch - PGS", plan.BuildUsageSummary().Subtitles.RemovedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareAsync_PrefersExistingEmbeddedSubtitle_WhenArchiveAlreadyContainsSameKind()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-archive-subtitles");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-archive-subtitles");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        var externalSubtitlePath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).srt", "subtitle");
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");

        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1280x720"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            outputPath,
            CreateVideoTrack(0, "AVC/H.264", "1280x720"),
            CreateAudioTrack(1, "E-AC-3"),
            CreateSubtitleTrack(3, "SubRip/SRT"));

        var archiveService = CreateArchiveService(archiveDirectory);

        var decision = await archiveService.PrepareAsync(
            FakeMkvMergeTestHelper.ResolveExecutablePath(),
            new SeriesEpisodeMuxRequest(
                mainVideoPath,
                AudioDescriptionPath: null,
                SubtitlePaths: [externalSubtitlePath],
                AttachmentPaths: [],
                outputPath,
                Title: "Pilot"),
            [mainVideoPath]);

        Assert.DoesNotContain(decision.SubtitleFiles, subtitle => !subtitle.IsEmbedded && string.Equals(subtitle.FilePath, externalSubtitlePath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(decision.SubtitleFiles, subtitle => subtitle.IsEmbedded && subtitle.EmbeddedTrackId == 3);
    }

    [Fact]
    public async Task PrepareAsync_KeepsEmbeddedSubtitle_WhenExternalSubtitleUsesDifferentLanguage()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-archive-subtitles-language");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-archive-subtitles-language");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        var externalSubtitlePath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).srt", "subtitle");
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");

        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1280x720"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            outputPath,
            CreateVideoTrack(0, "AVC/H.264", "1280x720"),
            CreateAudioTrack(1, "E-AC-3"),
            CreateSubtitleTrack(3, "SubRip/SRT", language: "en"));

        var archiveService = CreateArchiveService(archiveDirectory);

        var decision = await archiveService.PrepareAsync(
            FakeMkvMergeTestHelper.ResolveExecutablePath(),
            new SeriesEpisodeMuxRequest(
                mainVideoPath,
                AudioDescriptionPath: null,
                SubtitlePaths: [externalSubtitlePath],
                AttachmentPaths: [],
                outputPath,
                Title: "Pilot"),
            [mainVideoPath]);

        Assert.Contains(decision.SubtitleFiles, subtitle => !subtitle.IsEmbedded && string.Equals(subtitle.FilePath, externalSubtitlePath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(decision.SubtitleFiles, subtitle => subtitle.IsEmbedded && subtitle.EmbeddedTrackId == 3 && subtitle.LanguageCode == "en");
    }

    [Fact]
    public async Task DetectFromSelectedVideoAsync_DeduplicatesSubtitleKinds_AcrossSelectedVideoSources()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-subtitle-dedup");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-subtitle-dedup");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var primaryVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        var alternateVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-2.mp4");
        var primarySubtitlePath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).srt", "subtitle-primary");
        CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-2.srt", "subtitle-alternate");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02).txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02)-2.txt",
            "Sender: ARD\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");

        FakeMkvMergeTestHelper.WriteProbeFile(
            primaryVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            alternateVideoPath,
            CreateVideoTrack(0, "HEVC/H.265", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var service = CreateMuxService(archiveDirectory);

        var detected = await service.DetectFromSelectedVideoAsync(primaryVideoPath);

        Assert.Single(detected.SubtitlePaths);
        Assert.Equal(primarySubtitlePath, detected.SubtitlePaths[0]);
    }

    [Fact]
    public async Task CreatePlanAsync_KeepingArchivePrimary_UsageSummary_KeepsExistingSrt_AndAddsMissingAss()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-usage-keep");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-usage-keep");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        var subtitleSrtPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).srt", "subtitle-srt");
        var subtitleAssPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).ass", "subtitle-ass");
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");

        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1280x720"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            outputPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"),
            CreateSubtitleTrack(3, "SubRip/SRT", trackName: "Deutsch (hörgeschädigte) - SRT", isHearingImpaired: true));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            mainVideoPath,
            AudioDescriptionPath: null,
            SubtitlePaths: [subtitleAssPath, subtitleSrtPath],
            AttachmentPaths: [],
            outputPath,
            Title: "Pilot"));

        var summary = plan.BuildUsageSummary();

        Assert.False(summary.MainVideo.HasRemoved);
        Assert.False(summary.Subtitles.HasRemoved);
        Assert.Contains("Aus Zieldatei: Deutsch (hörgeschädigte) - SRT", summary.Subtitles.CurrentText, StringComparison.Ordinal);
        Assert.Contains(plan.SubtitleFiles, subtitle => subtitle.IsEmbedded && subtitle.EmbeddedTrackId == 3);
        Assert.Contains(plan.SubtitleFiles, subtitle => !subtitle.IsEmbedded && string.Equals(subtitle.FilePath, subtitleAssPath, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.SubtitleFiles, subtitle => !subtitle.IsEmbedded && string.Equals(subtitle.FilePath, subtitleSrtPath, StringComparison.OrdinalIgnoreCase));

        var arguments = plan.BuildArguments();
        AssertContainsSequence(
            arguments,
            "--subtitle-tracks",
            "3",
            "--language",
            "3:de",
            "--track-name",
            "3:Deutsch (hörgeschädigte) - SRT",
            "--default-track-flag",
            "3:no",
            "--hearing-impaired-flag",
            "3:yes",
            "--original-flag",
            "3:yes");
    }
}
