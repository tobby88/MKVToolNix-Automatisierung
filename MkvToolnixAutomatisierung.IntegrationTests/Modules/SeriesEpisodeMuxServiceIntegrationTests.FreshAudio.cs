using System.IO;
using System.Linq;
using MkvToolnixAutomatisierung.IntegrationTests.TestInfrastructure;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using Xunit;

namespace MkvToolnixAutomatisierung.IntegrationTests.Modules;

public sealed partial class SeriesEpisodeMuxServiceIntegrationTests
{
    [Fact]
    public async Task CreatePlanAsync_FreshTarget_KeepsMultipleNormalAudioTracksFromSingleSource()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source-fresh-multi-audio");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive-fresh-multi-audio");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3", language: "de"),
            CreateAudioTrack(2, "AAC", language: "en"),
            CreateAudioTrack(3, "AAC", trackName: "Deutsch (sehbehinderte) - AAC", isVisualImpaired: true));

        var service = CreateMuxService(archiveDirectory);
        var outputPath = Path.Combine(archiveDirectory, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");

        var plan = await service.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            mainVideoPath,
            AudioDescriptionPath: null,
            SubtitlePaths: [],
            AttachmentPaths: [],
            outputPath,
            Title: "Pilot"));

        Assert.False(plan.SkipMux);
        Assert.Equal([1, 2], plan.AudioSources.Select(source => source.TrackId).ToArray());
        Assert.Equal(["de", "en"], plan.AudioSources.Select(source => source.LanguageCode).ToArray());
        Assert.Equal(
            ["Deutsch - E-AC-3", "English - AAC"],
            plan.AudioSources.Select(source => source.TrackName).ToArray());
        Assert.Equal([1, 2], plan.PrimarySourceAudioTrackIds);

        var arguments = plan.BuildArguments();
        AssertContainsSequence(arguments, "--audio-tracks", "1,2");
        Assert.DoesNotContain("3:Deutsch (sehbehinderte) - AAC", arguments);
    }
}
