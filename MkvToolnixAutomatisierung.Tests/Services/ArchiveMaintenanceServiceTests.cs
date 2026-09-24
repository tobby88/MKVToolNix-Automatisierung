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
    public void AnalyzeContainer_PreservesRangeTitlesAndSeasonDespiteSingleEpisodeMetadata()
    {
        var path = @"C:\Archiv\Serie\Season 2\Serie - S02E01-E02 - Doppelfolge.mkv";
        var result = ArchiveMaintenanceService.AnalyzeContainer(path,
            new ContainerMetadata("Eigener Doppeltitel", [CreateVideoTrack(0), CreateAudioTrack(1, "Deutsch - AAC")], []),
            new ArchiveExpectedEpisodeMetadata("Nur die erste Folge", "05", "01", "de"));
        Assert.Null(result.RenameOperation);
        Assert.Null(result.ContainerTitleEdit);
        Assert.Equal("Eigener Doppeltitel", result.ExpectedTitle);
    }

    [Fact]
    public void ManualRenameIncludesRegionalSidecarsAndArtworkButNotUnrelatedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "sidecar-regions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "Episode.mkv");
            foreach (var suffix in new[] { ".pt-BR.sdh.srt", ".zh-Hant-TW.ass", "-fanart.png", "-poster.webp", ".another-episode.srt", " Extra.nfo" })
                File.WriteAllText(Path.Combine(root, "Episode" + suffix), "test");
            var rename = ArchiveMaintenanceService.BuildManualRenameOperation(source, "New.mkv")!;
            Assert.Equal(4, rename.Sidecars.Count);
            Assert.DoesNotContain(rename.Sidecars, sidecar => sidecar.SourcePath.Contains("another") || sidecar.SourcePath.Contains("Extra"));
        }
        finally { Directory.Delete(root, true); }
    }

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
    public void BuildManualRenameOperation_DetectsCaseOnlyFileNameChange()
    {
        var operation = ArchiveMaintenanceService.BuildManualRenameOperation(
            @"C:\Archiv\Serie\Season 1\serie - s01e01 - pilot.mkv",
            "Serie - S01E01 - Pilot.mkv");

        Assert.NotNull(operation);
        Assert.Equal(
            @"C:\Archiv\Serie\Season 1\Serie - S01E01 - Pilot.mkv",
            operation!.TargetPath,
            StringComparer.Ordinal);
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
    public async Task ApplyAsync_RenamesCaseOnlyMediaAndSidecars()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "archive-case-rename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var mediaPath = Path.Combine(tempRoot, "serie - s01e01 - pilot.mkv");
            var nfoPath = Path.Combine(tempRoot, "serie - s01e01 - pilot.nfo");
            File.WriteAllText(mediaPath, "mkv");
            File.WriteAllText(nfoPath, "nfo");
            var operation = ArchiveMaintenanceService.BuildManualRenameOperation(
                mediaPath,
                "Serie - S01E01 - Pilot.mkv");
            var service = new ArchiveMaintenanceService(
                new MkvMergeProbeService(),
                new StubMkvToolNixLocator(),
                new MuxExecutionService(),
                nfoProviderIds: new EmbyNfoProviderIdService());

            var result = await service.ApplyAsync(new ArchiveMaintenanceApplyRequest(
                mediaPath,
                operation,
                ContainerTitleEdit: null,
                TrackHeaderEdits: [],
                ProviderIdEdit: null));

            var fileNames = Directory.EnumerateFiles(tempRoot)
                .Select(Path.GetFileName)
                .ToList();
            Assert.True(result.Success);
            Assert.Equal("Serie - S01E01 - Pilot.mkv", Path.GetFileName(result.CurrentFilePath), StringComparer.Ordinal);
            Assert.Contains("Serie - S01E01 - Pilot.mkv", fileNames);
            Assert.Contains("Serie - S01E01 - Pilot.nfo", fileNames);
            Assert.DoesNotContain("serie - s01e01 - pilot.mkv", fileNames);
            Assert.DoesNotContain("serie - s01e01 - pilot.nfo", fileNames);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyAsync_ReturnsFailure_WhenRenameTargetAlreadyExists()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "archive-rename-conflict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var mediaPath = Path.Combine(tempRoot, "Serie - S01E01 - Alt.mkv");
            var targetPath = Path.Combine(tempRoot, "Serie - S01E01 - Neu.mkv");
            File.WriteAllText(mediaPath, "source");
            File.WriteAllText(targetPath, "target");
            var operation = ArchiveMaintenanceService.BuildManualRenameOperation(
                mediaPath,
                "Serie - S01E01 - Neu.mkv");
            var service = new ArchiveMaintenanceService(
                new MkvMergeProbeService(),
                new StubMkvToolNixLocator(),
                new MuxExecutionService(),
                nfoProviderIds: new EmbyNfoProviderIdService());

            var result = await service.ApplyAsync(new ArchiveMaintenanceApplyRequest(
                mediaPath,
                operation,
                ContainerTitleEdit: null,
                TrackHeaderEdits: [],
                ProviderIdEdit: null));

            Assert.False(result.Success);
            Assert.Equal(mediaPath, result.CurrentFilePath);
            Assert.Contains("Umbenennen fehlgeschlagen", result.Message, StringComparison.Ordinal);
            Assert.True(File.Exists(mediaPath));
            Assert.Equal("target", File.ReadAllText(targetPath));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyAsync_DoesNotRename_WhenNfoUpdateFails()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "archive-nfo-before-rename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var mediaPath = Path.Combine(tempRoot, "Serie - S01E01 - Alt.mkv");
            var targetPath = Path.Combine(tempRoot, "Serie - S01E01 - Neu.mkv");
            File.WriteAllText(mediaPath, "mkv");
            var operation = ArchiveMaintenanceService.BuildManualRenameOperation(
                mediaPath,
                "Serie - S01E01 - Neu.mkv");
            var service = new ArchiveMaintenanceService(
                new MkvMergeProbeService(),
                new StubMkvToolNixLocator(),
                new MuxExecutionService(),
                nfoProviderIds: new EmbyNfoProviderIdService());

            var result = await service.ApplyAsync(new ArchiveMaintenanceApplyRequest(
                mediaPath,
                operation,
                ContainerTitleEdit: null,
                TrackHeaderEdits: [],
                ProviderIdEdit: new ArchiveProviderIdEditOperation(new EmbyProviderIds("123", null), RemoveImdbId: false)));

            Assert.False(result.Success);
            Assert.Equal(mediaPath, result.CurrentFilePath);
            Assert.Contains("NFO-Datei fehlt", result.Message, StringComparison.Ordinal);
            Assert.True(File.Exists(mediaPath));
            Assert.False(File.Exists(targetPath));
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

    [Fact]
    public async Task ApplyAsync_WritesNfoTitleChangesToExistingNfo()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "archive-nfo-title-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var mediaPath = Path.Combine(tempRoot, "Serie - S01E01 - Pilot.mkv");
            var nfoPath = Path.Combine(tempRoot, "Serie - S01E01 - Pilot.nfo");
            File.WriteAllText(mediaPath, "mkv");
            File.WriteAllText(nfoPath, "<episodedetails><title>Alt</title><sorttitle>Alt Sort</sorttitle><lockdata>false</lockdata><dateadded>2026-04-28</dateadded></episodedetails>");
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
                ProviderIdEdit: null,
                NfoTextEdit: new ArchiveNfoTextEditOperation(
                    "Alt",
                    "Neu",
                    "Alt Sort",
                    "Neu Sort",
                    CurrentTitleLocked: false,
                    ExpectedTitleLocked: true,
                    CurrentSortTitleLocked: false,
                    ExpectedSortTitleLocked: true)));

            Assert.True(result.Success);
            var nfoText = File.ReadAllText(nfoPath);
            Assert.Contains("<title>Neu</title>", nfoText, StringComparison.Ordinal);
            Assert.Contains("<sorttitle>Neu Sort</sorttitle>", nfoText, StringComparison.Ordinal);
            Assert.Contains("<lockedfields>Name|SortName</lockedfields>", nfoText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ResolveExpectedTitleFromNfoAndTvdb_UsesLockedNfoTitle()
    {
        var nfoResult = new EmbyNfoMetadataReadResult(
            @"C:\Archiv\Serie\Season 3\Serie - S03E02 - Lippmann wird vermißt.nfo",
            NfoExists: true,
            EmbyProviderIds.Empty,
            Title: "Lippmann wird vermißt",
            SortTitle: "Lippmann wird vermißt",
            IsTitleLocked: true,
            IsSortTitleLocked: true,
            WarningMessage: null);

        var result = ArchiveMaintenanceService.ResolveExpectedTitleFromNfoAndTvdb(
            nfoResult,
            "Lippmann wird vermisst");

        Assert.Equal("Lippmann wird vermißt", result);
    }

    [Fact]
    public void ResolveExpectedTitleFromNfoAndTvdb_UsesTvdbTitle_WhenNfoTitleIsNotLocked()
    {
        var nfoResult = new EmbyNfoMetadataReadResult(
            @"C:\Archiv\Serie\Season 3\Serie - S03E02 - Lippmann wird vermißt.nfo",
            NfoExists: true,
            EmbyProviderIds.Empty,
            Title: "Lippmann wird vermißt",
            SortTitle: "Lippmann wird vermißt",
            IsTitleLocked: false,
            IsSortTitleLocked: true,
            WarningMessage: null);

        var result = ArchiveMaintenanceService.ResolveExpectedTitleFromNfoAndTvdb(
            nfoResult,
            "Lippmann wird vermisst");

        Assert.Equal("Lippmann wird vermisst", result);
    }

    [Fact]
    public async Task ApplyAsync_RollsBackMediaAndMovedSidecars_WhenLaterSidecarIsLocked()
    {
        using var directory = new ArchiveTestDirectory();
        var media = directory.CreateFile("Alt.mkv", "media");
        var nfo = directory.CreateFile("Alt.nfo", "nfo");
        var image = directory.CreateFile("Alt.jpg", "image");
        var operation = ArchiveMaintenanceService.BuildManualRenameOperation(media, "Neu.mkv")!;
        operation = operation with { Sidecars = operation.Sidecars.OrderBy(sidecar => sidecar.SourcePath.EndsWith(".nfo", StringComparison.Ordinal)).ToList() };
        using var lockedNfo = File.Open(nfo, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = await CreateService().ApplyAsync(new ArchiveMaintenanceApplyRequest(media, operation, null, [], null));

        Assert.False(result.Success);
        Assert.Equal(media, result.CurrentFilePath);
        Assert.Equal("media", File.ReadAllText(media));
        Assert.Equal("image", File.ReadAllText(image));
        Assert.True(File.Exists(nfo));
        Assert.False(File.Exists(operation.TargetPath));
        Assert.False(File.Exists(Path.ChangeExtension(operation.TargetPath, ".jpg")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyAsync_PreflightsRenameCollision_BeforeChangingNfo(bool targetIsDirectory)
    {
        using var directory = new ArchiveTestDirectory();
        var media = directory.CreateFile("Alt.mkv", "media");
        const string originalNfo = "<episodedetails><tvdbid>123</tvdbid></episodedetails>";
        var nfo = directory.CreateFile("Alt.nfo", originalNfo);
        var operation = ArchiveMaintenanceService.BuildManualRenameOperation(media, "Neu.mkv")!;
        if (targetIsDirectory)
        {
            Directory.CreateDirectory(operation.TargetPath);
        }
        else
        {
            File.WriteAllText(operation.TargetPath, "existing");
        }

        var result = await CreateService().ApplyAsync(new ArchiveMaintenanceApplyRequest(media, operation, null, [],
            new ArchiveProviderIdEditOperation(new EmbyProviderIds("456", null), false)));

        Assert.False(result.Success);
        Assert.Equal(originalNfo, File.ReadAllText(nfo));
        Assert.Equal("media", File.ReadAllText(media));
    }

    [Fact]
    public async Task ApplyAsync_RejectsMissingPlannedSidecar_BeforeMovingMedia()
    {
        using var directory = new ArchiveTestDirectory();
        var media = directory.CreateFile("Alt.mkv", "media");
        var nfo = directory.CreateFile("Alt.nfo", "nfo");
        var operation = ArchiveMaintenanceService.BuildManualRenameOperation(media, "Neu.mkv")!;
        File.Delete(nfo);

        var result = await CreateService().ApplyAsync(new ArchiveMaintenanceApplyRequest(media, operation, null, [], null));

        Assert.False(result.Success);
        Assert.True(File.Exists(media));
        Assert.False(File.Exists(operation.TargetPath));
    }

    [Fact]
    public async Task ApplyAsync_AllowsExtensionOnlyCaseRename_WithoutMovingUnchangedSidecars()
    {
        using var directory = new ArchiveTestDirectory();
        var media = directory.CreateFile("Pilot.mkv", "media");
        var nfo = directory.CreateFile("Pilot.nfo", "nfo");
        var operation = ArchiveMaintenanceService.BuildManualRenameOperation(media, "Pilot.MKV")!;

        var result = await CreateService().ApplyAsync(new ArchiveMaintenanceApplyRequest(media, operation, null, [], null));

        Assert.True(result.Success);
        Assert.Equal("Pilot.MKV", Path.GetFileName(result.CurrentFilePath));
        Assert.Equal("nfo", File.ReadAllText(nfo));
    }

    [Fact]
    public async Task ApplyAsync_MovesSubtitleSidecarsToNewSeason_WithoutTakingOtherEpisodes()
    {
        using var directory = new ArchiveTestDirectory();
        var stem = Path.Combine("Serie", "Season 1", "Serie - S01E01 - Alt");
        var media = directory.CreateFile(stem + ".mkv", "media");
        var subtitle = directory.CreateFile(stem + ".de.forced.srt", "subtitle");
        var unrelated = directory.CreateFile(stem + ".extra.srt", "other");
        var operation = ArchiveMaintenanceService.BuildManualRenameOperation(media, "Serie - S02E01 - Neu.mkv")!;

        var result = await CreateService().ApplyAsync(new ArchiveMaintenanceApplyRequest(media, operation, null, [], null));

        Assert.True(result.Success);
        Assert.EndsWith(Path.Combine("Season 2", "Serie - S02E01 - Neu.mkv"), result.CurrentFilePath);
        Assert.False(File.Exists(subtitle));
        Assert.Equal("subtitle", File.ReadAllText(Path.ChangeExtension(result.CurrentFilePath, ".de.forced.srt")));
        Assert.Equal("other", File.ReadAllText(unrelated));
    }

    [Theory]
    [InlineData("..\\outside.mkv")]
    [InlineData("C:\\outside.mkv")]
    [InlineData("wrong.txt")]
    public void BuildManualRenameOperation_RejectsPathsAndNonMkvNames(string target)
    {
        Assert.Throws<ArgumentException>(() => ArchiveMaintenanceService.BuildManualRenameOperation(@"C:\Archiv\Pilot.mkv", target));
    }

    [Fact]
    public async Task ApplyAsync_AlreadyCanceled_DoesNotWriteNfoOrRename()
    {
        using var directory = new ArchiveTestDirectory();
        var media = directory.CreateFile("Alt.mkv", "media");
        const string xml = "<episodedetails><tvdbid>123</tvdbid></episodedetails>";
        var nfo = directory.CreateFile("Alt.nfo", xml);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService().ApplyAsync(
            new ArchiveMaintenanceApplyRequest(media, ArchiveMaintenanceService.BuildManualRenameOperation(media, "Neu.mkv"), null, [],
                new ArchiveProviderIdEditOperation(new EmbyProviderIds("456", null), false)), cancellationToken: cancellation.Token));

        Assert.True(File.Exists(media));
        Assert.Equal(xml, File.ReadAllText(nfo));
    }

    [Fact]
    public async Task ApplyAsync_CancellationAfterProviderEdit_StopsTextEditAndRename()
    {
        using var directory = new ArchiveTestDirectory();
        var media = directory.CreateFile("Alt.mkv", "media");
        var nfo = directory.CreateFile("Alt.nfo", "<episodedetails><tvdbid>123</tvdbid><title>Alt</title></episodedetails>");
        using var cancellation = new CancellationTokenSource();
        var request = new ArchiveMaintenanceApplyRequest(media, ArchiveMaintenanceService.BuildManualRenameOperation(media, "Neu.mkv"), null, [],
            new ArchiveProviderIdEditOperation(new EmbyProviderIds("456", null), false),
            new ArchiveNfoTextEditOperation("Alt", "Neu", null, null, false, true, false, false));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService().ApplyAsync(request,
            new CallbackProgress(_ => cancellation.Cancel()), cancellation.Token));

        Assert.True(File.Exists(media));
        Assert.Contains("456", File.ReadAllText(nfo));
        Assert.Contains("<title>Alt</title>", File.ReadAllText(nfo));
        Assert.False(File.Exists(request.RenameOperation!.TargetPath));
    }

    [Fact]
    public async Task ApplyAsync_RejectsNfoLockChangedSinceScan_BeforeProviderEdits()
    {
        using var directory = new ArchiveTestDirectory();
        var media = directory.CreateFile("Alt.mkv", "media");
        const string xml = "<episodedetails><title>Alt</title><lockedfields>Name</lockedfields></episodedetails>";
        var nfo = directory.CreateFile("Alt.nfo", xml);

        var result = await CreateService().ApplyAsync(new ArchiveMaintenanceApplyRequest(media, null, null, [],
            new ArchiveProviderIdEditOperation(new EmbyProviderIds("456", null), false),
            new ArchiveNfoTextEditOperation("Alt", "Neu", null, null, false, true, false, false)));

        Assert.False(result.Success);
        Assert.Contains("neu scannen", result.Message);
        Assert.Equal(xml, File.ReadAllText(nfo));
    }

    [Fact]
    public void AnalyzeContainer_UsesLockedNfoTitle_WithoutTvdbLookup()
    {
        using var directory = new ArchiveTestDirectory();
        var media = directory.CreateFile("Serie - S01E01 - Dateititel.mkv", "media");
        directory.CreateFile("Serie - S01E01 - Dateititel.nfo", "<episodedetails><title>Gesperrter Titel</title><lockedfields>Name</lockedfields></episodedetails>");

        var analysis = ArchiveMaintenanceService.AnalyzeContainer(media, new ContainerMetadata("Gesperrter Titel", [], []));

        Assert.Equal("Gesperrter Titel", analysis.ExpectedTitle);
        Assert.Null(analysis.ContainerTitleEdit);
        Assert.EndsWith("Gesperrter Titel.mkv", analysis.RenameOperation!.TargetPath);
    }

    [Fact]
    public void AnalyzeContainer_UnreadableNfo_IsNotReportedAsSafe()
    {
        using var directory = new ArchiveTestDirectory();
        var media = directory.CreateFile("Serie - S01E01 - Titel.mkv", "media");
        directory.CreateFile("Serie - S01E01 - Titel.nfo", "<broken");

        var analysis = ArchiveMaintenanceService.AnalyzeContainer(media, new ContainerMetadata("Titel", [], []));

        Assert.True(analysis.HasError);
        Assert.Contains("NFO", analysis.ErrorMessage);
    }

    private static ArchiveMaintenanceService CreateService() => new(new MkvMergeProbeService(), new StubMkvToolNixLocator(), new MuxExecutionService());

    private sealed class CallbackProgress(Action<string> callback) : IProgress<string>
    {
        public void Report(string value) => callback(value);
    }

    private sealed class ArchiveTestDirectory : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "archive-review-" + Guid.NewGuid().ToString("N"));

        public string CreateFile(string name, string content)
        {
            var path = Path.Combine(_path, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(_path))
            {
                Directory.Delete(_path, recursive: true);
            }
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
