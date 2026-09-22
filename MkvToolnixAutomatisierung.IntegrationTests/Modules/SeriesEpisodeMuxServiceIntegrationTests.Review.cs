using System.IO;
using MkvToolnixAutomatisierung.IntegrationTests.TestInfrastructure;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using Xunit;

namespace MkvToolnixAutomatisierung.IntegrationTests.Modules;

public sealed partial class SeriesEpisodeMuxServiceIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Detection_DoesNotSalvageSubtitlesFromDifferentCut(bool defectiveVideo)
    {
        var source = Path.Combine(_tempDirectory, "cut-source");
        var main = CreateFile(source, "Beispielserie - Pilot (S01_E02).mp4");
        CreateFile(source, "Beispielserie - Pilot (S01_E02).txt", "Thema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");
        var subtitle = CreateFile(source, "Beispielserie - Pilot (S01_E02)-2.srt");
        CreateFile(source, "Beispielserie - Pilot (S01_E02)-2.txt",
            "Thema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 01:24:00\r\nGr\u00f6\u00dfe: 700,9 MiB");
        if (defectiveVideo)
        {
            CreateFile(source, "Beispielserie - Pilot (S01_E02)-2.mp4");
        }
        FakeMkvMergeTestHelper.WriteProbeFile(main, CreateVideoTrack(0, "AVC/H.264", "1920x1080"), CreateAudioTrack(1, "AAC"));

        var detected = await CreateMuxService(Path.Combine(_tempDirectory, "archive")).DetectFromSelectedVideoAsync(main);

        Assert.DoesNotContain(subtitle, detected.SubtitlePaths);
        Assert.DoesNotContain(subtitle, detected.RelatedFilePaths);
    }

    [Fact]
    public async Task Detection_DoesNotProbeExcludedInvalidVideo()
    {
        var source = Path.Combine(_tempDirectory, "excluded-source");
        var main = CreateFile(source, "Beispielserie - Pilot (S01_E02).mp4");
        var excluded = CreateFile(source, "Beispielserie - Pilot (S01_E02)-2.mp4");
        FakeMkvMergeTestHelper.WriteProbeFile(main, CreateVideoTrack(0, "AVC/H.264", "1920x1080"), CreateAudioTrack(1, "AAC"));
        FakeMkvMergeTestHelper.WriteProbeFile(excluded, CreateAudioTrack(1, "AAC"));

        var detected = await CreateMuxService(Path.Combine(_tempDirectory, "archive"))
            .DetectFromSelectedVideoAsync(main, excludedSourcePaths: [excluded]);

        Assert.Equal(main, detected.MainVideoPath);
        Assert.DoesNotContain(excluded, detected.RelatedFilePaths);
    }

    [Fact]
    public async Task Detection_SubtitleOnly_DoesNotMixDifferentCuts()
    {
        var source = Path.Combine(_tempDirectory, "subtitle-only-source");
        var main = CreateFile(source, "Beispielserie - Pilot (S01_E02).srt");
        CreateFile(source, "Beispielserie - Pilot (S01_E02).txt", "Dauer: 00:42:00");
        var other = CreateFile(source, "Beispielserie - Pilot (S01_E02)-2.vtt");
        CreateFile(source, "Beispielserie - Pilot (S01_E02)-2.txt", "Dauer: 01:24:00");

        var detected = await CreateMuxService(Path.Combine(_tempDirectory, "archive")).DetectFromSelectedVideoAsync(main);

        Assert.Equal([main], detected.SubtitlePaths);
        Assert.DoesNotContain(other, detected.RelatedFilePaths);
    }

    [Fact]
    public async Task CreatePlanAsync_FreshTarget_DeduplicatesSubtitleKindsUsingRequestPriority()
    {
        var source = Path.Combine(_tempDirectory, "subtitle-source");
        var main = CreateFile(source, "video.mp4");
        var preferred = CreateFile(source, "z-preferred.srt");
        var duplicate = CreateFile(source, "a-duplicate.srt");
        FakeMkvMergeTestHelper.WriteProbeFile(main, CreateVideoTrack(0, "AVC/H.264", "1920x1080"), CreateAudioTrack(1, "AAC"));
        var service = CreateMuxService(Path.Combine(_tempDirectory, "archive"));

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(main, null, [preferred, duplicate], [],
            Path.Combine(_tempDirectory, "output.mkv"), "Pilot", PlannedVideoPaths: [main]));

        Assert.Equal(preferred, Assert.Single(plan.SubtitleFiles).FilePath);
    }

    [Fact]
    public async Task CreatePlanAsync_RejectsContainerAsExternalSubtitle()
    {
        var main = CreateFile(_tempDirectory, "video.mp4");
        var subtitle = CreateFile(_tempDirectory, "not-a-subtitle.mkv");

        await Assert.ThrowsAsync<ArgumentException>(() => CreateMuxService(Path.Combine(_tempDirectory, "archive"))
            .CreatePlanAsync(new SeriesEpisodeMuxRequest(main, null, [subtitle], [],
                Path.Combine(_tempDirectory, "output.mkv"), "Pilot", PlannedVideoPaths: [main])));
    }

    [Fact]
    public async Task CreatePlanAsync_FreshTarget_SelectsMarkedAdRatherThanNormalFirstTrack()
    {
        var main = CreateFile(_tempDirectory, "video.mp4");
        var ad = CreateFile(_tempDirectory, "audio-description.mp4");
        FakeMkvMergeTestHelper.WriteProbeFile(main, CreateVideoTrack(0, "AVC/H.264", "1920x1080"), CreateAudioTrack(1, "AAC"));
        FakeMkvMergeTestHelper.WriteProbeFile(ad, CreateAudioTrack(1, "AAC"), CreateAudioTrack(2, "AC-3", isVisualImpaired: true));

        var plan = await CreateMuxService(Path.Combine(_tempDirectory, "archive")).CreatePlanAsync(
            new SeriesEpisodeMuxRequest(main, ad, [], [], Path.Combine(_tempDirectory, "output.mkv"), "Pilot", PlannedVideoPaths: [main]));

        Assert.Equal(2, Assert.Single(plan.AudioDescriptionSources).TrackId);
        AssertContainsSequence(plan.BuildArguments(), "--audio-tracks", "2", "--language", "2:de");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreatePlanAsync_ReplacingGermanAd_PreservesEnglishArchiveAd(bool upgradeVideo)
    {
        var archive = Path.Combine(_tempDirectory, "archive");
        var output = CreateFile(archive, "Beispielserie - S01E02 - Pilot.mkv");
        var main = CreateFile(_tempDirectory, "video.mp4");
        var ad = CreateFile(_tempDirectory, "audio-description.mp4");
        FakeMkvMergeTestHelper.WriteProbeFile(main,
            CreateVideoTrack(0, "AVC/H.264", upgradeVideo ? "3840x2160" : "1280x720"), CreateAudioTrack(1, "AAC"));
        FakeMkvMergeTestHelper.WriteProbeFile(ad,
            CreateAudioTrack(1, "AAC"), CreateAudioTrack(2, "AC-3", isVisualImpaired: true));
        FakeMkvMergeTestHelper.WriteProbeFile(output,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "AAC"),
            CreateAudioTrack(3, "AAC", isVisualImpaired: true, language: "en"),
            CreateAudioTrack(4, "AAC", isVisualImpaired: true, language: "de"));

        var plan = await CreateMuxService(archive).CreatePlanAsync(
            new SeriesEpisodeMuxRequest(main, ad, [], [], output, "Pilot", PlannedVideoPaths: [main]));

        Assert.False(plan.SkipMux);
        Assert.Equal(2, plan.AudioDescriptionSources.Count);
        Assert.Contains(plan.AudioDescriptionSources, track => track.FilePath == ad && track.TrackId == 2);
        Assert.Contains(plan.AudioDescriptionSources, track => track.FilePath == output && track.TrackId == 3);
        Assert.DoesNotContain(plan.AudioDescriptionSources, track => track.FilePath == output && track.TrackId == 4);
        Assert.NotNull(plan.WorkingCopy);
        Assert.True(plan.BuildUsageSummary().AudioDescription.HasRemoved);
        Assert.DoesNotContain("English", plan.BuildUsageSummary().AudioDescription.RemovedText ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatePlanAsync_MultilingualPlattSource_PreservesIndividualAudioLanguages()
    {
        var main = CreateFile(_tempDirectory, "Pilot op Platt.mp4");
        FakeMkvMergeTestHelper.WriteProbeFile(main,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "AAC", language: "nds"),
            CreateAudioTrack(2, "AAC", language: "en"));

        var plan = await CreateMuxService(Path.Combine(_tempDirectory, "archive")).CreatePlanAsync(
            new SeriesEpisodeMuxRequest(main, null, [], [], Path.Combine(_tempDirectory, "output.mkv"), "Pilot", PlannedVideoPaths: [main]));

        Assert.Equal(["nds", "en"], plan.AudioSources.Select(track => track.LanguageCode).ToArray());
    }

    [Fact]
    public async Task CreatePlanAsync_UnusedFreshTextDoesNotRemoveOnlyArchiveText()
    {
        var archive = Path.Combine(_tempDirectory, "archive");
        var output = CreateFile(archive, "Beispielserie - S01E02 - Pilot.mkv");
        var main = CreateFile(_tempDirectory, "video.mp4");
        var unusedText = CreateFile(_tempDirectory, "unused-video.txt");
        FakeMkvMergeTestHelper.WriteProbeFile(main,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"), CreateAudioTrack(1, "AAC"));
        FakeMkvMergeTestHelper.WriteProbeFileWithAttachments(output, [CreateAttachment("only-archive-text.txt", id: 7)],
            CreateVideoTrack(0, "AVC/H.264", "1280x720"), CreateAudioTrack(1, "AAC"));

        var plan = await CreateMuxService(archive).CreatePlanAsync(
            new SeriesEpisodeMuxRequest(main, null, [], [unusedText], output, "Pilot", PlannedVideoPaths: [main]));

        Assert.Equal(["only-archive-text.txt"], plan.PreservedAttachmentNames);
        Assert.Empty(plan.AttachmentFilePaths);
        Assert.NotNull(plan.WorkingCopy);
    }

    [Fact]
    public async Task CreatePlanAsync_KeepingArchivePrimary_CorrectsRawLanguageWithoutChangingSlots()
    {
        var archive = Path.Combine(_tempDirectory, "archive");
        var output = CreateFile(archive, "Beispielserie - S01E02 - Pilot.mkv");
        var main = CreateFile(_tempDirectory, "video.mp4");
        FakeMkvMergeTestHelper.WriteProbeFile(main,
            CreateVideoTrack(0, "AVC/H.264", "1280x720", trackName: "Plattdüütsch - HD - H.264"),
            CreateAudioTrack(1, "AAC", trackName: "Plattdüütsch - AAC"));
        FakeMkvMergeTestHelper.WriteProbeFileWithContainerTitle(output, "Pilot",
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", trackName: "Plattdüütsch - FHD - H.264", isDefaultTrack: true),
            CreateAudioTrack(1, "AAC", trackName: "Plattdüütsch - AAC", isDefaultTrack: true));

        var plan = await CreateMuxService(archive).CreatePlanAsync(
            new SeriesEpisodeMuxRequest(main, null, [], [], output, "Pilot", PlannedVideoPaths: [main]));

        Assert.False(plan.SkipMux);
        Assert.True(plan.HasHeaderEdits);
        Assert.Null(plan.WorkingCopy);
        var video = Assert.Single(plan.VideoSources);
        Assert.Equal(output, video.FilePath);
        Assert.Equal("nds", video.LanguageCode);
        var audio = Assert.Single(plan.AudioSources);
        Assert.Equal(output, audio.FilePath);
        Assert.Equal("nds", audio.LanguageCode);
        Assert.Equal(2, plan.TrackHeaderEdits.Count);
        Assert.All(plan.TrackHeaderEdits, operation =>
        {
            var edit = Assert.Single(operation.ValueEdits!);
            Assert.Equal("language", edit.PropertyName);
            Assert.Equal("de", edit.CurrentDisplayValue);
            Assert.Equal("nds", edit.ExpectedMkvPropEditValue);
        });
        AssertContainsSequence(plan.BuildArguments(), "--edit", "track:1", "--set", "language=nds");
        AssertContainsSequence(plan.BuildArguments(), "--edit", "track:2", "--set", "language=nds");
        Assert.Contains("Sprache: de -> nds", plan.BuildPreviewText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatePlanAsync_KeepingArchivePrimary_ReportsOnlyRemovedNormalAudio()
    {
        var archive = Path.Combine(_tempDirectory, "archive");
        var output = CreateFile(archive, "Beispielserie - S01E02 - Pilot.mkv");
        var main = CreateFile(_tempDirectory, "video.mp4");
        FakeMkvMergeTestHelper.WriteProbeFile(main,
            CreateVideoTrack(0, "HEVC/H.265", "1920x1080"), CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(output,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "AAC", trackName: "Archiv Deutsch - AAC"),
            CreateAudioTrack(2, "AAC", trackName: "English - AAC", language: "en"));

        var plan = await CreateMuxService(archive).CreatePlanAsync(
            new SeriesEpisodeMuxRequest(main, null, [], [], output, "Pilot", PlannedVideoPaths: [main]));

        Assert.Equal(output, plan.VideoSources[0].FilePath);
        Assert.Contains(plan.VideoSources, source => source.FilePath == main);
        Assert.Contains(plan.AudioSources, source => source.FilePath == main && source.TrackId == 1);
        Assert.Contains(plan.AudioSources, source => source.FilePath == output && source.TrackId == 2);
        Assert.DoesNotContain(plan.AudioSources, source => source.FilePath == output && source.TrackId == 1);
        var summary = plan.BuildUsageSummary().Audio;
        Assert.True(summary.HasRemoved);
        Assert.Contains("Archiv Deutsch - AAC", summary.RemovedText, StringComparison.Ordinal);
        Assert.DoesNotContain("English - AAC", summary.RemovedText!, StringComparison.Ordinal);
        Assert.Contains("Tonspuren", summary.RemovedReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_WarningExitWithoutRecognizedWarningLine_SetsWarningStatus()
    {
        var output = Path.Combine(_tempDirectory, "warning-output.mkv");
        FakeMkvMergeTestHelper.WriteMuxRunFile(output, exitCode: 1, lines: ["Progress: 100%"]);
        var updates = new List<MuxExecutionUpdate>();

        var result = await CreateMuxService(Path.Combine(_tempDirectory, "archive"))
            .ExecuteAsync(CreateExecutionTestPlan(output), onUpdate: updates.Add);

        Assert.Equal(1, result.ExitCode);
        Assert.True(result.HasWarning);
        Assert.True(updates[^1].HasWarning);
    }

    [Fact]
    public async Task ExecuteAsync_FailedHeaderEdit_DoesNotReportCompletion()
    {
        var output = CreateFile(_tempDirectory, "header-failure.mkv");
        // No probe JSON: the fake header editor returns a fatal error without touching the file.
        var updates = new List<MuxExecutionUpdate>();
        var result = await CreateMuxService(Path.Combine(_tempDirectory, "archive"))
            .ExecuteAsync(CreateExecutionTestPlan(output, headerEdit: true), onUpdate: updates.Add);

        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEqual(1, result.ExitCode);
        Assert.Null(result.LastProgressPercent);
        Assert.DoesNotContain(updates, update => update.ProgressPercent == 100);
    }

    [Fact]
    public async Task ExecuteAsync_SkipPlanDoesNotLaunchExecutable()
    {
        var plan = SeriesEpisodeMuxPlan.CreateSkip("does-not-exist.exe", Path.Combine(_tempDirectory, "output.mkv"), "Pilot", "Already current");
        var result = await CreateMuxService(Path.Combine(_tempDirectory, "archive")).ExecuteAsync(plan);
        Assert.Equal(0, result.ExitCode);
    }

    private SeriesEpisodeMuxPlan CreateExecutionTestPlan(string output, bool headerEdit = false)
    {
        var source = CreateFile(_tempDirectory, "execution-source.mp4");
        return new SeriesEpisodeMuxPlan(
            FakeMkvMergeTestHelper.ResolveExecutablePath(), output, "Pilot",
            [new VideoSourcePlan(source, 0, "Video", true)],
            [new AudioSourcePlan(source, 1, "Audio", true)],
            primarySourceAudioTrackIds: [1], primarySourceSubtitleTrackIds: [], primarySourceAttachmentIds: null,
            includePrimarySourceAttachments: false, attachmentSourcePath: null, attachmentSourceAttachmentIds: null,
            audioDescriptionFilePath: null, audioDescriptionTrackId: null, audioDescriptionTrackName: null, audioDescriptionLanguageCode: null,
            subtitleFiles: [], attachmentFilePaths: [], preservedAttachmentNames: [], usageComparison: ArchiveUsageComparison.Empty, workingCopy: null,
            mkvPropEditPath: headerEdit ? FakeMkvMergeTestHelper.ResolveExecutablePath() : null,
            containerTitleEdit: headerEdit ? new ContainerTitleEditOperation("Old", "Pilot") : null);
    }
}
