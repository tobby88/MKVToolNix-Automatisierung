using System.IO;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.IntegrationTests.TestInfrastructure;
using Xunit;

namespace MkvToolnixAutomatisierung.IntegrationTests.Modules;

public sealed partial class SeriesEpisodeMuxServiceIntegrationTests
{
    [Fact]
    public async Task DetectFromSelectedVideoAsync_AudioDescriptionOnly_DoesNotFail_AndMarksMissingPrimaryVideo()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-ad-only-detect");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-ad-only-detect");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var audioDescriptionPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02) Audiodeskription.mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02) Audiodeskription.txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");

        var service = CreateMuxService(archiveDirectory);

        var detected = await service.DetectFromSelectedVideoAsync(audioDescriptionPath);

        Assert.False(detected.HasPrimaryVideoSource);
        Assert.Equal(audioDescriptionPath, detected.MainVideoPath);
        Assert.Equal(audioDescriptionPath, detected.AudioDescriptionPath);
        Assert.Contains(
            detected.Notes,
            note => note.Contains("keine passende frische Hauptquelle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DetectFromSelectedVideoAsync_AudioDescriptionOnly_UsesSeriesDirectoryFallback_ForLegacyFileNamesWithoutTxt()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-ad-only-legacy", "SOKO Köln");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-ad-only-legacy");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var audioDescriptionPath = CreateFile(sourceDirectory, "SOKO Köln-Der Bienenkönig (Audiodeskription)-1427732091.mp4");
        FakeMkvMergeTestHelper.WriteProbeFile(
            audioDescriptionPath,
            CreateAudioTrack(0, "AAC", trackName: "Deutsch (sehbehinderte) - AAC", isVisualImpaired: true));

        var service = CreateMuxService(archiveDirectory);

        var detected = await service.DetectFromSelectedVideoAsync(audioDescriptionPath);

        Assert.False(detected.HasPrimaryVideoSource);
        Assert.Equal("SOKO Köln", detected.SeriesName);
        Assert.Equal("Der Bienenkönig", detected.SuggestedTitle);
        Assert.Equal("xx", detected.SeasonNumber);
        Assert.Equal("xx", detected.EpisodeNumber);
        Assert.EndsWith(
            Path.Combine("SOKO Köln", "Season xx", "SOKO Köln - SxxExx - Der Bienenkönig.mkv"),
            detected.SuggestedOutputFilePath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectFromSelectedVideoAsync_AudioDescriptionOnly_ParsesLegacyHoerfassungFileName_WithoutTxt()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-ad-only-hoerfassung", "Der Kommissar und");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-ad-only-hoerfassung");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var audioDescriptionPath = CreateFile(
            sourceDirectory,
            "Der Kommissar und das Meer-Hörfassung_ In einem kalten Land - Der Samstagskrimi (Audiodeskriptio.mp4");
        FakeMkvMergeTestHelper.WriteProbeFile(
            audioDescriptionPath,
            CreateAudioTrack(0, "AAC", trackName: "Deutsch (sehbehinderte) - AAC", isVisualImpaired: true));

        var service = CreateMuxService(archiveDirectory);

        var detected = await service.DetectFromSelectedVideoAsync(audioDescriptionPath);

        Assert.False(detected.HasPrimaryVideoSource);
        Assert.Equal("Der Kommissar und das Meer", detected.SeriesName);
        Assert.Equal("In einem kalten Land", detected.SuggestedTitle);
        Assert.Equal("xx", detected.SeasonNumber);
        Assert.Equal("xx", detected.EpisodeNumber);
        Assert.EndsWith(
            Path.Combine(
                "Der Kommissar und das Meer",
                "Season xx",
                "Der Kommissar und das Meer - SxxExx - In einem kalten Land.mkv"),
            detected.SuggestedOutputFilePath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreatePlanAsync_AudioDescriptionOnly_WithExistingArchiveTarget_UsesArchiveAsPrimarySource()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-ad-only-existing");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-ad-only-existing");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var audioDescriptionPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02) Audiodeskription.mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02) Audiodeskription.txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");

        FakeMkvMergeTestHelper.WriteProbeFile(
            audioDescriptionPath,
            CreateAudioTrack(0, "AAC", trackName: "Deutsch (sehbehinderte) - AAC", isVisualImpaired: true));
        FakeMkvMergeTestHelper.WriteProbeFile(
            outputPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3", trackName: "Deutsch - E-AC-3"));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            MainVideoPath: audioDescriptionPath,
            AudioDescriptionPath: audioDescriptionPath,
            SubtitlePaths: [],
            AttachmentPaths: [],
            OutputFilePath: outputPath,
            Title: "Pilot",
            HasPrimaryVideoSource: false));

        Assert.False(plan.SkipMux);
        Assert.Equal(outputPath, plan.VideoSources[0].FilePath);
        Assert.NotNull(plan.WorkingCopy);
        Assert.Equal(audioDescriptionPath, plan.AudioDescriptionFilePath);
        Assert.Equal(0, plan.AudioDescriptionTrackId);
        Assert.Contains(
            plan.Notes,
            note => note.Contains("Zusatzmaterial", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePlanAsync_SubtitleOnly_WithExistingArchiveTarget_UsesArchiveAsPrimarySource()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-subtitle-only-existing");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-subtitle-only-existing");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var subtitlePath = CreateFile(sourceDirectory, "Marie Brand - Pilot (S01_E02).ass", "subtitle");
        CreateFile(
            sourceDirectory,
            "Marie Brand - Pilot (S01_E02).txt",
            "Sender: ZDF\r\nThema: Marie Brand\r\nTitel: Pilot (S01/E02)\r\nDauer: 01:29:00");
        var outputPath = Path.Combine(archiveDirectory, "Marie Brand", "Season 1", "Marie Brand - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");

        FakeMkvMergeTestHelper.WriteProbeFile(
            outputPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", trackName: "Deutsch - FHD - H.264"),
            CreateAudioTrack(1, "E-AC-3", trackName: "Deutsch - E-AC-3"));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            MainVideoPath: subtitlePath,
            AudioDescriptionPath: null,
            SubtitlePaths: [subtitlePath],
            AttachmentPaths: [],
            OutputFilePath: outputPath,
            Title: "Pilot",
            HasPrimaryVideoSource: false));

        Assert.False(plan.SkipMux);
        Assert.Equal(outputPath, plan.VideoSources[0].FilePath);
        Assert.NotNull(plan.WorkingCopy);
        Assert.Null(plan.AudioDescriptionFilePath);
        Assert.Contains(plan.SubtitleFiles, subtitle => string.Equals(subtitle.FilePath, subtitlePath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            plan.Notes,
            note => note.Contains("Zusatzmaterial", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePlanAsync_AudioDescriptionOnly_WithAlreadyCompleteArchiveTarget_ReturnsSkipPlan()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-ad-only-skip");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-ad-only-skip");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var audioDescriptionPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02) Audiodeskription.mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02) Audiodeskription.txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");
        FakeMkvMergeTestHelper.WriteProbeFile(
            audioDescriptionPath,
            CreateAudioTrack(0, "AAC", trackName: "Deutsch (sehbehinderte) - AAC", isVisualImpaired: true));

        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");
        FakeMkvMergeTestHelper.WriteProbeFile(
            outputPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", trackName: "Deutsch - FHD - H.264"),
            CreateAudioTrack(1, "E-AC-3", trackName: "Deutsch - E-AC-3"),
            CreateAudioTrack(2, "AAC", trackName: "Deutsch (sehbehinderte) - AAC", isVisualImpaired: true));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            MainVideoPath: audioDescriptionPath,
            AudioDescriptionPath: audioDescriptionPath,
            SubtitlePaths: [],
            AttachmentPaths: [],
            OutputFilePath: outputPath,
            Title: "Pilot",
            HasPrimaryVideoSource: false));

        Assert.True(plan.SkipMux);
        Assert.Equal(outputPath, plan.OutputFilePath);
        Assert.Contains(
            "erneutes Muxen ist nicht nötig",
            plan.SkipReason ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreatePlanAsync_AudioDescriptionOnly_WithoutExistingArchiveTarget_ThrowsHelpfulMessage()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-ad-only-missing");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-ad-only-missing");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var audioDescriptionPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02) Audiodeskription.mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02) Audiodeskription.txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");

        FakeMkvMergeTestHelper.WriteProbeFile(
            audioDescriptionPath,
            CreateAudioTrack(0, "AAC", trackName: "Deutsch (sehbehinderte) - AAC", isVisualImpaired: true));

        var service = CreateMuxService(archiveDirectory);
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            MainVideoPath: audioDescriptionPath,
            AudioDescriptionPath: audioDescriptionPath,
            SubtitlePaths: [],
            AttachmentPaths: [],
            OutputFilePath: outputPath,
            Title: "Pilot",
            HasPrimaryVideoSource: false)));

        Assert.Contains("keine passende Hauptquelle", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
