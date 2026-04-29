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

    [Fact]
    public void Build_DerKommissarUndDasMeer_SuppressesOriginalFlags()
    {
        var plan = CreateMinimalPlan(
            originalLanguage: "deu",
            trackLanguageCode: "de",
            outputFilePath: @"Z:\Videos\Serien\Der Kommissar und das Meer\Season 1\Der Kommissar und das Meer - S01E01 - Pilot.mkv");

        var arguments = plan.BuildArguments();

        var flagValues = ExtractOriginalFlagValues(arguments);
        Assert.All(flagValues, value => Assert.Equal("no", value));
    }

    [Fact]
    public void Build_DerKommissarUndDerSee_UsesRegularOriginalLanguageRule()
    {
        var plan = CreateMinimalPlan(
            originalLanguage: "deu",
            trackLanguageCode: "de",
            outputFilePath: @"Z:\Videos\Serien\Der Kommissar und der See\Season 1\Der Kommissar und der See - S01E01 - Pilot.mkv");

        var arguments = plan.BuildArguments();

        var flagValues = ExtractOriginalFlagValues(arguments);
        Assert.All(flagValues, value => Assert.Equal("yes", value));
    }

    [Fact]
    public void Build_PrimarySourceSubtitleIdsEmpty_DisablesImplicitEmbeddedSubtitles()
    {
        var plan = CreateMinimalPlan(originalLanguage: "deu", trackLanguageCode: "de");

        var arguments = plan.BuildArguments();

        AssertContainsSequence(arguments, "--audio-tracks", "1", "--no-subtitles", "--no-attachments", "--video-tracks", "0");
    }

    [Fact]
    public void Build_AdditionalVideo_UsesConfiguredDefaultTrackFlag()
    {
        var plan = new SeriesEpisodeMuxPlan(
            mkvMergePath: @"C:\Tools\mkvmerge.exe",
            outputFilePath: @"C:\Temp\output.mkv",
            title: "Pilot",
            videoSources:
            [
                new VideoSourcePlan(@"C:\Temp\primary.mkv", 0, "Deutsch - FHD - H.264", IsDefaultTrack: true),
                new VideoSourcePlan(@"C:\Temp\additional.mkv", 2, "Plattdüütsch - FHD - H.264", IsDefaultTrack: true, LanguageCode: "nds")
            ],
            audioSources:
            [
                new AudioSourcePlan(@"C:\Temp\primary.mkv", 1, "Deutsch - AC-3", IsDefaultTrack: true)
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
            workingCopy: null);

        var arguments = plan.BuildArguments();

        AssertContainsSequence(arguments, "--video-tracks", "2", "--language", "2:nds");
        AssertContainsSequence(arguments, "--track-name", "2:Plattdüütsch - FHD - H.264", "--default-track-flag", "2:yes");
    }

    // ── Hilfsmethoden ───────────────────────────────────────────────────────

    private static SeriesEpisodeMuxPlan CreateMinimalPlan(
        string? originalLanguage,
        string trackLanguageCode,
        string outputFilePath = @"C:\Temp\output.mkv")
    {
        return new SeriesEpisodeMuxPlan(
            mkvMergePath: @"C:\Tools\mkvmerge.exe",
            outputFilePath: outputFilePath,
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
}
