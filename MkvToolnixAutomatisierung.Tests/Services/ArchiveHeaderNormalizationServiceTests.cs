using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class ArchiveHeaderNormalizationServiceTests
{
    [Fact]
    public void BuildForArchiveFile_DoesNotReportMissingSupplementTracks()
    {
        var container = new ContainerMetadata(
            Title: "Beispielserie - S01E01 - Pilot",
            Tracks:
            [
                CreateVideoTrack(0, isDefault: true),
                CreateAudioTrack(1, "Deutsch - AAC", isDefault: true)
            ],
            Attachments: []);

        var result = ArchiveHeaderNormalizationService.BuildForArchiveFile(
            "Beispielserie - S01E01 - Pilot.mkv",
            container,
            "Beispielserie - S01E01 - Pilot",
            originalLanguage: null);

        Assert.False(result.HasChanges);
    }

    [Fact]
    public void BuildForArchiveFile_PlansHeaderCorrections_ForExistingTracksOnly()
    {
        var container = new ContainerMetadata(
            Title: "Alter Titel",
            Tracks:
            [
                CreateVideoTrack(0, trackName: "HD - H.264", isDefault: false),
                CreateAudioTrack(1, "Deutsch Audiodeskription - AAC", isVisualImpaired: false, isDefault: true),
                CreateSubtitleTrack(2, "Deutsch SDH - SRT", isHearingImpaired: false, isDefault: true)
            ],
            Attachments: []);

        var result = ArchiveHeaderNormalizationService.BuildForArchiveFile(
            "Beispielserie - S01E01 - Pilot.mkv",
            container,
            "Beispielserie - S01E01 - Pilot",
            originalLanguage: "de");

        Assert.NotNull(result.ContainerTitleEdit);
        Assert.Contains(result.TrackHeaderEdits, edit => edit.ValueEdits?.Any(value => value.PropertyName == "flag-default" && value.ExpectedMkvPropEditValue == "1") == true);
        Assert.Contains(result.TrackHeaderEdits, edit => edit.ValueEdits?.Any(value => value.PropertyName == "flag-visual-impaired" && value.ExpectedMkvPropEditValue == "1") == true);
        Assert.Contains(result.TrackHeaderEdits, edit => edit.ValueEdits?.Any(value => value.PropertyName == "flag-hearing-impaired" && value.ExpectedMkvPropEditValue == "1") == true);
    }

    [Fact]
    public void BuildForArchiveFile_DerKommissarUndDasMeer_PlansOriginalFlagsOff()
    {
        var container = new ContainerMetadata(
            Title: "Pilot",
            Tracks:
            [
                CreateVideoTrack(0, isOriginalLanguage: true),
                CreateAudioTrack(1, "Deutsch - AAC", isOriginalLanguage: true)
            ],
            Attachments: []);

        var result = ArchiveHeaderNormalizationService.BuildForArchiveFile(
            @"Z:\Videos\Serien\Der Kommissar und das Meer\Season 1\Der Kommissar und das Meer - S01E01 - Pilot.mkv",
            container,
            "Pilot",
            originalLanguage: "de");

        var originalFlagEdits = result.TrackHeaderEdits
            .SelectMany(edit => edit.ValueEdits ?? [])
            .Where(value => string.Equals(value.PropertyName, "flag-original", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, originalFlagEdits.Count);
        Assert.All(originalFlagEdits, edit => Assert.Equal("0", edit.ExpectedMkvPropEditValue));
    }

    [Fact]
    public void BuildForArchiveFile_DerKommissarUndDerSee_KeepsRegularOriginalFlags()
    {
        var container = new ContainerMetadata(
            Title: "Pilot",
            Tracks:
            [
                CreateVideoTrack(0, isOriginalLanguage: true),
                CreateAudioTrack(1, "Deutsch - AAC", isOriginalLanguage: true)
            ],
            Attachments: []);

        var result = ArchiveHeaderNormalizationService.BuildForArchiveFile(
            @"Z:\Videos\Serien\Der Kommissar und der See\Season 1\Der Kommissar und der See - S01E01 - Pilot.mkv",
            container,
            "Pilot",
            originalLanguage: "de");

        Assert.DoesNotContain(result.TrackHeaderEdits, edit =>
            edit.ValueEdits?.Any(value => string.Equals(value.PropertyName, "flag-original", StringComparison.Ordinal)) == true);
    }

    [Theory]
    [InlineData("Deutsch SDH", false, true)]
    [InlineData("Deutsch hearing impaired", false, true)]
    [InlineData("Deutsch - SRT", true, true)]
    [InlineData("Deutsch - SRT", false, false)]
    public void IsHearingImpairedSubtitleTrack_DetectsExpectedSignals(string trackName, bool isHearingImpaired, bool expected)
    {
        var track = CreateSubtitleTrack(2, trackName, isHearingImpaired);

        Assert.Equal(expected, ArchiveHeaderNormalizationService.IsHearingImpairedSubtitleTrack(track));
    }

    private static ContainerTrackMetadata CreateVideoTrack(
        int trackId,
        string trackName = "Deutsch - HD - H.264",
        bool isDefault = true,
        bool isOriginalLanguage = false)
    {
        return new ContainerTrackMetadata(
            TrackId: trackId,
            Type: "video",
            CodecLabel: "H.264",
            Language: "de",
            TrackName: trackName,
            VideoWidth: 1280,
            IsVisualImpaired: false,
            IsHearingImpaired: false,
            IsDefaultTrack: isDefault,
            IsOriginalLanguage: isOriginalLanguage);
    }

    private static ContainerTrackMetadata CreateAudioTrack(
        int trackId,
        string trackName,
        bool isVisualImpaired = false,
        bool isDefault = true,
        bool isOriginalLanguage = false)
    {
        return new ContainerTrackMetadata(
            TrackId: trackId,
            Type: "audio",
            CodecLabel: "AAC",
            Language: "de",
            TrackName: trackName,
            VideoWidth: 0,
            IsVisualImpaired: isVisualImpaired,
            IsHearingImpaired: false,
            IsDefaultTrack: isDefault,
            IsOriginalLanguage: isOriginalLanguage);
    }

    private static ContainerTrackMetadata CreateSubtitleTrack(
        int trackId,
        string trackName,
        bool isHearingImpaired,
        bool isDefault = false)
    {
        return new ContainerTrackMetadata(
            TrackId: trackId,
            Type: "subtitles",
            CodecLabel: "SRT",
            Language: "de",
            TrackName: trackName,
            VideoWidth: 0,
            IsVisualImpaired: false,
            IsHearingImpaired: isHearingImpaired,
            IsDefaultTrack: isDefault);
    }
}
