using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Modules;

public sealed class SeriesEpisodeMuxArgumentBuilderTests
{
    // ── ResolveOriginalFlag ──────────────────────────────────────────────────

    [Fact]
    public void ResolveOriginalFlag_NullOriginalLanguage_ReturnsYes()
    {
        Assert.Equal("yes", SeriesEpisodeMuxArgumentBuilder.ResolveOriginalFlag("de", null));
    }

    [Fact]
    public void ResolveOriginalFlag_EmptyOriginalLanguage_ReturnsYes()
    {
        Assert.Equal("yes", SeriesEpisodeMuxArgumentBuilder.ResolveOriginalFlag("de", ""));
    }

    [Theory]
    [InlineData("de", "de")]
    [InlineData("de", "deu")]
    [InlineData("de", "ger")]
    [InlineData("en", "en")]
    [InlineData("en", "eng")]
    public void ResolveOriginalFlag_TrackMatchesSeriesOriginalLanguage_ReturnsYes(
        string trackLanguage, string seriesOriginalLanguage)
    {
        Assert.Equal("yes", SeriesEpisodeMuxArgumentBuilder.ResolveOriginalFlag(trackLanguage, seriesOriginalLanguage));
    }

    [Theory]
    [InlineData("de", "swe")]    // Schwedische Produktion, deutsche Synchro → kein Original
    [InlineData("de", "sv")]     // Gleiche Sprache, alternatives Kürzel
    [InlineData("de", "en")]     // Englische Produktion, deutsche Synchro
    [InlineData("de", "eng")]    // Gleiche Sprache, alternatives Kürzel
    [InlineData("en", "deu")]    // Deutsche Produktion, englische Synchro
    [InlineData("de", "ja")]     // Japanische Produktion, deutsche Synchro
    [InlineData("de", "fr")]     // Französische Produktion, deutsche Synchro
    public void ResolveOriginalFlag_TrackDoesNotMatchSeriesOriginalLanguage_ReturnsNo(
        string trackLanguage, string seriesOriginalLanguage)
    {
        Assert.Equal("no", SeriesEpisodeMuxArgumentBuilder.ResolveOriginalFlag(trackLanguage, seriesOriginalLanguage));
    }

    [Fact]
    public void ResolveOriginalFlag_NullTrackLanguage_TreatedAsGerman()
    {
        // Null-Track gilt als Deutsch; Deutsche Originalsprache → yes
        Assert.Equal("yes", SeriesEpisodeMuxArgumentBuilder.ResolveOriginalFlag(null, "deu"));

        // Null-Track gilt als Deutsch; Schwedische Originalsprache → no
        Assert.Equal("no", SeriesEpisodeMuxArgumentBuilder.ResolveOriginalFlag(null, "swe"));
    }

    // ── Build (--original-flag im Argumentstrom) ──────────────────────────

    [Fact]
    public void Build_GermanSeriesOrigin_AllTracksGetOriginalFlagYes()
    {
        var plan = CreateMinimalPlan(originalLanguage: "deu", trackLanguageCode: "de");

        var arguments = plan.BuildArguments();

        // Das erste --original-flag im Strom muss "yes" sein (Videospur)
        var flagValues = ExtractOriginalFlagValues(arguments);
        Assert.All(flagValues, value => Assert.Equal("yes", value));
    }

    [Fact]
    public void Build_ForeignSeriesOrigin_GermanTracksGetOriginalFlagNo()
    {
        var plan = CreateMinimalPlan(originalLanguage: "swe", trackLanguageCode: "de");

        var arguments = plan.BuildArguments();

        var flagValues = ExtractOriginalFlagValues(arguments);
        Assert.All(flagValues, value => Assert.Equal("no", value));
    }

    [Fact]
    public void Build_UnknownSeriesOrigin_AllTracksGetOriginalFlagYes()
    {
        var plan = CreateMinimalPlan(originalLanguage: null, trackLanguageCode: "de");

        var arguments = plan.BuildArguments();

        var flagValues = ExtractOriginalFlagValues(arguments);
        Assert.All(flagValues, value => Assert.Equal("yes", value));
    }

    // ── Hilfsmethoden ───────────────────────────────────────────────────────

    private static SeriesEpisodeMuxPlan CreateMinimalPlan(string? originalLanguage, string trackLanguageCode)
    {
        return new SeriesEpisodeMuxPlan(
            mkvMergePath: @"C:\Tools\mkvmerge.exe",
            outputFilePath: @"C:\Temp\output.mkv",
            title: "Pilot",
            videoSources:
            [
                new VideoSourcePlan(@"C:\Temp\video.mkv", 0, "Track", IsDefaultTrack: true, LanguageCode: trackLanguageCode)
            ],
            audioSources:
            [
                new AudioSourcePlan(@"C:\Temp\video.mkv", 1, "Track", IsDefaultTrack: true, LanguageCode: trackLanguageCode)
            ],
            primarySourceAudioTrackIds: [1],
            primarySourceSubtitleTrackIds: [],
            primarySourceAttachmentIds: null,
            includePrimarySourceAttachments: false,
            attachmentSourcePath: null,
            attachmentSourceAttachmentIds: null,
            audioDescriptionFilePath: null,
            audioDescriptionTrackId: null,
            audioDescriptionTrackName: null,
            audioDescriptionLanguageCode: null,
            subtitleFiles: [],
            attachmentFilePaths: [],
            preservedAttachmentNames: [],
            usageComparison: ArchiveUsageComparison.Empty,
            workingCopy: null,
            originalLanguage: originalLanguage);
    }

    private static IReadOnlyList<string> ExtractOriginalFlagValues(IReadOnlyList<string> arguments)
    {
        var values = new List<string>();
        for (var i = 0; i < arguments.Count - 1; i++)
        {
            if (arguments[i] == "--original-flag")
            {
                // Format: "trackId:yes" oder "trackId:no" → nur den Wert-Teil extrahieren
                var parts = arguments[i + 1].Split(':', 2);
                if (parts.Length == 2)
                {
                    values.Add(parts[1]);
                }
            }
        }

        return values;
    }
}
