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
}
