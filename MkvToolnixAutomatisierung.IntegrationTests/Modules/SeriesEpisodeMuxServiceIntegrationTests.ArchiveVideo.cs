using System.IO;
using MkvToolnixAutomatisierung.IntegrationTests.TestInfrastructure;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using Xunit;

namespace MkvToolnixAutomatisierung.IntegrationTests.Modules;

public sealed partial class SeriesEpisodeMuxServiceIntegrationTests
{
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
        Assert.Equal(["cover.jpg"], plan.PreservedAttachmentNames);
        Assert.Contains(plan.SubtitleFiles, subtitle => subtitle.IsEmbedded && subtitle.EmbeddedTrackId == 3);
        Assert.Equal(outputPath, plan.AudioDescriptionFilePath);
        Assert.Contains("Aus Zieldatei: cover.jpg", plan.BuildUsageSummary().Attachments.CurrentText, StringComparison.Ordinal);

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
        AssertContainsSequence(arguments, "--no-video", "--no-audio", "--no-subtitles", "--attachments", "0", runtimeArchivePath);
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

        Assert.Equal(betterAdditionalPath, decision.PrimarySourcePath);
        Assert.Empty(decision.AdditionalVideoPaths);
        Assert.Equal(
            [(betterAdditionalPath, 0)],
            decision.VideoSelections.Select(selection => (selection.FilePath, selection.TrackId)).ToList());
    }

    [Fact]
    public async Task PrepareAsync_SelectsBestArchiveAndFreshVideos_PerLanguageAndCodecSlot()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-archive-language-codec-slots");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-archive-language-codec-slots");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var freshGermanH264Path = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-de-h264.mp4");
        var freshGermanH265Path = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-de-h265.mp4");
        var freshEnglishH264Path = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-en-h264.mp4");
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");

        FakeMkvMergeTestHelper.WriteProbeFile(
            freshGermanH264Path,
            CreateVideoTrack(0, "AVC/H.264", "1280x720", language: "de"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            freshGermanH265Path,
            CreateVideoTrack(0, "HEVC/H.265", "3840x2160", language: "de"),
            CreateAudioTrack(1, "AAC"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            freshEnglishH264Path,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "en"),
            CreateAudioTrack(1, "E-AC-3", language: "en"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            outputPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "de"),
            CreateAudioTrack(1, "E-AC-3"),
            CreateVideoTrack(2, "HEVC/H.265", "1280x720", language: "de"),
            CreateVideoTrack(3, "AVC/H.264", "1920x1080", language: "nds"),
            CreateVideoTrack(4, "AVC/H.264", "1280x720", language: "de"));

        var archiveService = CreateArchiveService(archiveDirectory);

        var decision = await archiveService.PrepareAsync(
            FakeMkvMergeTestHelper.ResolveExecutablePath(),
            new SeriesEpisodeMuxRequest(
                freshGermanH264Path,
                AudioDescriptionPath: null,
                SubtitlePaths: [],
                AttachmentPaths: [],
                outputPath,
                Title: "Pilot"),
            [freshGermanH264Path, freshGermanH265Path, freshEnglishH264Path]);

        Assert.Equal(outputPath, decision.PrimarySourcePath);
        Assert.Equal(
            [
                (outputPath, 0, "de"),
                (freshGermanH265Path, 0, "de"),
                (outputPath, 3, "nds"),
                (freshEnglishH264Path, 0, "en")
            ],
            decision.VideoSelections.Select(selection => (selection.FilePath, selection.TrackId, selection.LanguageCode)).ToList());
        Assert.Equal([freshGermanH265Path, freshEnglishH264Path], decision.AdditionalVideoPaths);
        Assert.NotNull(decision.UsageComparison.AdditionalVideos);
        Assert.Contains("1280px / H.265", decision.UsageComparison.AdditionalVideos!.RemovedText, StringComparison.Ordinal);
        Assert.Contains("1280px / H.264", decision.UsageComparison.AdditionalVideos.RemovedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatePlanAsync_KeepingArchivePrimary_DoesNotRetainNameDetectedAudioDescriptionAsNormalAudio()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-filter-ad-normal-audio");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-filter-ad-normal-audio");
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
            CreateAudioTrack(1, "E-AC-3", trackName: "Deutsch - E-AC-3"),
            CreateAudioTrack(2, "AAC", trackName: "Deutsch (sehbehinderte) - AAC", isVisualImpaired: false),
            CreateAudioTrack(3, "AAC", trackName: "Deutsch Audiodeskription - AAC", isVisualImpaired: false));

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
        Assert.Equal([1], plan.AudioSources.Select(source => source.TrackId).ToList());
        Assert.Equal(2, plan.AudioDescriptionTrackId);
    }

    [Fact]
    public async Task CreatePlanAsync_KeepingArchivePrimary_DropsArchiveAudioLanguages_ThatFreshMultiAudioSourceAlreadyCovers()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-fresh-multi-audio");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-fresh-multi-audio");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var freshEnglishVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-en.mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02)-en.txt",
            "Sender: BBC\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");

        FakeMkvMergeTestHelper.WriteProbeFile(
            freshEnglishVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "en"),
            CreateAudioTrack(1, "AAC", trackName: "English - AAC", language: "en"),
            CreateAudioTrack(2, "E-AC-3", trackName: "Deutsch - E-AC-3", language: "de"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            outputPath,
            CreateVideoTrack(0, "AVC/H.264", "3840x2160", language: "de"),
            CreateAudioTrack(1, "E-AC-3", trackName: "Deutsch - E-AC-3", language: "de"),
            CreateAudioTrack(2, "AAC", trackName: "English - AAC", language: "en"));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            freshEnglishVideoPath,
            AudioDescriptionPath: null,
            SubtitlePaths: [],
            AttachmentPaths: [],
            outputPath,
            Title: "Pilot"));

        Assert.False(plan.SkipMux);
        Assert.Equal(
            [outputPath, freshEnglishVideoPath],
            plan.VideoSources.Select(source => source.FilePath).ToList());
        Assert.NotNull(plan.PrimarySourceAudioTrackIds);
        Assert.Empty(plan.PrimarySourceAudioTrackIds!);
        Assert.All(plan.AudioSources, source => Assert.Equal(freshEnglishVideoPath, source.FilePath));
        Assert.Equal(
            ["en", "de"],
            plan.AudioSources.Select(source => source.LanguageCode).ToList());
        Assert.Equal(
            [1, 2],
            plan.AudioSources.Select(source => source.TrackId).ToList());
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
    public async Task CreatePlanAsync_KeepingArchivePrimary_WithoutNewContent_SkipsWhenRelevantTrackNamesAreAlreadyConsistent()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-archive-skip-consistent");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-archive-skip-consistent");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02).txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");

        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1280x720"),
            CreateAudioTrack(1, "E-AC-3"));

        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");
        FakeMkvMergeTestHelper.WriteProbeFileWithAttachmentsAndContainerTitle(
            outputPath,
            [CreateAttachment("cover.jpg")],
            "Pilot",
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", trackName: "Deutsch - FHD - H.264"),
            CreateAudioTrack(1, "E-AC-3", trackName: "Deutsch - E-AC-3"),
            CreateAudioTrack(2, "AAC", trackName: "Deutsch (sehbehinderte) - AAC", isVisualImpaired: true),
            CreateSubtitleTrack(3, "SubRip/SRT", trackName: "Deutsch (hörgeschädigte) - SRT", isHearingImpaired: true));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            mainVideoPath,
            AudioDescriptionPath: null,
            SubtitlePaths: [],
            AttachmentPaths: [],
            outputPath,
            Title: "Pilot"));

        Assert.True(plan.SkipMux);

        var summary = plan.BuildUsageSummary();
        Assert.Equal("Zieldatei bereits aktuell", summary.ArchiveAction);
        Assert.Contains("relevanten Spurnamen", summary.ArchiveDetails, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Aus Zieldatei: Deutsch - FHD - H.264", summary.MainVideo.CurrentText);
        Assert.Equal("Aus Zieldatei: Deutsch - E-AC-3", summary.Audio.CurrentText);
        Assert.Equal("Aus Zieldatei: Deutsch (sehbehinderte) - AAC", summary.AudioDescription.CurrentText);
        Assert.Contains("Aus Zieldatei: Deutsch (hörgeschädigte) - SRT", summary.Subtitles.CurrentText, StringComparison.Ordinal);
        Assert.Contains("Aus Zieldatei: cover.jpg", summary.Attachments.CurrentText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatePlanAsync_KeepingArchivePrimary_WithoutNewContent_UsesDirectHeaderEditForTrackNameNormalization()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-archive-rename-only");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-archive-rename-only");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02).txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");

        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1280x720"),
            CreateAudioTrack(1, "E-AC-3"));

        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");
        FakeMkvMergeTestHelper.WriteProbeFileWithAttachmentsAndContainerTitle(
            outputPath,
            [CreateAttachment("cover.jpg")],
            "Pilot",
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", trackName: "Deutsch - FHD - H.264"),
            CreateAudioTrack(1, "E-AC-3", trackName: "Alter Audiotitel"),
            CreateAudioTrack(2, "AAC", trackName: "Deutsch (sehbehinderte) - AAC", isVisualImpaired: true),
            CreateSubtitleTrack(3, "SubRip/SRT", trackName: "Deutsch (hörgeschädigte) - SRT", isHearingImpaired: true));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            mainVideoPath,
            AudioDescriptionPath: null,
            SubtitlePaths: [],
            AttachmentPaths: [],
            outputPath,
            Title: "Pilot"));

        Assert.False(plan.SkipMux);
        Assert.True(plan.HasTrackHeaderEdits);
        Assert.Null(plan.WorkingCopy);
        Assert.Equal("mkvpropedit", plan.ExecutionToolDisplayName);
        Assert.Contains(
            plan.Notes,
            note => note.Contains("Benennungen der relevanten Spuren", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            plan.Notes,
            note => note.Contains("Alter Audiotitel -> Deutsch - E-AC-3", StringComparison.OrdinalIgnoreCase));

        var summary = plan.BuildUsageSummary();
        Assert.Equal("Zieldatei bleibt inhaltlich unverändert", summary.ArchiveAction);
        Assert.Equal("Es werden nur die Benennungen der relevanten Spuren direkt im Header vereinheitlicht", summary.ArchiveDetails);
        Assert.Equal("Aus Zieldatei: Deutsch - FHD - H.264", summary.MainVideo.CurrentText);
        Assert.Equal("Aus Zieldatei: Deutsch - E-AC-3", summary.Audio.CurrentText);
        Assert.Equal("Aus Zieldatei: Deutsch (sehbehinderte) - AAC", summary.AudioDescription.CurrentText);
        Assert.Contains("Aus Zieldatei: Deutsch (hörgeschädigte) - SRT", summary.Subtitles.CurrentText, StringComparison.Ordinal);
        Assert.Contains("Aus Zieldatei: cover.jpg", summary.Attachments.CurrentText, StringComparison.Ordinal);

        var arguments = plan.BuildArguments();
        Assert.Equal(outputPath, arguments[0]);
        AssertContainsSequence(arguments, "--edit", "track:2", "--set", "name=Deutsch - E-AC-3");
    }

    [Fact]
    public async Task CreatePlanAsync_KeepingArchivePrimary_UsesDirectHeaderEditForContainerTitleNormalization()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-archive-title-only");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-archive-title-only");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02).txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");

        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1280x720"),
            CreateAudioTrack(1, "E-AC-3"));

        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");
        FakeMkvMergeTestHelper.WriteProbeFileWithAttachmentsAndContainerTitle(
            outputPath,
            [],
            "Alter Pilot-Titel",
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", trackName: "Deutsch - FHD - H.264"),
            CreateAudioTrack(1, "E-AC-3", trackName: "Deutsch - E-AC-3"));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            mainVideoPath,
            AudioDescriptionPath: null,
            SubtitlePaths: [],
            AttachmentPaths: [],
            outputPath,
            Title: "Pilot"));

        Assert.False(plan.SkipMux);
        Assert.True(plan.HasHeaderEdits);
        Assert.False(plan.HasTrackHeaderEdits);
        Assert.NotNull(plan.ContainerTitleEdit);
        Assert.Equal("Alter Pilot-Titel", plan.ContainerTitleEdit!.CurrentTitle);
        Assert.Equal("Pilot", plan.ContainerTitleEdit.ExpectedTitle);
        Assert.Null(plan.WorkingCopy);
        Assert.Contains(
            plan.Notes,
            note => note.Contains("MKV-Titel: Alter Pilot-Titel -> Pilot", StringComparison.OrdinalIgnoreCase));

        var summary = plan.BuildUsageSummary();
        Assert.Equal("Zieldatei bleibt inhaltlich unverändert", summary.ArchiveAction);
        Assert.Equal("Es wird nur der MKV-Titel direkt im Header vereinheitlicht", summary.ArchiveDetails);

        var arguments = plan.BuildArguments();
        Assert.Equal(outputPath, arguments[0]);
        AssertContainsSequence(arguments, "--edit", "info", "--set", "title=Pilot");
    }

    [Fact]
    public async Task CreatePlanAsync_AddsDurationMismatchNote_WhenArchiveLooksLikeDoubleEpisode()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-duration-mismatch");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-duration-mismatch");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Beispielserie - Rififi (S2014_E05).mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Rififi (S2014_E05).txt",
            "Sender: NDR\r\nThema: Beispielserie\r\nTitel: Rififi (S2014_E05)\r\nDauer: 01:26:00");
        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var outputPath = Path.Combine(
            archiveDirectory,
            "Beispielserie",
            "Season 2014",
            "Beispielserie - S2014E05 - Rififi (1).mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");
        FakeMkvMergeTestHelper.WriteProbeFileWithAttachments(
            outputPath,
            [
                CreateAttachment(
                    "Beispielserie-Rififi (1_2)-de.txt",
                    textContent: "Sender: NDR\r\nThema: Beispielserie\r\nTitel: Rififi (1/2)\r\nDauer: 00:43:00"),
                CreateAttachment(
                    "Beispielserie-Rififi (1_2) - Audiodeskription.txt",
                    textContent: "Sender: NDR\r\nThema: Beispielserie\r\nTitel: Rififi (1/2) - Audiodeskription\r\nDauer: 00:43:00"),
                CreateAttachment(
                    "Beispielserie-Büttenwarder op Platt_ Rififi (1_2).txt",
                    textContent: "Sender: NDR\r\nThema: Beispielserie\r\nTitel: Büttenwarder op Platt: Rififi (1/2)\r\nDauer: 00:42:50")
            ],
            CreateVideoTrack(0, "AVC/H.264", "1280x720"),
            CreateAudioTrack(1, "E-AC-3"));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            mainVideoPath,
            AudioDescriptionPath: null,
            SubtitlePaths: [],
            AttachmentPaths: [],
            outputPath,
            Title: "Rififi (1)"));

        Assert.Contains(plan.Notes, note => note.Contains("Doppelfolge", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Notes, note => note.Contains("2-mal so lang", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePlanAsync_DoesNotAddDurationMismatchNote_ForShortSpecials()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-short-special-duration");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-short-special-duration");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Beispielserie - Der Vorspann der Kultserie (S00_E01).mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Der Vorspann der Kultserie (S00_E01).txt",
            "Sender: NDR\r\nThema: Beispielserie\r\nTitel: Der Vorspann der Kultserie (S00_E01)\r\nDauer: 00:01:26");
        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "960x544"),
            CreateAudioTrack(1, "AAC"));

        var outputPath = Path.Combine(
            archiveDirectory,
            "Beispielserie",
            "Specials",
            "Beispielserie - S00E01 - Der Vorspann der Kultserie.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");
        FakeMkvMergeTestHelper.WriteProbeFileWithAttachments(
            outputPath,
            [
                CreateAttachment(
                    "Beispielserie-Der Vorspann der Kultserie.txt",
                    textContent: "Sender: NDR\r\nThema: Beispielserie\r\nTitel: Der Vorspann der Kultserie\r\nDauer: 00:00:43")
            ],
            CreateVideoTrack(0, "AVC/H.264", "960x544", trackName: "Deutsch - SD - H.264"),
            CreateAudioTrack(1, "AAC", trackName: "Deutsch - AAC"));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            mainVideoPath,
            AudioDescriptionPath: null,
            SubtitlePaths: [],
            AttachmentPaths: [],
            outputPath,
            Title: "Der Vorspann der Kultserie"));

        Assert.DoesNotContain(plan.Notes, note => note.Contains("Doppelfolge", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.Notes, note => note.Contains("Mehrfachfolge", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePlanAsync_AddsVariantNote_WhenArchiveContainsMatchingMultipartEpisodeVariant()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-episode-variant");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-episode-variant");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Beispielserie - Rififi (S2014_E05).mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Rififi (S2014_E05).txt",
            "Sender: NDR\r\nThema: Beispielserie\r\nTitel: Rififi (S2014_E05)\r\nDauer: 00:26:00");
        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var seasonDirectory = Path.Combine(archiveDirectory, "Beispielserie", "Season 2014");
        var outputPath = Path.Combine(seasonDirectory, "Beispielserie - S2014E05 - Rififi (1).mkv");
        var doubleEpisodePath = Path.Combine(seasonDirectory, "Beispielserie - S2014E05-E06 - Rififi.mkv");
        CreateFile(seasonDirectory, Path.GetFileName(outputPath), "archive-single");
        CreateFile(seasonDirectory, Path.GetFileName(doubleEpisodePath), "archive-double");
        FakeMkvMergeTestHelper.WriteProbeFile(
            outputPath,
            CreateVideoTrack(0, "AVC/H.264", "1280x720"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            doubleEpisodePath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            mainVideoPath,
            AudioDescriptionPath: null,
            SubtitlePaths: [],
            AttachmentPaths: [],
            outputPath,
            Title: "Rififi (1)"));

        Assert.Contains(plan.Notes, note => note.Contains("Mehrfachfolge", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Notes, note => note.Contains("S2014E05-E06", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePlanAsync_AdOnlyContinuation_AddsVariantNote_WhenArchiveContainsMatchingMultipartEpisodeVariant()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-ad-only-episode-variant");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-ad-only-episode-variant");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var audioDescriptionPath = CreateFile(
            sourceDirectory,
            "Beispielserie - Rififi (2) ... es geht weiter (mit Audiodeskription).mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Rififi (2) ... es geht weiter (mit Audiodeskription).txt",
            "Sender: NDR\r\nThema: Beispielserie\r\nTitel: Rififi (2) ... es geht weiter (mit Audiodeskription)\r\nDauer: 00:26:13");
        FakeMkvMergeTestHelper.WriteProbeFile(
            audioDescriptionPath,
            CreateAudioTrack(0, "AAC", trackName: "Deutsch (sehbehinderte) - AAC", isVisualImpaired: true));

        var seasonDirectory = Path.Combine(archiveDirectory, "Beispielserie", "Season 2014");
        var outputPath = Path.Combine(seasonDirectory, "Beispielserie - S2014E06 - Rififi ... es geht weiter (2).mkv");
        var doubleEpisodePath = Path.Combine(seasonDirectory, "Beispielserie - S2014E05-E06 - Rififi.mkv");
        CreateFile(seasonDirectory, Path.GetFileName(outputPath), "archive-single");
        CreateFile(seasonDirectory, Path.GetFileName(doubleEpisodePath), "archive-double");
        FakeMkvMergeTestHelper.WriteProbeFile(
            outputPath,
            CreateVideoTrack(0, "AVC/H.264", "1280x720"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            doubleEpisodePath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            audioDescriptionPath,
            AudioDescriptionPath: audioDescriptionPath,
            SubtitlePaths: [],
            AttachmentPaths: [],
            outputPath,
            Title: "Rififi (2) ... es geht weiter (mit Audiodeskription)",
            HasPrimaryVideoSource: false));

        Assert.Contains(plan.Notes, note => note.Contains("Mehrfachfolge", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Notes, note => note.Contains("S2014E05-E06", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePlanAsync_AdOnlyContinuation_AddsVariantNote_WhenExistingTargetAlreadySkipsMux()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-ad-only-skip-episode-variant");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-ad-only-skip-episode-variant");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var audioDescriptionPath = CreateFile(
            sourceDirectory,
            "Beispielserie - Rififi (2) ... es geht weiter (mit Audiodeskription).mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Rififi (2) ... es geht weiter (mit Audiodeskription).txt",
            "Sender: NDR\r\nThema: Beispielserie\r\nTitel: Rififi (2) ... es geht weiter (mit Audiodeskription)\r\nDauer: 00:26:13");
        FakeMkvMergeTestHelper.WriteProbeFile(
            audioDescriptionPath,
            CreateAudioTrack(0, "AAC", trackName: "Deutsch (sehbehinderte) - AAC", isVisualImpaired: true));

        var seasonDirectory = Path.Combine(archiveDirectory, "Beispielserie", "Season 2014");
        var outputPath = Path.Combine(seasonDirectory, "Beispielserie - S2014E06 - Rififi ... es geht weiter (2).mkv");
        var doubleEpisodePath = Path.Combine(seasonDirectory, "Beispielserie - S2014E05-E06 - Rififi.mkv");
        CreateFile(seasonDirectory, Path.GetFileName(outputPath), "archive-single");
        CreateFile(seasonDirectory, Path.GetFileName(doubleEpisodePath), "archive-double");
        FakeMkvMergeTestHelper.WriteProbeFileWithContainerTitle(
            outputPath,
            "Rififi (2) ... es geht weiter (mit Audiodeskription)",
            CreateVideoTrack(0, "AVC/H.264", "1280x720", trackName: "Deutsch - HD - H.264"),
            CreateAudioTrack(1, "E-AC-3", trackName: "Deutsch - E-AC-3"),
            CreateAudioTrack(2, "AAC", trackName: "Deutsch (sehbehinderte) - AAC", isVisualImpaired: true));
        FakeMkvMergeTestHelper.WriteProbeFile(
            doubleEpisodePath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            audioDescriptionPath,
            AudioDescriptionPath: audioDescriptionPath,
            SubtitlePaths: [],
            AttachmentPaths: [],
            outputPath,
            Title: "Rififi (2) ... es geht weiter (mit Audiodeskription)",
            HasPrimaryVideoSource: false));

        Assert.True(plan.SkipMux);
        Assert.Contains(plan.Notes, note => note.Contains("Mehrfachfolge", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Notes, note => note.Contains("S2014E05-E06", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePlanAsync_KeepingArchivePrimary_ExplainsWhenSelectedAssAlreadyExistsInTarget()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-existing-ass-subtitle");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-existing-ass-subtitle");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Beispielserie - Olympische Rekorde (S2015_E02-E03).mp4");
        var assSubtitlePath = CreateFile(sourceDirectory, "Beispielserie - Olympische Rekorde (S2015_E02-E03).ass", "ass-subtitle");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Olympische Rekorde (S2015_E02-E03).txt",
            "Sender: NDR\r\nThema: Beispielserie\r\nTitel: Olympische Rekorde (S2015_E02-E03)\r\nDauer: 00:49:01");
        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1280x720"),
            CreateAudioTrack(1, "E-AC-3"));

        var outputPath = Path.Combine(
            archiveDirectory,
            "Beispielserie",
            "Season 2015",
            "Beispielserie - S2015E02-E03 - Olympische Rekorde.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");
        FakeMkvMergeTestHelper.WriteProbeFileWithContainerTitle(
            outputPath,
            "Olympische Rekorde",
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", trackName: "Deutsch - FHD - H.264"),
            CreateAudioTrack(1, "E-AC-3", trackName: "Deutsch - E-AC-3"),
            CreateSubtitleTrack(2, "SubStationAlpha", trackName: "Deutsch (hörgeschädigte) - SSA", isHearingImpaired: true),
            CreateSubtitleTrack(3, "SubRip/SRT", trackName: "Deutsch (hörgeschädigte) - SRT", isHearingImpaired: true));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            mainVideoPath,
            AudioDescriptionPath: null,
            SubtitlePaths: [assSubtitlePath],
            AttachmentPaths: [],
            outputPath,
            Title: "Olympische Rekorde",
            PlannedVideoPaths: [mainVideoPath]));

        Assert.True(plan.SkipMux);
        Assert.Contains(plan.Notes, note => note.Contains("nicht zusätzlich", StringComparison.OrdinalIgnoreCase));
        var subtitlesText = plan.BuildUsageSummary().Subtitles.CurrentText;
        Assert.Contains("Aus Zieldatei: Deutsch (hörgeschädigte) - SSA", subtitlesText, StringComparison.Ordinal);
        Assert.Contains("Aus Zieldatei: Deutsch (hörgeschädigte) - SRT", subtitlesText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Rififi … es geht weiter (2)", "Beispielserie - S2014E06 - Rififi … es geht weiter (2).mkv")]
    [InlineData("Olympische Rekorde (1): Rekord", "Beispielserie - S2014E05 - Olympische Rekorde (1) - Rekord.mkv")]
    public async Task CreatePlanAsync_AddsVariantNote_ForMultipartContinuationAndColonSubtitles(
        string title,
        string outputFileName)
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-episode-variant-extended");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-episode-variant-extended");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var sourceFileTitle = title.Replace(':', '_');
        var mainVideoPath = CreateFile(sourceDirectory, $"Beispielserie - {sourceFileTitle} (S2014_E05).mp4");
        var assSubtitlePath = CreateFile(sourceDirectory, $"Beispielserie - {sourceFileTitle} (S2014_E05).ass", "ass-subtitle");
        CreateFile(
            sourceDirectory,
            $"Beispielserie - {sourceFileTitle} (S2014_E05).txt",
            $"Sender: NDR\r\nThema: Beispielserie\r\nTitel: {title} (S2014_E05)\r\nDauer: 00:26:00");
        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var seasonDirectory = Path.Combine(archiveDirectory, "Beispielserie", "Season 2014");
        var outputPath = Path.Combine(seasonDirectory, outputFileName);
        var multipartPath = Path.Combine(
            seasonDirectory,
            title.StartsWith("Olympische", StringComparison.Ordinal)
                ? "Beispielserie - S2014E05-E06 - Olympische Rekorde.mkv"
                : "Beispielserie - S2014E05-E06 - Rififi.mkv");
        CreateFile(seasonDirectory, Path.GetFileName(outputPath), "archive-single");
        CreateFile(seasonDirectory, Path.GetFileName(multipartPath), "archive-double");
        FakeMkvMergeTestHelper.WriteProbeFile(
            outputPath,
            CreateVideoTrack(0, "AVC/H.264", "1280x720"),
            CreateAudioTrack(1, "E-AC-3"),
            CreateSubtitleTrack(2, "SubRip/SRT", trackName: "Deutsch (hörgeschädigte) - SRT", isHearingImpaired: true));
        FakeMkvMergeTestHelper.WriteProbeFile(
            multipartPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            mainVideoPath,
            AudioDescriptionPath: null,
            SubtitlePaths: [assSubtitlePath],
            AttachmentPaths: [],
            outputPath,
            Title: title));

        Assert.Contains(plan.Notes, note => note.Contains("Mehrfachfolge", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Notes, note => note.Contains("S2014E05-E06", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.SubtitleFiles, subtitle => !subtitle.IsEmbedded && string.Equals(subtitle.FilePath, assSubtitlePath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.SubtitleFiles, subtitle => subtitle.IsEmbedded && subtitle.Kind.DisplayName == "SRT");
    }

    [Fact]
    public async Task CreatePlanAsync_OpPlattSourceWithWrongLanguageFlag_ReplacesOnlyMatchingPlattArchiveSlot()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-op-platt-language-hint-archive");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-op-platt-language-hint-archive");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var opPlattVideoPath = CreateFile(sourceDirectory, "Neues aus Büttenwarder-Büttenwarder op Platt_ Bildungsschock-0183875890.mp4");
        CreateFile(
            sourceDirectory,
            "Neues aus Büttenwarder-Büttenwarder op Platt_ Bildungsschock-0183875890.txt",
            "Sender: NDR\r\nThema: Neues aus Büttenwarder\r\nTitel: Büttenwarder op Platt: Bildungsschock\r\nDauer: 00:24:25");
        FakeMkvMergeTestHelper.WriteProbeFile(
            opPlattVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "en"),
            CreateAudioTrack(1, "AAC", language: "en"));

        var outputPath = Path.Combine(
            archiveDirectory,
            "Neues aus Büttenwarder",
            "Season 2014",
            "Neues aus Büttenwarder - S2014E01 - Bildungsschock.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");
        FakeMkvMergeTestHelper.WriteProbeFile(
            outputPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "de", trackName: "Deutsch - FHD - H.264"),
            CreateAudioTrack(1, "E-AC-3", language: "de", trackName: "Deutsch - E-AC-3"),
            CreateVideoTrack(2, "AVC/H.264", "1280x720", language: "nds", trackName: "Plattdüütsch - HD - H.264"),
            CreateAudioTrack(3, "AAC", language: "nds", trackName: "Plattdüütsch - AAC"));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            opPlattVideoPath,
            AudioDescriptionPath: null,
            SubtitlePaths: [],
            AttachmentPaths: [],
            outputPath,
            Title: "Bildungsschock",
            PlannedVideoPaths: [opPlattVideoPath]));

        Assert.Equal([outputPath, opPlattVideoPath], plan.VideoSources.Select(source => source.FilePath).ToList());
        Assert.Equal(["de", "nds"], plan.VideoSources.Select(source => source.LanguageCode).ToList());
        Assert.Equal([outputPath, opPlattVideoPath], plan.AudioSources.Select(source => source.FilePath).ToList());
        Assert.Equal(["de", "nds"], plan.AudioSources.Select(source => source.LanguageCode).ToList());
        Assert.DoesNotContain(plan.VideoSources, source => source.TrackName.Contains("English", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.AudioSources, source => source.TrackName.Contains("English", StringComparison.OrdinalIgnoreCase));
        var summary = plan.BuildUsageSummary();
        Assert.NotNull(summary.AdditionalVideos.RemovedText);
        Assert.Contains("Plattdüütsch", summary.AdditionalVideos.RemovedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreatePlanAsync_GermanSourceWithWrongVideoLanguageFlag_DoesNotCreateEnglishVideoSlot()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-german-video-language-flag");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-german-video-language-flag");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var sourcePath = CreateFile(sourceDirectory, "Neues aus Büttenwarder-Büttenwarder mobil_ Killerkralle-0364751988.mp4");
        CreateFile(
            sourceDirectory,
            "Neues aus Büttenwarder-Büttenwarder mobil_ Killerkralle-0364751988.txt",
            "Sender: NDR\r\nThema: Neues aus Büttenwarder\r\nTitel: Büttenwarder mobil: Killerkralle\r\nDauer: 00:03:28");
        FakeMkvMergeTestHelper.WriteProbeFile(
            sourcePath,
            CreateVideoTrack(0, "AVC/H.264", "1280x720", language: "eng"),
            CreateAudioTrack(1, "AAC", language: "ger"));

        var outputPath = Path.Combine(
            archiveDirectory,
            "Neues aus Büttenwarder",
            "Specials",
            "Neues aus Büttenwarder - S00E22 - Büttenwarder mobil - Killerkralles Mondn Beik.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");
        FakeMkvMergeTestHelper.WriteProbeFileWithContainerTitle(
            outputPath,
            "Büttenwarder mobil - Killerkralles Mondn Beik",
            CreateVideoTrack(0, "AVC/H.264", "1280x720", language: "de", trackName: "Deutsch - HD - H.264"),
            CreateAudioTrack(1, "AAC", language: "de", trackName: "Deutsch - AAC"));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            sourcePath,
            AudioDescriptionPath: null,
            SubtitlePaths: [],
            AttachmentPaths: [],
            outputPath,
            Title: "Büttenwarder mobil - Killerkralles Mondn Beik",
            PlannedVideoPaths: [sourcePath]));

        Assert.True(plan.SkipMux);
        Assert.DoesNotContain(plan.Notes, note => note.Contains("English", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("English", plan.BuildUsageSummary().AdditionalVideos.CurrentText, StringComparison.OrdinalIgnoreCase);
    }
}
