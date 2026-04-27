using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class ArchiveMaintenanceServiceTests
{
    [Fact]
    public void AnalyzeContainer_DoesNotTreatMissingAdOrSubtitlesAsIssue()
    {
        var analysis = ArchiveMaintenanceService.AnalyzeContainer(
            @"C:\Archiv\Serie\Season 1\Serie - S01E01 - Pilot.mkv",
            new ContainerMetadata(
                "Pilot",
                [CreateVideoTrack(0), CreateAudioTrack(1, "Deutsch - AAC")],
                Attachments: []));

        Assert.False(analysis.RequiresRemux);
        Assert.Empty(analysis.Issues);
    }

    [Fact]
    public void AnalyzeContainer_ReportsDuplicateAudioDescriptionsAsRemuxIssue()
    {
        var analysis = ArchiveMaintenanceService.AnalyzeContainer(
            @"C:\Archiv\Serie\Season 1\Serie - S01E01 - Pilot.mkv",
            new ContainerMetadata(
                "Pilot",
                [
                    CreateVideoTrack(0),
                    CreateAudioTrack(1, "Deutsch - AAC"),
                    CreateAudioTrack(2, "Deutsch (sehbehinderte) - AAC", isVisualImpaired: true, isDefault: false),
                    CreateAudioTrack(3, "Deutsch Audiodeskription - AAC", isVisualImpaired: false, isDefault: false)
                ],
                Attachments: []));

        Assert.True(analysis.RequiresRemux);
        Assert.Contains(analysis.Issues, issue => issue.Message.Contains("Doppelte AD-Spuren", StringComparison.Ordinal));
    }

    [Fact]
    public void AnalyzeContainer_ReportsDuplicateSubtitlesAsRemuxIssue()
    {
        var analysis = ArchiveMaintenanceService.AnalyzeContainer(
            @"C:\Archiv\Serie\Season 1\Serie - S01E01 - Pilot.mkv",
            new ContainerMetadata(
                "Pilot",
                [
                    CreateVideoTrack(0),
                    CreateAudioTrack(1, "Deutsch - AAC"),
                    CreateSubtitleTrack(2, "Deutsch (hörgeschädigte) - SRT"),
                    CreateSubtitleTrack(3, "Deutsch SDH - SRT")
                ],
                Attachments: []));

        Assert.True(analysis.RequiresRemux);
        Assert.Contains(analysis.Issues, issue => issue.Message.Contains("Doppelte Untertitel", StringComparison.Ordinal));
    }

    [Fact]
    public void AnalyzeContainer_PlansSafeFilenameNormalization()
    {
        var analysis = ArchiveMaintenanceService.AnalyzeContainer(
            @"C:\Archiv\Serie\Season 1\Serie - S01E01 - Pilot: Start.mkv",
            new ContainerMetadata(
                "Pilot - Start",
                [CreateVideoTrack(0), CreateAudioTrack(1, "Deutsch - AAC")],
                Attachments: []));

        Assert.NotNull(analysis.RenameOperation);
        Assert.EndsWith("Serie - S01E01 - Pilot - Start.mkv", analysis.RenameOperation!.TargetPath, StringComparison.OrdinalIgnoreCase);
    }

    private static ContainerTrackMetadata CreateVideoTrack(int trackId)
    {
        return new ContainerTrackMetadata(
            TrackId: trackId,
            Type: "video",
            CodecLabel: "H.264",
            Language: "de",
            TrackName: "Deutsch - HD - H.264",
            VideoWidth: 1280,
            IsVisualImpaired: false,
            IsHearingImpaired: false,
            IsDefaultTrack: true);
    }

    private static ContainerTrackMetadata CreateAudioTrack(
        int trackId,
        string trackName,
        bool isVisualImpaired = false,
        bool isDefault = true)
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
            IsDefaultTrack: isDefault);
    }

    private static ContainerTrackMetadata CreateSubtitleTrack(int trackId, string trackName)
    {
        return new ContainerTrackMetadata(
            TrackId: trackId,
            Type: "subtitles",
            CodecLabel: "SRT",
            Language: "de",
            TrackName: trackName,
            VideoWidth: 0,
            IsVisualImpaired: false,
            IsHearingImpaired: false,
            IsDefaultTrack: false);
    }
}
