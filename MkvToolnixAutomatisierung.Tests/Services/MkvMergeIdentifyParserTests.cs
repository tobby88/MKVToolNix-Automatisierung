using System.Text.Json;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class MkvMergeIdentifyParserTests
{
    [Fact]
    public void CreatePrimaryVideoMetadata_HappyPath_ParsesTrackIdsCodecAndWidth()
    {
        using var doc = JsonDocument.Parse("""
            {
                "tracks": [
                    {
                        "id": 0, "type": "video", "codec": "V_MPEGH/ISO/HEVC",
                        "properties": { "pixel_dimensions": "1920x1080", "language_ietf": "deu" }
                    },
                    {
                        "id": 1, "type": "audio", "codec": "A_AC3",
                        "properties": { "language_ietf": "deu" }
                    }
                ]
            }
            """);

        var result = MkvMergeIdentifyParser.CreatePrimaryVideoMetadata(doc, "test.mkv");

        Assert.Equal(0, result.VideoTrackId);
        Assert.Equal(1, result.AudioTrackId);
        Assert.Equal(1920, result.VideoWidth);
        Assert.Equal("H.265", result.VideoCodecLabel);
        Assert.Equal("AC-3", result.AudioCodecLabel);
        Assert.Equal("deu", result.VideoLanguage);
        Assert.Equal("deu", result.AudioLanguage);
    }

    [Fact]
    public void CreatePrimaryVideoMetadata_NoVideoTrack_Throws()
    {
        using var doc = JsonDocument.Parse("""
            {
                "tracks": [
                    { "id": 1, "type": "audio", "codec": "A_AAC" }
                ]
            }
            """);

        Assert.Throws<InvalidOperationException>(
            () => MkvMergeIdentifyParser.CreatePrimaryVideoMetadata(doc, "test.mkv"));
    }

    [Fact]
    public void CreatePrimaryVideoMetadata_NoAudioTrack_Throws()
    {
        using var doc = JsonDocument.Parse("""
            {
                "tracks": [
                    { "id": 0, "type": "video", "codec": "V_MPEG4/ISO/AVC" }
                ]
            }
            """);

        Assert.Throws<InvalidOperationException>(
            () => MkvMergeIdentifyParser.CreatePrimaryVideoMetadata(doc, "test.mkv"));
    }

    [Fact]
    public void CreatePrimaryVideoMetadata_NoTracksElement_Throws()
    {
        using var doc = JsonDocument.Parse("""{ "container": {} }""");

        Assert.Throws<InvalidOperationException>(
            () => MkvMergeIdentifyParser.CreatePrimaryVideoMetadata(doc, "test.mkv"));
    }

    [Theory]
    [InlineData("V_MPEGH/ISO/HEVC", "H.265")]
    [InlineData("V_MPEG4/ISO/AVC", "H.264")]
    [InlineData("H.264", "H.264")]
    [InlineData("HEVC", "H.265")]
    public void CreatePrimaryVideoMetadata_VideoCodecNormalization(string rawCodec, string expectedLabel)
    {
        using var doc = JsonDocument.Parse($$"""
            {
                "tracks": [
                    { "id": 0, "type": "video", "codec": "{{rawCodec}}" },
                    { "id": 1, "type": "audio", "codec": "A_AAC" }
                ]
            }
            """);

        var result = MkvMergeIdentifyParser.CreatePrimaryVideoMetadata(doc, "test.mkv");

        Assert.Equal(expectedLabel, result.VideoCodecLabel);
    }

    [Theory]
    [InlineData("A_EAC3", "E-AC-3")]
    [InlineData("A_AC3", "AC-3")]
    [InlineData("A_AAC", "AAC")]
    [InlineData("A_OPUS", "Opus")]
    [InlineData("A_VORBIS", "Vorbis")]
    public void CreatePrimaryVideoMetadata_AudioCodecNormalization(string rawCodec, string expectedLabel)
    {
        using var doc = JsonDocument.Parse($$"""
            {
                "tracks": [
                    { "id": 0, "type": "video", "codec": "V_MPEG4/ISO/AVC" },
                    { "id": 1, "type": "audio", "codec": "{{rawCodec}}" }
                ]
            }
            """);

        var result = MkvMergeIdentifyParser.CreatePrimaryVideoMetadata(doc, "test.mkv");

        Assert.Equal(expectedLabel, result.AudioCodecLabel);
    }

    [Fact]
    public void CreatePrimaryVideoMetadata_ParsesWidthFromPixelDimensions()
    {
        using var doc = JsonDocument.Parse("""
            {
                "tracks": [
                    {
                        "id": 0, "type": "video", "codec": "V_MPEGH/ISO/HEVC",
                        "properties": { "pixel_dimensions": "3840x2160" }
                    },
                    { "id": 1, "type": "audio", "codec": "A_AAC" }
                ]
            }
            """);

        var result = MkvMergeIdentifyParser.CreatePrimaryVideoMetadata(doc, "test.mkv");

        Assert.Equal(3840, result.VideoWidth);
    }

    [Fact]
    public void CreatePrimaryVideoMetadata_LanguageFromIetfTag()
    {
        using var doc = JsonDocument.Parse("""
            {
                "tracks": [
                    {
                        "id": 0, "type": "video", "codec": "V_MPEGH/ISO/HEVC",
                        "properties": { "language_ietf": "eng" }
                    },
                    {
                        "id": 1, "type": "audio", "codec": "A_AC3",
                        "properties": { "language_ietf": "eng" }
                    }
                ]
            }
            """);

        var result = MkvMergeIdentifyParser.CreatePrimaryVideoMetadata(doc, "test.mkv");

        Assert.Equal("eng", result.VideoLanguage);
        Assert.Equal("eng", result.AudioLanguage);
    }

    [Fact]
    public void CreatePrimaryVideoMetadata_PrefersFirstNormalAudioTrack_OverVisualImpairedTrack()
    {
        using var doc = JsonDocument.Parse("""
            {
                "tracks": [
                    {
                        "id": 0, "type": "video", "codec": "V_MPEGH/ISO/HEVC",
                        "properties": { "pixel_dimensions": "1920x1080", "language_ietf": "deu" }
                    },
                    {
                        "id": 1, "type": "audio", "codec": "A_AAC",
                        "properties": { "language_ietf": "deu", "track_name": "Deutsch (sehbehinderte) - AAC", "flag_visual_impaired": true }
                    },
                    {
                        "id": 2, "type": "audio", "codec": "A_AC3",
                        "properties": { "language_ietf": "eng", "track_name": "English - AC-3" }
                    }
                ]
            }
            """);

        var result = MkvMergeIdentifyParser.CreatePrimaryVideoMetadata(doc, "test.mkv");

        Assert.Equal(2, result.AudioTrackId);
        Assert.Equal("AC-3", result.AudioCodecLabel);
        Assert.Equal("eng", result.AudioLanguage);
    }

    [Fact]
    public void CreatePrimaryVideoMetadata_PrefersFirstNormalAudioTrack_WhenAdIsDetectedOnlyByTrackName()
    {
        using var doc = JsonDocument.Parse("""
            {
                "tracks": [
                    {
                        "id": 0, "type": "video", "codec": "V_MPEG4/ISO/AVC",
                        "properties": { "pixel_dimensions": "1280x720", "language_ietf": "deu" }
                    },
                    {
                        "id": 1, "type": "audio", "codec": "A_AAC",
                        "properties": { "language_ietf": "deu", "track_name": "Deutsch Audiodeskription - AAC" }
                    },
                    {
                        "id": 2, "type": "audio", "codec": "A_EAC3",
                        "properties": { "language_ietf": "deu", "track_name": "Deutsch - E-AC-3" }
                    }
                ]
            }
            """);

        var result = MkvMergeIdentifyParser.CreatePrimaryVideoMetadata(doc, "test.mkv");

        Assert.Equal(2, result.AudioTrackId);
        Assert.Equal("E-AC-3", result.AudioCodecLabel);
        Assert.Equal("deu", result.AudioLanguage);
    }

    [Fact]
    public void CreateContainerMetadata_ParsesAllTrackTypesAndAttachments()
    {
        using var doc = JsonDocument.Parse("""
            {
                "container": {
                    "properties": {
                        "title": "Pilot"
                    }
                },
                "tracks": [
                    {
                        "id": 0, "type": "video", "codec": "V_MPEG4/ISO/AVC",
                        "properties": { "pixel_dimensions": "1280x720", "language_ietf": "deu" }
                    },
                    {
                        "id": 1, "type": "audio", "codec": "A_AC3",
                        "properties": { "language_ietf": "deu", "tag_duration": "00:04:47.722000000" }
                    },
                    {
                        "id": 2, "type": "subtitles", "codec": "S_TEXT/ASS",
                        "properties": { "language_ietf": "deu" }
                    }
                ],
                "attachments": [
                    { "id": 1, "file_name": "cover.jpg" },
                    { "id": 2, "file_name": "chapter.xml" }
                ]
            }
            """);

        var result = MkvMergeIdentifyParser.CreateContainerMetadata(doc, "test.mkv");

        Assert.Equal("Pilot", result.Title);
        Assert.Equal(3, result.Tracks.Count);
        Assert.Equal("video", result.Tracks[0].Type, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("H.264", result.Tracks[0].CodecLabel);
        Assert.Equal(1280, result.Tracks[0].VideoWidth);
        Assert.Equal("audio", result.Tracks[1].Type, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("AC-3", result.Tracks[1].CodecLabel);
        Assert.Equal(TimeSpan.FromSeconds(287.722), result.Tracks[1].Duration);
        Assert.Equal("subtitles", result.Tracks[2].Type, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("SSA", result.Tracks[2].CodecLabel);
        Assert.Equal(2, result.Attachments.Count);
        Assert.Equal("cover.jpg", result.Attachments[0].FileName);
        Assert.Equal("chapter.xml", result.Attachments[1].FileName);
    }

    [Theory]
    [InlineData("SubRip/SRT", "SRT")]
    [InlineData("S_TEXT/ASS", "SSA")]
    [InlineData("S_TEXT/WEBVTT", "WebVTT")]
    public void CreateContainerMetadata_SubtitleCodecNormalization(string rawCodec, string expectedLabel)
    {
        using var doc = JsonDocument.Parse($$"""
            {
                "tracks": [
                    { "id": 0, "type": "subtitles", "codec": "{{rawCodec}}" }
                ],
                "attachments": []
            }
            """);

        var result = MkvMergeIdentifyParser.CreateContainerMetadata(doc, "test.mkv");

        Assert.Equal(expectedLabel, result.Tracks[0].CodecLabel);
    }

    [Fact]
    public void CreateFirstAudioTrackMetadata_ReturnsFirstAudioTrack_IgnoresVideo()
    {
        using var doc = JsonDocument.Parse("""
            {
                "tracks": [
                    { "id": 0, "type": "video", "codec": "V_MPEG4/ISO/AVC" },
                    { "id": 1, "type": "audio", "codec": "A_AC3", "properties": { "language_ietf": "deu" } },
                    { "id": 2, "type": "audio", "codec": "A_AAC", "properties": { "language_ietf": "eng" } }
                ]
            }
            """);

        var result = MkvMergeIdentifyParser.CreateFirstAudioTrackMetadata(doc, "test.mkv");

        Assert.Equal(1, result.TrackId);
        Assert.Equal("AC-3", result.CodecLabel);
        Assert.Equal("deu", result.Language);
    }

    [Fact]
    public void CreateFirstAudioTrackMetadata_NoAudioTrack_Throws()
    {
        using var doc = JsonDocument.Parse("""
            {
                "tracks": [
                    { "id": 0, "type": "video", "codec": "V_MPEG4/ISO/AVC" }
                ]
            }
            """);

        Assert.Throws<InvalidOperationException>(
            () => MkvMergeIdentifyParser.CreateFirstAudioTrackMetadata(doc, "test.mkv"));
    }

    [Fact]
    public void CreateContainerMetadata_BooleanTrackFlags_ParsedCorrectly()
    {
        using var doc = JsonDocument.Parse("""
            {
                "tracks": [
                    {
                        "id": 0, "type": "audio", "codec": "A_AC3",
                        "properties": {
                            "language_ietf": "deu",
                            "flag_visual_impaired": true,
                            "flag_hearing_impaired": false,
                            "default_track": true
                        }
                    }
                ],
                "attachments": []
            }
            """);

        var result = MkvMergeIdentifyParser.CreateContainerMetadata(doc, "test.mkv");

        var track = Assert.Single(result.Tracks);
        Assert.True(track.IsVisualImpaired);
        Assert.False(track.IsHearingImpaired);
        Assert.True(track.IsDefaultTrack);
    }
}
