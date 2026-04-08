using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class AudioTrackClassifierTests
{
    [Theory]
    [InlineData("", true, true)]
    [InlineData("Deutsch (sehbehinderte) - AAC", false, true)]
    [InlineData("Deutsch Audiodeskription - AAC", false, true)]
    [InlineData("Deutsch - AAC", false, false)]
    public void IsAudioDescriptionTrack_DetectsExpectedSignals(string trackName, bool isVisualImpaired, bool expected)
    {
        var track = CreateAudioTrack(1, trackName, isVisualImpaired);

        Assert.Equal(expected, AudioTrackClassifier.IsAudioDescriptionTrack(track));
    }

    [Fact]
    public void GetNormalAudioTracks_RemovesAudioDescriptionTracks_AndPreservesOrder()
    {
        var tracks = new[]
        {
            CreateAudioTrack(1, "Deutsch - E-AC-3"),
            CreateAudioTrack(2, "Deutsch Audiodeskription - AAC", isVisualImpaired: false),
            CreateAudioTrack(3, "English - AAC", language: "en")
        };

        var result = AudioTrackClassifier.GetNormalAudioTracks(tracks);

        Assert.Equal([1, 3], result.Select(track => track.TrackId).ToArray());
    }

    [Fact]
    public void GetPreferredNormalAudioTracks_FallsBackToAllAudioTracks_WhenHeuristicWouldRemoveEveryTrack()
    {
        var tracks = new[]
        {
            CreateAudioTrack(4, "Deutsch Audiodeskription - AAC"),
            CreateAudioTrack(5, "English Audiodeskription - AAC", language: "en")
        };

        var result = AudioTrackClassifier.GetPreferredNormalAudioTracks(tracks);

        Assert.Equal([4, 5], result.Select(track => track.TrackId).ToArray());
    }

    private static ContainerTrackMetadata CreateAudioTrack(
        int trackId,
        string trackName,
        bool isVisualImpaired = false,
        string language = "de")
    {
        return new ContainerTrackMetadata(
            TrackId: trackId,
            Type: "audio",
            CodecLabel: "AAC",
            Language: language,
            TrackName: trackName,
            VideoWidth: 0,
            IsVisualImpaired: isVisualImpaired,
            IsHearingImpaired: false,
            IsDefaultTrack: false);
    }
}
