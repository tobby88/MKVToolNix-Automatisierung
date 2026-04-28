using System;
using System.IO;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Emby;
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

    [Fact]
    public void AnalyzeContainer_UsesResolvedTvdbTitle_WhenMetadataIsAvailable()
    {
        var analysis = ArchiveMaintenanceService.AnalyzeContainer(
            @"C:\Archiv\Serie\Season 1\Serie - S01E01 - Alter Titel.mkv",
            new ContainerMetadata(
                "Alter Titel",
                [CreateVideoTrack(0), CreateAudioTrack(1, "Deutsch - AAC")],
                Attachments: []),
            new ArchiveExpectedEpisodeMetadata(
                "TVDB-Titel",
                "01",
                "01",
                "de"));

        Assert.Equal("TVDB-Titel", analysis.ExpectedTitle);
        Assert.Equal("TVDB-Titel", analysis.ContainerTitleEdit?.ExpectedTitle);
        Assert.EndsWith("Serie - S01E01 - TVDB-Titel.mkv", analysis.RenameOperation?.TargetPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnalyzeContainer_NormalizesMetadataQuotes_WhenComparingFileName()
    {
        var analysis = ArchiveMaintenanceService.AnalyzeContainer(
            @"C:\Archiv\Der Alte\Season 6\Der Alte - S06E09 - 'Ich werde dich töten'.mkv",
            new ContainerMetadata(
                "\"Ich werde dich töten\"",
                [CreateVideoTrack(0), CreateAudioTrack(1, "Deutsch - AAC")],
                Attachments: []),
            new ArchiveExpectedEpisodeMetadata(
                "\"Ich werde dich töten\"",
                "06",
                "09",
                "de"));

        Assert.Null(analysis.RenameOperation);
    }

    [Fact]
    public void BuildManualRenameOperation_MovesEpisodeToMatchingSeasonFolder()
    {
        var operation = ArchiveMaintenanceService.BuildManualRenameOperation(
            @"C:\Archiv\Serie\Season 1\Serie - S01E01 - Pilot.mkv",
            "Serie - S02E01 - Pilot.mkv");

        Assert.NotNull(operation);
        Assert.Equal(
            @"C:\Archiv\Serie\Season 2\Serie - S02E01 - Pilot.mkv",
            operation!.TargetPath,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildManualRenameOperation_MovesSeasonZeroToSpecialsFolder()
    {
        var operation = ArchiveMaintenanceService.BuildManualRenameOperation(
            @"C:\Archiv\Serie\Season 1\Serie - S01E01 - Bonus.mkv",
            "Serie - S00E01 - Bonus.mkv");

        Assert.NotNull(operation);
        Assert.Equal(
            @"C:\Archiv\Serie\Specials\Serie - S00E01 - Bonus.mkv",
            operation!.TargetPath,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildManualRenameOperation_RenamesNfoAndThumbsSidecars()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "archive-sidecars-" + Guid.NewGuid().ToString("N"));
        var seasonDirectory = Path.Combine(tempRoot, "Serie", "Season 1");
        Directory.CreateDirectory(seasonDirectory);
        try
        {
            var sourcePath = Path.Combine(seasonDirectory, "Serie - S01E01 - Alt.mkv");
            var sourceBase = Path.Combine(seasonDirectory, "Serie - S01E01 - Alt");
            File.WriteAllText(sourcePath, "mkv");
            File.WriteAllText(sourceBase + ".nfo", "nfo");
            File.WriteAllText(sourceBase + "-thumb.jpg", "jpg");

            var operation = ArchiveMaintenanceService.BuildManualRenameOperation(
                sourcePath,
                "Serie - S02E01 - Neu.mkv");

            Assert.NotNull(operation);
            Assert.Contains(operation!.Sidecars, sidecar => sidecar.SourcePath.EndsWith(".nfo", StringComparison.OrdinalIgnoreCase)
                && sidecar.TargetPath.EndsWith(Path.Combine("Season 2", "Serie - S02E01 - Neu.nfo"), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(operation.Sidecars, sidecar => sidecar.SourcePath.EndsWith("-thumb.jpg", StringComparison.OrdinalIgnoreCase)
                && sidecar.TargetPath.EndsWith(Path.Combine("Season 2", "Serie - S02E01 - Neu-thumb.jpg"), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyAsync_WritesProviderIdChangesToExistingNfo()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "archive-provider-ids-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var mediaPath = Path.Combine(tempRoot, "Serie - S01E01 - Pilot.mkv");
            var nfoPath = Path.Combine(tempRoot, "Serie - S01E01 - Pilot.nfo");
            File.WriteAllText(mediaPath, "mkv");
            File.WriteAllText(nfoPath, "<episodedetails><uniqueid type=\"tvdb\">123</uniqueid><imdbid>tt1234567</imdbid></episodedetails>");
            var service = new ArchiveMaintenanceService(
                new MkvMergeProbeService(),
                new StubMkvToolNixLocator(),
                new MuxExecutionService(),
                nfoProviderIds: new EmbyNfoProviderIdService());

            var result = await service.ApplyAsync(new ArchiveMaintenanceApplyRequest(
                mediaPath,
                RenameOperation: null,
                ContainerTitleEdit: null,
                TrackHeaderEdits: [],
                ProviderIdEdit: new ArchiveProviderIdEditOperation(new EmbyProviderIds("456", "tt7654321"), RemoveImdbId: false)));

            Assert.True(result.Success);
            var nfoText = File.ReadAllText(nfoPath);
            Assert.Contains("456", nfoText, StringComparison.Ordinal);
            Assert.Contains("tt7654321", nfoText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
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

    private sealed class StubMkvToolNixLocator : IMkvToolNixLocator
    {
        public string FindMkvMergePath() => throw new NotSupportedException();

        public string FindMkvPropEditPath() => throw new NotSupportedException();
    }
}
