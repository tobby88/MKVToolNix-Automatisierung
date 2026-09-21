using System.IO;
using MkvToolnixAutomatisierung.IntegrationTests.TestInfrastructure;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using Xunit;

namespace MkvToolnixAutomatisierung.IntegrationTests.Modules;

public sealed partial class SeriesEpisodeMuxServiceIntegrationTests
{
    [Theory]
    [InlineData("1920x1080", true)]
    [InlineData("1280x720", false)]
    [InlineData("720x576", false)]
    public async Task CreatePlanAsync_ReplacesMatchingSubtitlesOnlyWhenPrimaryVideoIsUpgraded(string sourceDimensions, bool replacesPrimary)
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-upgrade");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-upgrade");
        Directory.CreateDirectory(archiveDirectory);
        var video = CreateFile(sourceDirectory, "Serie - Pilot.mp4");
        var srt = CreateFile(sourceDirectory, "Serie - Pilot.srt", "new subtitles");
        var duplicateSrt = CreateFile(sourceDirectory, "Serie - Pilot-other.srt", "duplicate slot");
        var ass = CreateFile(sourceDirectory, "Serie - Pilot.ass", "additional subtitles");
        var output = CreateFile(Path.Combine(archiveDirectory, "Serie", "Season 1"), "Serie - S01E01 - Pilot.mkv", "archive");
        FakeMkvMergeTestHelper.WriteProbeFile(video,
            CreateVideoTrack(0, "AVC/H.264", sourceDimensions), CreateAudioTrack(1, "AAC"));
        FakeMkvMergeTestHelper.WriteProbeFile(output,
            CreateVideoTrack(0, "AVC/H.264", "1280x720"), CreateAudioTrack(1, "AAC"),
            CreateSubtitleTrack(3, "SubRip/SRT", isHearingImpaired: true),
            CreateSubtitleTrack(4, "WebVTT", isHearingImpaired: true),
            CreateSubtitleTrack(5, "SubRip/SRT", isHearingImpaired: true, language: "en"),
            CreateSubtitleTrack(6, "SubRip/SRT", isHearingImpaired: false),
            CreateSubtitleTrack(7, "SubRip/SRT", isHearingImpaired: true, isForced: true),
            CreateSubtitleTrack(8, "HDMV PGS"));

        var plan = await CreateMuxService(archiveDirectory).CreatePlanAsync(new SeriesEpisodeMuxRequest(
            video, null, [srt, duplicateSrt, ass], [], output, "Pilot"));

        Assert.Equal(replacesPrimary ? video : output, plan.VideoSources[0].FilePath);
        Assert.Equal(replacesPrimary, plan.SubtitleFiles.Any(track => track.FilePath == srt));
        Assert.Equal(!replacesPrimary, plan.SubtitleFiles.Any(track => track.EmbeddedTrackId == 3));
        Assert.Single(plan.SubtitleFiles, track => track.FilePath == ass);
        Assert.DoesNotContain(plan.SubtitleFiles, track => track.FilePath == duplicateSrt);
        // Fehlendes Format, andere Sprache, Standard- und Forced-Rollen sowie unbekannte Codecs
        // dürfen niemals durch die neuen deutschen HI-Untertitel verlorengehen.
        foreach (var id in new[] { 4, 5, 6, 7, 8 })
            Assert.Single(plan.SubtitleFiles, track => track.IsEmbedded && track.EmbeddedTrackId == id);
        Assert.True(Assert.Single(plan.SubtitleFiles, track => track.EmbeddedTrackId == 7).IsForced);
        var summary = plan.BuildUsageSummary().Subtitles;
        Assert.Equal(replacesPrimary, summary.HasRemoved);
        if (replacesPrimary) Assert.Contains("neuen Hauptquelle", summary.RemovedReason!);
        var arguments = plan.BuildArguments();
        Assert.Equal(replacesPrimary, arguments.Contains(srt));
        Assert.Contains(ass, arguments);
        Assert.DoesNotContain(duplicateSrt, arguments);
        var subtitleTrackIds = arguments.Select((arg, index) => (arg, index))
            .Where(pair => pair.arg == "--subtitle-tracks").Select(pair => arguments[pair.index + 1]).ToList();
        Assert.Equal(!replacesPrimary, subtitleTrackIds.Contains("3"));
        Assert.Contains("7", subtitleTrackIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreatePlanAsync_PrimaryUpgradeKeepsOldSubtitlesWithoutSameFormatReplacement(bool addDifferentFormat)
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-no-replacement");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-no-replacement");
        Directory.CreateDirectory(archiveDirectory);
        var video = CreateFile(sourceDirectory, "Serie - Pilot.mp4");
        var ass = CreateFile(sourceDirectory, "Serie - Pilot.ass", "new format");
        var output = CreateFile(Path.Combine(archiveDirectory, "Serie", "Season 1"), "Serie - S01E01 - Pilot.mkv", "archive");
        FakeMkvMergeTestHelper.WriteProbeFile(video,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"), CreateAudioTrack(1, "AAC"));
        FakeMkvMergeTestHelper.WriteProbeFile(output,
            CreateVideoTrack(0, "AVC/H.264", "1280x720"), CreateAudioTrack(1, "AAC"),
            CreateSubtitleTrack(3, "SubRip/SRT", isHearingImpaired: true));

        var plan = await CreateMuxService(archiveDirectory).CreatePlanAsync(new SeriesEpisodeMuxRequest(
            video, null, addDifferentFormat ? [ass] : [], [], output, "Pilot"));

        Assert.Equal(video, plan.VideoSources[0].FilePath);
        Assert.Single(plan.SubtitleFiles, track => track.IsEmbedded && track.EmbeddedTrackId == 3);
        Assert.Equal(addDifferentFormat, plan.SubtitleFiles.Any(track => track.FilePath == ass));
        Assert.False(plan.BuildUsageSummary().Subtitles.HasRemoved);
        Assert.NotNull(plan.WorkingCopy);
        AssertContainsSequence(plan.BuildArguments(), "--subtitle-tracks", "3");
    }

    [Fact]
    public async Task CreatePlanAsync_PrimaryUpgradeWithCompleteSubtitleReplacementDoesNotReuseArchiveInput()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-complete-replacement");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-complete-replacement");
        Directory.CreateDirectory(archiveDirectory);
        var video = CreateFile(sourceDirectory, "Serie - Pilot.mp4");
        var srt = CreateFile(sourceDirectory, "Serie - Pilot.srt", "replacement subtitles");
        var output = CreateFile(Path.Combine(archiveDirectory, "Serie", "Season 1"), "Serie - S01E01 - Pilot.mkv", "archive");
        FakeMkvMergeTestHelper.WriteProbeFile(video,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"), CreateAudioTrack(1, "AAC"));
        FakeMkvMergeTestHelper.WriteProbeFile(output,
            CreateVideoTrack(0, "AVC/H.264", "1280x720"), CreateAudioTrack(1, "AAC"),
            CreateSubtitleTrack(3, "SubRip/SRT", isHearingImpaired: true));

        var plan = await CreateMuxService(archiveDirectory).CreatePlanAsync(new SeriesEpisodeMuxRequest(
            video, null, [srt], [], output, "Pilot"));

        Assert.Equal(video, plan.VideoSources[0].FilePath);
        var subtitle = Assert.Single(plan.SubtitleFiles);
        Assert.Equal(srt, subtitle.FilePath);
        Assert.False(subtitle.IsEmbedded);
        Assert.Null(plan.WorkingCopy);
        Assert.True(plan.BuildUsageSummary().Subtitles.HasRemoved);
        var arguments = plan.BuildArguments();
        Assert.Contains(srt, arguments);
        Assert.DoesNotContain("--subtitle-tracks", arguments);
        // Der Archivpfad darf nur als Ausgabe, nicht erneut als Eingabe auftauchen.
        Assert.Single(arguments, argument => argument == output);
    }

    [Fact]
    public async Task CreatePlanAsync_SecondaryVideoUpgradeDoesNotReplaceSubtitles()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-secondary-upgrade");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-secondary-upgrade");
        Directory.CreateDirectory(archiveDirectory);
        var video = CreateFile(sourceDirectory, "Serie - Pilot.mp4");
        var srt = CreateFile(sourceDirectory, "Serie - Pilot.srt", "new subtitles");
        var output = CreateFile(Path.Combine(archiveDirectory, "Serie", "Season 1"), "Serie - S01E01 - Pilot.mkv", "archive");
        FakeMkvMergeTestHelper.WriteProbeFile(video,
            CreateVideoTrack(0, "HEVC/H.265", "1920x1080"), CreateAudioTrack(1, "AAC"));
        FakeMkvMergeTestHelper.WriteProbeFile(output,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateVideoTrack(2, "HEVC/H.265", "1280x720"), CreateAudioTrack(1, "AAC"),
            CreateSubtitleTrack(3, "SubRip/SRT", isHearingImpaired: true));

        var plan = await CreateMuxService(archiveDirectory).CreatePlanAsync(new SeriesEpisodeMuxRequest(
            video, null, [srt], [], output, "Pilot"));

        Assert.Equal(output, plan.VideoSources[0].FilePath);
        Assert.Contains(plan.VideoSources, source => source.FilePath == video);
        Assert.Single(plan.SubtitleFiles, track => track.IsEmbedded && track.EmbeddedTrackId == 3);
        Assert.DoesNotContain(srt, plan.BuildArguments());
        Assert.False(plan.BuildUsageSummary().Subtitles.HasRemoved);
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
    public async Task CreatePlanAsync_KeepingArchivePrimary_PreservesUnknownEmbeddedSubtitleTracks_WhenAnotherChangeRequiresMux()
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
        Assert.Contains(plan.SubtitleFiles, subtitle => subtitle.IsEmbedded && subtitle.EmbeddedTrackId == 2 && subtitle.Kind.DisplayName == "Unbekannt");
        Assert.Null(plan.BuildUsageSummary().Subtitles.RemovedText);

        var arguments = plan.BuildArguments();
        AssertContainsSequence(arguments, "--subtitle-tracks", "2");
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
            CreateSubtitleTrack(3, "SubRip/SRT", trackName: "Deutsch (hörgeschädigte) - SRT", isHearingImpaired: true));

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
    public async Task PrepareAsync_KeepsExplicitExternalSubtitle_WhenExistingSameKindIsStandard()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-archive-subtitles-accessibility");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-archive-subtitles-accessibility");
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
            CreateSubtitleTrack(3, "SubRip/SRT", trackName: "Deutsch - SRT", isHearingImpaired: false));

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
        Assert.Contains(decision.SubtitleFiles, subtitle => subtitle.IsEmbedded && subtitle.EmbeddedTrackId == 3 && !subtitle.IsHearingImpaired);
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
    public async Task DetectFromSelectedVideoAsync_DeduplicatesSubtitleOnlySupplement_WithSameKind()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-subtitle-only-dedup");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-subtitle-only-dedup");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Beispielserie-Pilot (S05_E01)-1358865137.mp4");
        var preferredSubtitlePath = CreateFile(sourceDirectory, "Beispielserie-Pilot (S05_E01)-1358865137.ass", "subtitle-primary");
        CreateFile(sourceDirectory, "Beispielserie-Pilot (Staffel 5, Folge 25)-0880795506.ass", "subtitle-supplement");
        CreateFile(
            sourceDirectory,
            "Beispielserie-Pilot (S05_E01)-1358865137.txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S05/E01)\r\nDauer: 00:48:00");
        CreateFile(
            sourceDirectory,
            "Beispielserie-Pilot (Staffel 5, Folge 25)-0880795506.txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (Staffel 5, Folge 25)\r\nDauer: 00:48:05");

        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "de"),
            CreateAudioTrack(1, "AAC", language: "de"));

        var service = CreateMuxService(archiveDirectory);
        var directoryContext = service.CreateDirectoryDetectionContext(sourceDirectory);

        var detected = await service.DetectFromSelectedVideoAsync(mainVideoPath, directoryContext);

        Assert.Equal([preferredSubtitlePath], detected.SubtitlePaths);
    }

    [Fact]
    public async Task CreatePlanAsync_ExistingArchiveAndDuplicateAssSources_UsesPreferredAssOnly()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-plan-subtitle-dedup");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-plan-subtitle-dedup");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        var preferredSubtitlePath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-zdf.ass", "subtitle-preferred");
        var duplicateSubtitlePath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-ard.ass", "subtitle-duplicate");
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");

        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1280x720"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            outputPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            mainVideoPath,
            AudioDescriptionPath: null,
            SubtitlePaths: [preferredSubtitlePath, duplicateSubtitlePath],
            AttachmentPaths: [],
            outputPath,
            Title: "Pilot"));

        var externalSubtitle = Assert.Single(plan.SubtitleFiles, subtitle => !subtitle.IsEmbedded);
        Assert.Equal(preferredSubtitlePath, externalSubtitle.FilePath);
        Assert.DoesNotContain(duplicateSubtitlePath, plan.BuildArguments());
    }

    [Fact]
    public async Task CreatePlanAsync_PreservesMissingSubtitleKinds_FromNonSelectedDurationMatchedSource()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-supplement-subtitle-kinds");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-supplement-subtitle-kinds");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var zdfVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        var srfVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-srf.mp4");
        var zdfSubtitlePath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).srt", "subtitle-srt");
        var srfSubtitlePath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-srf.vtt", "subtitle-vtt");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02).txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02)-srf.txt",
            "Sender: SRF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");

        FakeMkvMergeTestHelper.WriteProbeFile(
            zdfVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            srfVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "3840x2160"),
            CreateAudioTrack(1, "E-AC-3"));

        var service = CreateMuxService(archiveDirectory);
        var detected = await service.DetectFromSelectedVideoAsync(zdfVideoPath);
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            detected.MainVideoPath,
            detected.AudioDescriptionPath,
            detected.SubtitlePaths,
            detected.AttachmentPaths,
            outputPath,
            detected.SuggestedTitle));

        Assert.Equal(srfVideoPath, detected.MainVideoPath);
        Assert.Equal(new[] { zdfSubtitlePath, srfSubtitlePath }, detected.SubtitlePaths);
        Assert.Equal(new[] { zdfSubtitlePath, srfSubtitlePath }, plan.SubtitleFiles.Select(subtitle => subtitle.FilePath));
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
        Assert.Contains(summary.Subtitles.CurrentItems, item => item.IsExisting && item.Text.Contains("SRT", StringComparison.Ordinal));
        Assert.Contains(summary.Subtitles.CurrentItems, item => item.IsAdded && item.Text.EndsWith(".ass", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.SubtitleFiles, subtitle => subtitle.IsEmbedded && subtitle.EmbeddedTrackId == 3);
        Assert.Contains(plan.SubtitleFiles, subtitle => !subtitle.IsEmbedded && string.Equals(subtitle.FilePath, subtitleAssPath, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.SubtitleFiles, subtitle => !subtitle.IsEmbedded && string.Equals(subtitle.FilePath, subtitleSrtPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            plan.Notes,
            note => note.Contains("Deutsch (hörgeschädigte) - SRT", StringComparison.Ordinal)
                && !note.Contains("SSA", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            summary.Notes,
            note => note.Contains("Deutsch (hörgeschädigte) - SRT", StringComparison.Ordinal)
                && !note.Contains("SSA", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(summary.Notes, note => note.StartsWith("Archiv-MKV", StringComparison.OrdinalIgnoreCase));

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
            "--forced-display-flag",
            "3:no",
            "--original-flag",
            "3:yes");
    }
}
