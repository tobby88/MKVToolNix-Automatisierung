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
    public async Task CreatePlanAsync_DoesNotCreateMissingOutputDirectory_WhenOnlyBuildingPlan()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-preview-only");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-preview-only");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var primaryVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        FakeMkvMergeTestHelper.WriteProbeFile(
            primaryVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var service = CreateMuxService(archiveDirectory);
        var outputDirectory = Path.Combine(archiveDirectory, "Beispielserie", "Season 1");
        var outputPath = Path.Combine(outputDirectory, "Beispielserie - S01E02 - Pilot.mkv");

        Assert.False(Directory.Exists(outputDirectory));

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            primaryVideoPath,
            AudioDescriptionPath: null,
            SubtitlePaths: [],
            AttachmentPaths: [],
            outputPath,
            Title: "Pilot"));

        Assert.False(plan.SkipMux);
        Assert.Equal(outputPath, plan.OutputFilePath);
        Assert.False(Directory.Exists(outputDirectory));
    }

    [Fact]
    public async Task CreatePlanAsync_ExistingCustomTargetOutsideArchiveRoot_IsOverwrittenWithoutArchiveReuse()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-custom-target");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-custom-target");
        var customOutputDirectory = Path.Combine(_tempDirectory, "custom-output");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);
        Directory.CreateDirectory(customOutputDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        var subtitlePath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).srt", "subtitle");
        var outputPath = Path.Combine(customOutputDirectory, "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(customOutputDirectory, Path.GetFileName(outputPath), "existing-custom-target");

        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1280x720"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFileWithAttachments(
            outputPath,
            [CreateAttachment("cover.jpg")],
            CreateVideoTrack(0, "HEVC/H.265", "1920x1080"),
            CreateAudioTrack(1, "AAC"),
            CreateSubtitleTrack(3, "SubRip/SRT", trackName: "Deutsch (hörgeschädigte) - SRT", isHearingImpaired: true));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            mainVideoPath,
            AudioDescriptionPath: null,
            SubtitlePaths: [subtitlePath],
            AttachmentPaths: [],
            outputPath,
            Title: "Pilot"));

        Assert.False(plan.SkipMux);
        Assert.Equal(outputPath, plan.OutputFilePath);
        Assert.Equal(mainVideoPath, plan.VideoSources[0].FilePath);
        Assert.Null(plan.WorkingCopy);
        Assert.Null(plan.AttachmentSourcePath);
        Assert.Empty(plan.PreservedAttachmentNames);
        Assert.Single(plan.SubtitleFiles);
        Assert.False(plan.SubtitleFiles[0].IsEmbedded);
        Assert.Equal(subtitlePath, plan.SubtitleFiles[0].FilePath);
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

    [Fact]
    public async Task CreatePlanAsync_RespectsExcludedSourcePaths_WhenDetectionIsRebuiltForPreviewOrMux()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-exclusions");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-exclusions");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var lowerQualityPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-a.mp4");
        var preferredButRejectedPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-b.mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02)-a.txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02)-b.txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");

        FakeMkvMergeTestHelper.WriteProbeFile(
            lowerQualityPath,
            CreateVideoTrack(0, "AVC/H.264", "1280x720"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            preferredButRejectedPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var service = CreateMuxService(archiveDirectory);
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            lowerQualityPath,
            AudioDescriptionPath: null,
            SubtitlePaths: [],
            AttachmentPaths: [],
            outputPath,
            Title: "Pilot",
            ExcludedSourcePaths:
            [
                preferredButRejectedPath
            ]));

        Assert.False(plan.SkipMux);
        Assert.Single(plan.VideoSources);
        Assert.Equal(lowerQualityPath, plan.VideoSources[0].FilePath);
        Assert.DoesNotContain(plan.VideoSources, source => string.Equals(source.FilePath, preferredButRejectedPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePlanAsync_ReplacingArchivePrimary_PreservesArchiveAttachments_AlongsideReusedArchiveTracks()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-archive-attachments");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-archive-attachments");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var newPrimaryVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02).txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");

        FakeMkvMergeTestHelper.WriteProbeFile(
            newPrimaryVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");
        FakeMkvMergeTestHelper.WriteProbeFileWithAttachments(
            outputPath,
            [CreateAttachment("cover.jpg"), CreateAttachment("notes.txt")],
            CreateVideoTrack(0, "AVC/H.264", "1280x720"),
            CreateAudioTrack(1, "E-AC-3"),
            CreateAudioTrack(2, "AAC", trackName: "Audiodeskription", isVisualImpaired: true),
            CreateSubtitleTrack(3, "SubRip/SRT", isHearingImpaired: true));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            newPrimaryVideoPath,
            AudioDescriptionPath: null,
            SubtitlePaths: [],
            AttachmentPaths: [],
            outputPath,
            Title: "Pilot"));

        Assert.False(plan.SkipMux);
        Assert.NotNull(plan.WorkingCopy);
        Assert.Equal(outputPath, plan.AttachmentSourcePath);
        Assert.Equal(new[] { "cover.jpg", "notes.txt" }, plan.PreservedAttachmentNames);
        Assert.Contains(plan.SubtitleFiles, subtitle => subtitle.IsEmbedded && subtitle.EmbeddedTrackId == 3);
        Assert.Equal(outputPath, plan.AudioDescriptionFilePath);
        Assert.Contains("cover.jpg (aus Zieldatei)", plan.BuildUsageSummary().Attachments.CurrentText, StringComparison.Ordinal);

        var arguments = plan.BuildArguments();
        var runtimeArchivePath = plan.WorkingCopy!.DestinationFilePath;
        AssertContainsSequence(
            arguments,
            "--audio-tracks",
            "2",
            "--language",
            "2:de",
            "--track-name",
            "2:Deutsch (sehbehinderte) - AAC");
        AssertContainsSequence(arguments, "--no-video", "--no-audio", "--no-subtitles", runtimeArchivePath);
        Assert.Equal(3, arguments.Count(argument => string.Equals(argument, runtimeArchivePath, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task PrepareAsync_KeepingArchivePrimary_RetainsBetterSameCodecAdditionalVideo()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-archive-upgrade");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-archive-upgrade");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var manualPrimaryPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-1.mp4");
        var betterAdditionalPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-2.mp4");
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");

        FakeMkvMergeTestHelper.WriteProbeFile(
            manualPrimaryPath,
            CreateVideoTrack(0, "AVC/H.264", "1280x720"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            betterAdditionalPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            outputPath,
            CreateVideoTrack(0, "AVC/H.264", "1280x720"),
            CreateAudioTrack(1, "E-AC-3"));

        var archiveService = CreateArchiveService(archiveDirectory);

        var decision = await archiveService.PrepareAsync(
            FakeMkvMergeTestHelper.ResolveExecutablePath(),
            new SeriesEpisodeMuxRequest(
                manualPrimaryPath,
                AudioDescriptionPath: null,
                SubtitlePaths: [],
                AttachmentPaths: [],
                outputPath,
                Title: "Pilot"),
            [manualPrimaryPath, betterAdditionalPath]);

        Assert.Equal(outputPath, decision.PrimarySourcePath);
        Assert.Contains(betterAdditionalPath, decision.AdditionalVideoPaths);
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
    public async Task CreatePlanAsync_KeepingArchivePrimary_PreservesManuallySelectedTextAttachments()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-archive-manual-attachments");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-archive-manual-attachments");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        var manualAttachmentPath = CreateFile(sourceDirectory, "Zusatzinfos.txt", "Zusatz");
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");

        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1280x720"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFileWithAttachments(
            outputPath,
            [CreateAttachment("bestehend.txt")],
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            mainVideoPath,
            AudioDescriptionPath: null,
            SubtitlePaths: [],
            AttachmentPaths: [manualAttachmentPath],
            outputPath,
            Title: "Pilot",
            ManualAttachmentPaths: [manualAttachmentPath]));

        Assert.False(plan.SkipMux);
        Assert.Equal(outputPath, plan.VideoSources[0].FilePath);
        Assert.Contains(manualAttachmentPath, plan.AttachmentFilePaths);
        AssertContainsSequence(plan.BuildArguments(), "--attachment-mime-type", "text/plain", "--attach-file", manualAttachmentPath);
    }

    [Fact]
    public async Task CreatePlanAsync_KeepingArchivePrimary_DoesNotCarryOverAutoDetectedAttachment_FromUnusedFreshPrimary()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-archive-auto-attachments");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-archive-auto-attachments");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        var sourceAttachmentPath = CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02).txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");
        var subtitleAssPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).ass", "subtitle-ass");
        var subtitleSrtPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).srt", "subtitle-srt");
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");

        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1280x720"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFileWithAttachments(
            outputPath,
            [CreateAttachment("bestehend.txt")],
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"),
            CreateSubtitleTrack(3, "SubRip/SRT", trackName: "Deutsch (hörgeschädigte) - SRT", isHearingImpaired: true));

        var service = CreateMuxService(archiveDirectory);
        var detected = await service.DetectFromSelectedVideoAsync(mainVideoPath);

        Assert.Contains(sourceAttachmentPath, detected.AttachmentPaths);
        Assert.Equal(new[] { subtitleAssPath, subtitleSrtPath }, detected.SubtitlePaths);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            detected.MainVideoPath,
            detected.AudioDescriptionPath,
            detected.SubtitlePaths,
            detected.AttachmentPaths,
            outputPath,
            detected.SuggestedTitle));

        Assert.False(plan.SkipMux);
        Assert.Equal(outputPath, plan.VideoSources[0].FilePath);
        Assert.DoesNotContain(sourceAttachmentPath, plan.AttachmentFilePaths, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("bestehend.txt", plan.PreservedAttachmentNames);
        Assert.Contains(plan.SubtitleFiles, subtitle => subtitle.IsEmbedded && subtitle.EmbeddedTrackId == 3);
        Assert.Contains(plan.SubtitleFiles, subtitle => !subtitle.IsEmbedded && string.Equals(subtitle.FilePath, subtitleAssPath, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.SubtitleFiles, subtitle => !subtitle.IsEmbedded && string.Equals(subtitle.FilePath, subtitleSrtPath, StringComparison.OrdinalIgnoreCase));
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
    public async Task DetectFromSelectedVideoAsync_DoesNotMergeDifferentEpisodes_ThatShareSeriesAndTitle()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-same-title");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-same-title");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var episodeOnePath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E01).mp4");
        var episodeTwoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        var episodeTwoAlternatePath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-2.mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E01).txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E01)\r\nDauer: 00:42:00");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02).txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02)-2.txt",
            "Sender: ARD\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");

        FakeMkvMergeTestHelper.WriteProbeFile(
            episodeOnePath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            episodeTwoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            episodeTwoAlternatePath,
            CreateVideoTrack(0, "HEVC/H.265", "1920x1080"),
            CreateAudioTrack(1, "AAC"));

        var service = CreateMuxService(archiveDirectory);

        var detected = await service.DetectFromSelectedVideoAsync(episodeOnePath);

        Assert.Equal(episodeOnePath, detected.MainVideoPath);
        Assert.Empty(detected.AdditionalVideoPaths);
        Assert.DoesNotContain(detected.RelatedFilePaths, path => string.Equals(path, episodeTwoPath, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(detected.RelatedFilePaths, path => string.Equals(path, episodeTwoAlternatePath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DetectFromSelectedVideoAsync_PreservesSeriesNames_WithInternalHyphens()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-hyphen-series");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-hyphen-series");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Der Kroatien-Krimi - Pilot (S01_E02).mp4");
        CreateFile(
            sourceDirectory,
            "Der Kroatien-Krimi - Pilot (S01_E02).txt",
            "Sender: ZDF\r\nThema: Der Kroatien-Krimi\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");

        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var service = CreateMuxService(archiveDirectory);

        var detected = await service.DetectFromSelectedVideoAsync(mainVideoPath);

        Assert.Equal("Der Kroatien-Krimi", detected.SeriesName);
        Assert.EndsWith(
            Path.Combine("Der Kroatien-Krimi", "Season 1", "Der Kroatien-Krimi - S01E02 - Pilot.mkv"),
            detected.SuggestedOutputFilePath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreatePlanAsync_ReplacingArchivePrimary_UsageSummary_ShowsRemovedArchiveParts_WithReasons()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-usage-replace");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-usage-replace");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        var audioDescriptionPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02) Audiodeskription.mp4");
        var subtitlePath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).srt", "subtitle");
        var manualAttachmentPath = CreateFile(sourceDirectory, "Zusatzinfos.txt", "attachment");
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");

        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            audioDescriptionPath,
            CreateAudioTrack(0, "AAC", trackName: "Deutsch (sehbehinderte) - AAC", isVisualImpaired: true));
        FakeMkvMergeTestHelper.WriteProbeFileWithAttachments(
            outputPath,
            [CreateAttachment("bestehend.txt")],
            CreateVideoTrack(0, "AVC/H.264", "1280x720"),
            CreateAudioTrack(1, "AAC", trackName: "Deutsch - AAC"),
            CreateAudioTrack(2, "AAC", trackName: "Deutsch (sehbehinderte) - AAC", isVisualImpaired: true));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            mainVideoPath,
            AudioDescriptionPath: audioDescriptionPath,
            SubtitlePaths: [subtitlePath],
            AttachmentPaths: [manualAttachmentPath],
            outputPath,
            Title: "Pilot"));

        var summary = plan.BuildUsageSummary();

        Assert.True(summary.MainVideo.HasRemoved);
        Assert.Contains("höhere Qualität", summary.MainVideo.RemovedReason, StringComparison.Ordinal);
        Assert.True(summary.Audio.HasRemoved);
        Assert.Contains("Tonspur", summary.Audio.RemovedReason, StringComparison.Ordinal);
        Assert.True(summary.AudioDescription.HasRemoved);
        Assert.Contains("AD", summary.AudioDescription.RemovedReason, StringComparison.Ordinal);
        Assert.False(summary.Subtitles.HasRemoved);
        Assert.True(summary.Attachments.HasRemoved);
        Assert.Contains("Anhänge", summary.Attachments.RemovedReason, StringComparison.Ordinal);
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
        Assert.Contains("Deutsch (hörgeschädigte) - SRT (aus Zieldatei)", summary.Subtitles.CurrentText, StringComparison.Ordinal);
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

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private SeriesEpisodeMuxService CreateMuxService(string archiveDirectory)
    {
        var settingsStore = CreateSettingsStore();
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

    private SeriesArchiveService CreateArchiveService(string archiveDirectory)
    {
        var settingsStore = CreateSettingsStore();
        var service = new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(settingsStore));
        service.ConfigureArchiveRootDirectory(archiveDirectory);
        return service;
    }

    private static AppSettingsStore CreateSettingsStore()
    {
        var settingsStore = new AppSettingsStore();
        new AppToolPathStore(settingsStore).Save(new AppToolPathSettings
        {
            MkvToolNixDirectoryPath = FakeMkvMergeTestHelper.ResolveExecutablePath()
        });
        return settingsStore;
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

    private static object CreateAudioTrack(
        int id,
        string codec,
        string trackName = "",
        bool isVisualImpaired = false,
        string language = "de")
    {
        return new
        {
            id,
            type = "audio",
            codec,
            properties = new
            {
                language_ietf = language,
                track_name = trackName,
                flag_visual_impaired = isVisualImpaired
            }
        };
    }

    private static object CreateSubtitleTrack(
        int id,
        string codec,
        string trackName = "",
        bool isHearingImpaired = false,
        string language = "de")
    {
        return new
        {
            id,
            type = "subtitles",
            codec,
            properties = new
            {
                language_ietf = language,
                track_name = trackName,
                flag_hearing_impaired = isHearingImpaired
            }
        };
    }

    private static object CreateAttachment(string fileName)
    {
        return new
        {
            file_name = fileName
        };
    }

    private static void AssertContainsSequence(IReadOnlyList<string> values, params string[] expectedSequence)
    {
        for (var index = 0; index <= values.Count - expectedSequence.Length; index++)
        {
            var matches = true;
            for (var offset = 0; offset < expectedSequence.Length; offset++)
            {
                if (!string.Equals(values[index + offset], expectedSequence[offset], StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return;
            }
        }

        throw new Xunit.Sdk.XunitException($"Sequenz '{string.Join("', '", expectedSequence)}' wurde nicht gefunden.");
    }

    private sealed class NullDurationProbe : IMediaDurationProbe
    {
        public TimeSpan? TryReadDuration(string filePath)
        {
            return null;
        }
    }
}
