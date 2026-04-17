using System.IO;
using MkvToolnixAutomatisierung.IntegrationTests.TestInfrastructure;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using Xunit;

namespace MkvToolnixAutomatisierung.IntegrationTests.Modules;

public sealed partial class SeriesEpisodeMuxServiceIntegrationTests
{
    [Fact]
    public async Task DetectFromSelectedVideoAsync_SelectsBestVideoPerLanguageAndCodecSlot_AndOrdersLanguagesBeforeQuality()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-video-language-slots");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-video-language-slots");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var germanH264Path = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        var betterGermanH264Path = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-de-h264-besser.mp4");
        var germanH265Path = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-de-h265.mp4");
        var lowGermanH265Path = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-de-h265-alt.mp4");
        var plattdeutschH264Path = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-nds-h264.mp4");
        var englishH264Path = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-en-h264.mp4");
        var englishH265Path = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-en-h265.mp4");

        CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).txt", "de-h264");
        CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-de-h264-besser.txt", "de-h264-better");
        CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-de-h265.txt", "de-h265");
        CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-de-h265-alt.txt", "de-h265-old");
        CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-nds-h264.txt", "nds-h264");
        CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-en-h264.txt", "en-h264");
        CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-en-h265.txt", "en-h265");

        FakeMkvMergeTestHelper.WriteProbeFile(
            germanH264Path,
            CreateVideoTrack(0, "AVC/H.264", "1280x720", language: "de"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            betterGermanH264Path,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "de"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            germanH265Path,
            CreateVideoTrack(0, "HEVC/H.265", "3840x2160", language: "de"),
            CreateAudioTrack(1, "AAC"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            lowGermanH265Path,
            CreateVideoTrack(0, "HEVC/H.265", "1920x1080", language: "de"),
            CreateAudioTrack(1, "AAC"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            plattdeutschH264Path,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "nds"),
            CreateAudioTrack(1, "E-AC-3", language: "nds"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            englishH264Path,
            CreateVideoTrack(0, "AVC/H.264", "3840x2160", language: "en"),
            CreateAudioTrack(1, "E-AC-3", language: "en"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            englishH265Path,
            CreateVideoTrack(0, "HEVC/H.265", "3840x2160", language: "en"),
            CreateAudioTrack(1, "AAC", language: "en"));

        var service = CreateMuxService(archiveDirectory);

        var detected = await service.DetectFromSelectedVideoAsync(germanH264Path);

        Assert.Equal(betterGermanH264Path, detected.MainVideoPath);
        Assert.Equal(
            [germanH265Path, plattdeutschH264Path, englishH264Path, englishH265Path],
            detected.AdditionalVideoPaths);
        Assert.Equal(
            new[]
            {
                Path.ChangeExtension(betterGermanH264Path, ".txt"),
                Path.ChangeExtension(germanH265Path, ".txt"),
                Path.ChangeExtension(plattdeutschH264Path, ".txt"),
                Path.ChangeExtension(englishH264Path, ".txt"),
                Path.ChangeExtension(englishH265Path, ".txt")
            }.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
            detected.AttachmentPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePlanAsync_UsesProvidedPlannedVideoPaths_InsteadOfRefreshingDetection()
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
            detected.SuggestedTitle,
            PlannedVideoPaths: [detected.MainVideoPath, .. detected.AdditionalVideoPaths],
            DetectionNotes: detected.Notes));

        Assert.Single(plan.VideoSources);
        Assert.Equal(primaryVideoPath, plan.VideoSources[0].FilePath);
        Assert.DoesNotContain(plan.VideoSources, source => string.Equals(source.FilePath, alternateVideoPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DetectFromSelectedVideoAsync_StopsBeforeCandidateProbe_WhenProgressCallbackCancels()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-cancel-detection");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-cancel-detection");
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
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DetectFromSelectedVideoAsync(
            primaryVideoPath,
            update =>
            {
                if (update.StatusText.StartsWith("Analysiere Videoquelle", StringComparison.Ordinal))
                {
                    cancellation.Cancel();
                }
            },
            cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task DetectFromSelectedVideoAsync_CancelsRunningIdentify_WithoutPoisoningPreparedDirectoryCache()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-cancel-running-identify");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-cancel-running-identify");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var primaryVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02).txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");
        FakeMkvMergeTestHelper.WriteProbeFileWithDelay(
            primaryVideoPath,
            delayBeforeOutputMilliseconds: 5000,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var service = CreateMuxService(archiveDirectory);
        var directoryContext = service.CreateDirectoryDetectionContext(sourceDirectory);

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DetectFromSelectedVideoAsync(
            primaryVideoPath,
            directoryContext,
            cancellationToken: cancellationSource.Token));

        FakeMkvMergeTestHelper.WriteProbeFile(
            primaryVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var detected = await service.DetectFromSelectedVideoAsync(primaryVideoPath, directoryContext);

        Assert.Equal(primaryVideoPath, detected.MainVideoPath);
        Assert.Empty(detected.AdditionalVideoPaths);
    }

    [Fact]
    public async Task DetectFromSelectedVideoAsync_WithSharedDirectoryContext_ProbesSameVideoOnlyOnceAcrossConcurrentCalls()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-shared-context-single-probe");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-shared-context-single-probe");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var primaryVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        var invocationLogPath = Path.Combine(_tempDirectory, "probe-invocations.log");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02).txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");
        FakeMkvMergeTestHelper.WriteProbeFileWithDelayAndInvocationLog(
            primaryVideoPath,
            delayBeforeOutputMilliseconds: 500,
            invocationLogPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var service = CreateMuxService(archiveDirectory);
        var directoryContext = service.CreateDirectoryDetectionContext(sourceDirectory);

        var detections = await Task.WhenAll(
            service.DetectFromSelectedVideoAsync(primaryVideoPath, directoryContext),
            service.DetectFromSelectedVideoAsync(primaryVideoPath, directoryContext));

        Assert.All(detections, detected => Assert.Equal(primaryVideoPath, detected.MainVideoPath));
        Assert.Equal([primaryVideoPath], File.ReadAllLines(invocationLogPath));
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
    public async Task DetectFromSelectedVideoAsync_RemovesRepeatedSeriesPrefix_FromTxtEpisodeTitles()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-repeated-series-prefix");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-repeated-series-prefix");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Pippi Langstrumpf-Pippi Langstrumpf_ Pippi und die Seeräuber 2. Teil.mp4");
        CreateFile(
            sourceDirectory,
            "Pippi Langstrumpf-Pippi Langstrumpf_ Pippi und die Seeräuber 2. Teil.txt",
            "Sender: ORF\r\nThema: Pippi Langstrumpf\r\nTitel: Pippi Langstrumpf: Pippi und die Seeräuber 2. Teil\r\nDauer: 00:28:03");
        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "AAC"));

        var service = CreateMuxService(archiveDirectory);

        var detected = await service.DetectFromSelectedVideoAsync(mainVideoPath);

        Assert.Equal("Pippi Langstrumpf", detected.SeriesName);
        Assert.Equal("Pippi und die Seeräuber 2. Teil", detected.SuggestedTitle);
        Assert.EndsWith(
            Path.Combine("Pippi Langstrumpf", "Season xx", "Pippi Langstrumpf - SxxExx - Pippi und die Seeräuber 2. Teil.mkv"),
            detected.SuggestedOutputFilePath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectFromSelectedVideoAsync_GroupsBuettenwarderOpPlattWithNormalAndAdSources()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "Neues aus Büttenwarder");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-buettenwarder-op-platt");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var normalVideoPath = CreateFile(sourceDirectory, "Neues aus Büttenwarder-Liebesnacht-2129643944.mp4");
        var audioDescriptionPath = CreateFile(sourceDirectory, "Neues aus Büttenwarder-Liebesnacht (Audiodeskription)-0574946159.mp4");
        var opPlattVideoPath = CreateFile(sourceDirectory, "Neues aus Büttenwarder-Büttenwarder op Platt_ Liebesnacht-1307924250.mp4");
        var normalSubtitlePath = CreateFile(sourceDirectory, "Neues aus Büttenwarder-Liebesnacht-2129643944.srt", "subtitle");
        var audioDescriptionSubtitlePath = CreateFile(sourceDirectory, "Neues aus Büttenwarder-Liebesnacht (Audiodeskription)-0574946159.srt", "ad subtitle");

        CreateFile(
            sourceDirectory,
            "Neues aus Büttenwarder-Liebesnacht-2129643944.txt",
            "Sender: NDR\r\nThema: Neues aus Büttenwarder\r\nTitel: Liebesnacht\r\nDauer: 00:25:34");
        CreateFile(
            sourceDirectory,
            "Neues aus Büttenwarder-Liebesnacht (Audiodeskription)-0574946159.txt",
            "Sender: NDR\r\nThema: Neues aus Büttenwarder\r\nTitel: Liebesnacht (Audiodeskription)\r\nDauer: 00:25:34");
        CreateFile(
            sourceDirectory,
            "Neues aus Büttenwarder-Büttenwarder op Platt_ Liebesnacht-1307924250.txt",
            "Sender: NDR\r\nThema: Neues aus Büttenwarder\r\nTitel: Büttenwarder op Platt: Liebesnacht\r\nDauer: 00:25:34");

        FakeMkvMergeTestHelper.WriteProbeFile(
            normalVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "de"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            opPlattVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "nds"),
            CreateAudioTrack(1, "AAC", language: "nds"));

        var service = CreateMuxService(archiveDirectory);

        var detected = await service.DetectFromSelectedVideoAsync(opPlattVideoPath);

        Assert.Equal("Liebesnacht", detected.SuggestedTitle);
        Assert.Contains(detected.RelatedFilePaths, path => string.Equals(path, normalVideoPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(detected.RelatedFilePaths, path => string.Equals(path, audioDescriptionPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(detected.RelatedFilePaths, path => string.Equals(path, opPlattVideoPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(detected.RelatedFilePaths, path => string.Equals(path, normalSubtitlePath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(detected.RelatedFilePaths, path => string.Equals(path, audioDescriptionSubtitlePath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DetectFromSelectedVideoAsync_GroupsBuettenwarderLanguageVariants_WithSlightDurationDifferences()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-buettenwarder-duration-tolerance");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-buettenwarder-duration-tolerance");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var normalVideoPath = CreateFile(
            sourceDirectory,
            "Neues aus Büttenwarder-Bildungsschock-0186867506.mp4",
            "de");
        var opPlattVideoPath = CreateFile(
            sourceDirectory,
            "Neues aus Büttenwarder-Büttenwarder op Platt_ Bildungsschock-0183875890.mp4",
            new string('p', 64));

        CreateFile(
            sourceDirectory,
            "Neues aus Büttenwarder-Bildungsschock-0186867506.txt",
            "Sender: NDR\r\nThema: Neues aus Büttenwarder\r\nTitel: Bildungsschock\r\nDauer: 00:24:18");
        CreateFile(
            sourceDirectory,
            "Neues aus Büttenwarder-Büttenwarder op Platt_ Bildungsschock-0183875890.txt",
            "Sender: NDR\r\nThema: Neues aus Büttenwarder\r\nTitel: Büttenwarder op Platt: Bildungsschock\r\nDauer: 00:24:25");

        FakeMkvMergeTestHelper.WriteProbeFile(
            normalVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "de"),
            CreateAudioTrack(1, "AAC", language: "de"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            opPlattVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "und"),
            CreateAudioTrack(1, "AAC", language: "de"));

        var service = CreateMuxService(archiveDirectory);

        var detected = await service.DetectFromSelectedVideoAsync(opPlattVideoPath);

        Assert.Equal(normalVideoPath, detected.MainVideoPath);
        Assert.Contains(opPlattVideoPath, detected.AdditionalVideoPaths, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(detected.RelatedFilePaths, path => string.Equals(path, normalVideoPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(detected.RelatedFilePaths, path => string.Equals(path, opPlattVideoPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DetectFromSelectedVideoAsync_GroupsGenericFilmeTopicAndHoerfassungWithSeriesSources()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-pettersson-findus-generic-topic");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-pettersson-findus-generic-topic");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var zdfNormalPath = CreateFile(sourceDirectory, "Filme-Pettersson und Findus - Findus zieht um-0585373376.mp4");
        var kikaNormalPath = CreateFile(sourceDirectory, "Pettersson und Findus-Pettersson und Findus - Findus zieht um-0986869160.mp4");
        var hoerfassungPath = CreateFile(sourceDirectory, "Pettersson und Findus-Pettersson und Findus - Findus zieht um (Hörfassung)-1824923996.mp4");

        CreateFile(
            sourceDirectory,
            "Filme-Pettersson und Findus - Findus zieht um-0585373376.txt",
            "Sender: ZDF-tivi\r\nThema: Filme\r\nTitel: Pettersson und Findus - Findus zieht um\r\nDauer: 01:13:30");
        CreateFile(
            sourceDirectory,
            "Pettersson und Findus-Pettersson und Findus - Findus zieht um-0986869160.txt",
            "Sender: KiKA\r\nThema: Pettersson und Findus\r\nTitel: Pettersson und Findus - Findus zieht um\r\nDauer: 01:13:30");
        CreateFile(
            sourceDirectory,
            "Pettersson und Findus-Pettersson und Findus - Findus zieht um (Hörfassung)-1824923996.txt",
            "Sender: KiKA\r\nThema: Pettersson und Findus\r\nTitel: Pettersson und Findus - Findus zieht um (Hörfassung)\r\nDauer: 01:13:30");

        FakeMkvMergeTestHelper.WriteProbeFile(
            zdfNormalPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "de"),
            CreateAudioTrack(1, "AAC", language: "de"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            kikaNormalPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "de"),
            CreateAudioTrack(1, "AAC", language: "de"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            hoerfassungPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "de"),
            CreateAudioTrack(1, "AAC", trackName: "Deutsch (sehbehinderte) - AAC", isVisualImpaired: true, language: "de"));

        var service = CreateMuxService(archiveDirectory);

        var detected = await service.DetectFromSelectedVideoAsync(zdfNormalPath);

        Assert.Equal("Pettersson und Findus", detected.SeriesName);
        Assert.Equal("Findus zieht um", detected.SuggestedTitle);
        Assert.Equal(hoerfassungPath, detected.AudioDescriptionPath);
        Assert.Contains(detected.RelatedFilePaths, path => string.Equals(path, zdfNormalPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(detected.RelatedFilePaths, path => string.Equals(path, kikaNormalPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(detected.RelatedFilePaths, path => string.Equals(path, hoerfassungPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DetectFromSelectedVideoAsync_GroupsEditorialSeriesSuffixVariants_WhenMediathekCodesDiffer()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-editorial-series-suffix");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-editorial-series-suffix");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var canonicalPath = CreateFile(sourceDirectory, "Die Toten vom Bodensee-Der Seelenkreis (S02_E03)-1491353440.mp4");
        var editorialPath = CreateFile(sourceDirectory, "Die Toten vom Bodensee-Der Seelenkreis - aus der Reihe _Die Toten vom Bodensee_ (S2025_E04)-0779747296.mp4");
        var canonicalAdPath = CreateFile(sourceDirectory, "Die Toten vom Bodensee-Der Seelenkreis (S02_E03) (Audiodeskription)-0008179300.mp4");
        var editorialAdPath = CreateFile(sourceDirectory, "Die Toten vom Bodensee-Der Seelenkreis - aus der Reihe _Die Toten vom Bodensee_ (S2025_E04) (Audiodeskription)-0703426844.mp4");

        CreateFile(
            sourceDirectory,
            "Die Toten vom Bodensee-Der Seelenkreis (S02_E03)-1491353440.txt",
            "Sender: ZDF\r\nThema: Die Toten vom Bodensee\r\nTitel: Der Seelenkreis (S02/E03)\r\nDauer: 01:29:17");
        CreateFile(
            sourceDirectory,
            "Die Toten vom Bodensee-Der Seelenkreis - aus der Reihe _Die Toten vom Bodensee_ (S2025_E04)-0779747296.txt",
            "Sender: 3Sat\r\nThema: Die Toten vom Bodensee\r\nTitel: Der Seelenkreis - aus der Reihe \"Die Toten vom Bodensee\" (S2025/E04)\r\nDauer: 01:29:21");
        CreateFile(
            sourceDirectory,
            "Die Toten vom Bodensee-Der Seelenkreis (S02_E03) (Audiodeskription)-0008179300.txt",
            "Sender: ZDF\r\nThema: Die Toten vom Bodensee\r\nTitel: Der Seelenkreis (S02/E03) (Audiodeskription)\r\nDauer: 01:29:17");
        CreateFile(
            sourceDirectory,
            "Die Toten vom Bodensee-Der Seelenkreis - aus der Reihe _Die Toten vom Bodensee_ (S2025_E04) (Audiodeskription)-0703426844.txt",
            "Sender: 3Sat\r\nThema: Die Toten vom Bodensee\r\nTitel: Der Seelenkreis - aus der Reihe \"Die Toten vom Bodensee\" (S2025/E04) (Audiodeskription)\r\nDauer: 01:29:21");

        FakeMkvMergeTestHelper.WriteProbeFile(
            canonicalPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "de"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            editorialPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "de"),
            CreateAudioTrack(1, "E-AC-3"));

        var service = CreateMuxService(archiveDirectory);
        var directoryContext = service.CreateDirectoryDetectionContext(sourceDirectory);

        Assert.Single(directoryContext.MainVideoFiles);

        var detected = await service.DetectFromSelectedVideoAsync(canonicalPath, directoryContext);

        Assert.Equal("Der Seelenkreis", detected.SuggestedTitle);
        Assert.Contains(detected.RelatedFilePaths, path => string.Equals(path, canonicalPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(detected.RelatedFilePaths, path => string.Equals(path, editorialPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(detected.RelatedFilePaths, path => string.Equals(path, canonicalAdPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(detected.RelatedFilePaths, path => string.Equals(path, editorialAdPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DetectFromSelectedVideoAsync_DoesNotUseClearlyIncompleteMp4_ButKeepsItsSubtitles()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-incomplete-mp4");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-incomplete-mp4");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var normalVideoPath = CreateFile(sourceDirectory, "Neues aus Büttenwarder-Bildungsschock-0186867506.mp4");
        var incompleteVideoPath = CreateFile(sourceDirectory, "Neues aus Büttenwarder-Büttenwarder op Platt_ Bildungsschock-0183875890.mp4");
        var incompleteSubtitlePath = CreateFile(sourceDirectory, "Neues aus Büttenwarder-Büttenwarder op Platt_ Bildungsschock-0183875890.srt", "subtitle");
        CreateFile(
            sourceDirectory,
            "Neues aus Büttenwarder-Bildungsschock-0186867506.txt",
            "Sender: NDR\r\nThema: Neues aus Büttenwarder\r\nTitel: Bildungsschock\r\nDauer: 00:24:18");
        CreateFile(
            sourceDirectory,
            "Neues aus Büttenwarder-Büttenwarder op Platt_ Bildungsschock-0183875890.txt",
            "Sender: NDR\r\nThema: Neues aus Büttenwarder\r\nTitel: Büttenwarder op Platt: Bildungsschock\r\nDauer: 00:24:25\r\nGröße: 700,9 MiB");
        FakeMkvMergeTestHelper.WriteProbeFile(
            normalVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "de"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            incompleteVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "nds"),
            CreateAudioTrack(1, "AAC", language: "nds"));

        var service = CreateMuxService(archiveDirectory);

        var detected = await service.DetectFromSelectedVideoAsync(normalVideoPath);

        Assert.Equal(normalVideoPath, detected.MainVideoPath);
        Assert.DoesNotContain(incompleteVideoPath, detected.AdditionalVideoPaths);
        Assert.Contains(incompleteSubtitlePath, detected.SubtitlePaths);
        Assert.DoesNotContain(detected.RelatedFilePaths, path => string.Equals(path, incompleteVideoPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(detected.Notes, note => note.Contains("Defekte/unvollständige Quelle", StringComparison.OrdinalIgnoreCase));
    }
}
