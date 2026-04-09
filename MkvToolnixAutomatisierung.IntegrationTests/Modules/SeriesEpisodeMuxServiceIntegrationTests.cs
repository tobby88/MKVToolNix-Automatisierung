using System.IO;
using MkvToolnixAutomatisierung.IntegrationTests.TestInfrastructure;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.IntegrationTests.Modules;

[Collection("PortableStorage")]
public sealed partial class SeriesEpisodeMuxServiceIntegrationTests : IDisposable
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
    public async Task CreatePlanAsync_FreshTarget_OrdersVideoTracksByLanguageThenCodec()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-plan-video-language-order");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-plan-video-language-order");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var germanH264Path = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        var germanH265Path = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-de-h265.mp4");
        var plattdeutschH264Path = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-nds-h264.mp4");
        var englishH264Path = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-en-h264.mp4");

        CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).txt", "de-h264");
        CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-de-h265.txt", "de-h265");
        CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-nds-h264.txt", "nds-h264");
        CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-en-h264.txt", "en-h264");

        FakeMkvMergeTestHelper.WriteProbeFile(
            germanH264Path,
            CreateVideoTrack(0, "AVC/H.264", "1280x720", language: "de"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            germanH265Path,
            CreateVideoTrack(0, "HEVC/H.265", "3840x2160", language: "de"),
            CreateAudioTrack(1, "AAC"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            plattdeutschH264Path,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "nds"),
            CreateAudioTrack(1, "E-AC-3", language: "nds"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            englishH264Path,
            CreateVideoTrack(0, "AVC/H.264", "3840x2160", language: "en"),
            CreateAudioTrack(1, "E-AC-3", language: "en"));

        var service = CreateMuxService(archiveDirectory);
        var detected = await service.DetectFromSelectedVideoAsync(germanH264Path);
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            detected.MainVideoPath,
            detected.AudioDescriptionPath,
            detected.SubtitlePaths,
            detected.AttachmentPaths,
            outputPath,
            detected.SuggestedTitle));

        Assert.Equal(
            [germanH264Path, germanH265Path, plattdeutschH264Path, englishH264Path],
            plan.VideoSources.Select(source => source.FilePath).ToList());
        Assert.Equal(
            ["de", "de", "nds", "en"],
            plan.VideoSources.Select(source => source.LanguageCode).ToList());
        Assert.Equal(
            [
                "Deutsch - HD - H.264",
                "Deutsch - UHD - H.265",
                "Plattdüütsch - FHD - H.264",
                "English - UHD - H.264"
            ],
            plan.VideoSources.Select(source => source.TrackName).ToList());
        Assert.Equal(
            [germanH264Path, germanH265Path, plattdeutschH264Path, englishH264Path],
            plan.AudioSources.Select(source => source.FilePath).ToList());
        Assert.Equal(
            ["de", "de", "nds", "en"],
            plan.AudioSources.Select(source => source.LanguageCode).ToList());
        Assert.Equal(
            [
                "Deutsch - E-AC-3",
                "Deutsch - AAC",
                "Plattdüütsch - E-AC-3",
                "English - E-AC-3"
            ],
            plan.AudioSources.Select(source => source.TrackName).ToList());

        var arguments = plan.BuildArguments();
        AssertContainsSequence(arguments, "--audio-tracks", "1");
        Assert.Contains("1:Plattdüütsch - E-AC-3", arguments);
        Assert.Contains("1:English - E-AC-3", arguments);
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
    public async Task CreatePlanAsync_ReplacingArchivePrimary_DropsTextAttachmentForFreshVideoThatLostArchiveComparison()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-drop-unused-fresh-text");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-drop-unused-fresh-text");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var freshGermanH264Path = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        var freshGermanH264AttachmentPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).txt", "fresh-h264");
        var freshGermanH265Path = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-2.mp4");
        var freshGermanH265AttachmentPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02)-2.txt", "fresh-h265");
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
        CreateFile(Path.GetDirectoryName(outputPath)!, Path.GetFileName(outputPath), "archive");

        FakeMkvMergeTestHelper.WriteProbeFile(
            freshGermanH264Path,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080", language: "de"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            freshGermanH265Path,
            CreateVideoTrack(0, "HEVC/H.265", "1280x720", language: "de"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            outputPath,
            CreateVideoTrack(0, "AVC/H.264", "1280x720", language: "de"),
            CreateAudioTrack(1, "E-AC-3"),
            CreateVideoTrack(2, "HEVC/H.265", "1920x1080", language: "de"));

        var service = CreateMuxService(archiveDirectory);

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            freshGermanH264Path,
            AudioDescriptionPath: null,
            SubtitlePaths: [],
            AttachmentPaths: [freshGermanH264AttachmentPath, freshGermanH265AttachmentPath],
            outputPath,
            Title: "Pilot"));

        Assert.False(plan.SkipMux);
        Assert.Contains(freshGermanH264AttachmentPath, plan.AttachmentFilePaths);
        Assert.DoesNotContain(freshGermanH265AttachmentPath, plan.AttachmentFilePaths);
        Assert.Contains(plan.VideoSources, source => string.Equals(source.FilePath, outputPath, StringComparison.OrdinalIgnoreCase) && source.TrackId == 2);
        Assert.DoesNotContain(plan.VideoSources, source => string.Equals(source.FilePath, freshGermanH265Path, StringComparison.OrdinalIgnoreCase));
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

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private SeriesEpisodeMuxService CreateMuxService(
        string archiveDirectory,
        IMediaDurationProbe? durationProbe = null)
    {
        var settingsStore = CreateSettingsStore();
        var probeService = new MkvMergeProbeService();
        var archiveService = new SeriesArchiveService(probeService, new AppArchiveSettingsStore(settingsStore));
        archiveService.ConfigureArchiveRootDirectory(archiveDirectory);
        var effectiveDurationProbe = durationProbe ?? new NullDurationProbe();

        return new SeriesEpisodeMuxService(
            new SeriesEpisodeMuxPlanner(
                new MkvToolNixLocator(new AppToolPathStore(settingsStore)),
                probeService,
                archiveService,
                effectiveDurationProbe),
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

    private static object CreateVideoTrack(
        int id,
        string codec,
        string pixelDimensions,
        string trackName = "",
        string language = "de")
    {
        return new
        {
            id,
            type = "video",
            codec,
            properties = new
            {
                pixel_dimensions = pixelDimensions,
                language_ietf = language,
                track_name = trackName
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

    private static object CreateAttachment(string fileName, int? id = null, string? textContent = null)
    {
        var attachment = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["file_name"] = fileName
        };
        if (id is not null)
        {
            attachment["id"] = id.Value;
        }

        if (textContent is not null)
        {
            attachment["text_content"] = textContent;
        }

        return attachment;
    }

    private static string BuildAttachmentTextContent(string title, string? mediaUrl = null)
    {
        var lines = new List<string>
        {
            "Sender: NDR",
            "Thema: Beispielserie",
            $"Titel: {title}"
        };
        if (!string.IsNullOrWhiteSpace(mediaUrl))
        {
            lines.Add("URL");
            lines.Add(string.Empty);
            lines.Add(mediaUrl);
        }

        return string.Join("\r\n", lines);
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

    private sealed class DictionaryDurationProbe : IMediaDurationProbe
    {
        private readonly IReadOnlyDictionary<string, TimeSpan> _durations;

        public DictionaryDurationProbe(IReadOnlyDictionary<string, TimeSpan> durations)
        {
            _durations = durations;
        }

        public TimeSpan? TryReadDuration(string filePath)
        {
            return _durations.TryGetValue(filePath, out var duration)
                ? duration
                : null;
        }
    }
}
