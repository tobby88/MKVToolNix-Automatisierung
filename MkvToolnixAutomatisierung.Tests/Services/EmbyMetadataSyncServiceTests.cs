using System.IO;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Emby;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class EmbyMetadataSyncServiceTests
{
    [Fact]
    public void ReportProgress_PreservesDecisionsAndMovesBetweenSiblingFoldersWhenReopened()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-report-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var mediaPath = Path.Combine(directory, "Bonus.mkv");
            var reportPath = Path.Combine(directory, "run.json");
            WriteReport(reportPath, mediaPath);
            var service = new EmbyMetadataSyncService(new ThrowingEmbyClient(), new EmbyNfoProviderIdService());
            var review = new BatchOutputEmbyReview
            {
                TvdbId = "",
                ImdbId = "",
                TvdbUnavailable = true,
                ImdbUnavailable = true,
                TvdbManuallyReviewed = true,
                ImdbManuallyReviewed = true
            };
            var reviews = new Dictionary<string, BatchOutputEmbyReview>(StringComparer.OrdinalIgnoreCase)
            {
                [mediaPath] = review
            };

            // Auch ein Lauf ohne erfolgreichen Refresh muss die bewussten Entscheidungen behalten.
            var partial = service.MarkOutputReportsDone([reportPath], [], reviews);
            Assert.Empty(partial.FailedReports);
            var partialPath = Assert.Single(partial.MovedReports).TargetPath;
            Assert.Equal(Path.Combine(directory, "partial", "run.json"), partialPath);
            var imported = Assert.Single(service.LoadNewOutputReport(partialPath));
            Assert.Equal(review, imported.Review);
            Assert.Equal("100", imported.ProviderIds.TvdbId); // Ursprüngliche Mux-Zuordnung bleibt nachvollziehbar.

            var done = service.MarkOutputReportsDone([partialPath], [mediaPath], reviews);
            var donePath = Assert.Single(done.MovedReports).TargetPath;
            Assert.Equal(Path.Combine(directory, "done", "run.json"), donePath);
            var finished = BatchOutputMetadataReportJson.Deserialize(File.ReadAllText(donePath))!;
            Assert.True(finished.Items[0].EmbySyncDone);
            Assert.NotNull(finished.Items[0].EmbySyncDoneAt);
            Assert.NotNull(finished.EmbySyncCompletedAt);
            Assert.Empty(service.MarkOutputReportsDone([donePath], [mediaPath], reviews).MovedReports);

            // Eine zurückgenommene Entscheidung darf keinen veralteten Abschluss hinterlassen.
            reviews[mediaPath] = review with { TvdbUnavailable = false, TvdbManuallyReviewed = false };
            var reopened = service.MarkOutputReportsDone([donePath], [], reviews);
            Assert.Equal(partialPath, Assert.Single(reopened.MovedReports).TargetPath);
            var pending = BatchOutputMetadataReportJson.Deserialize(File.ReadAllText(partialPath))!;
            Assert.False(pending.Items[0].EmbySyncDone);
            Assert.Null(pending.Items[0].EmbySyncDoneAt);
            Assert.Null(pending.EmbySyncCompletedAt);
            Assert.False(pending.Items[0].EmbyReview!.TvdbUnavailable);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReportProgress_DoesNotOverwriteOtherReportsOrModifyUnselectedEntries()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-report-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var first = Path.Combine(directory, "First.mkv");
            var second = Path.Combine(directory, "Second.mkv");
            var reportPath = Path.Combine(directory, "run.json");
            WriteReport(reportPath, first, second);
            var partialDirectory = Path.Combine(directory, "partial");
            Directory.CreateDirectory(partialDirectory);
            var collisionPath = Path.Combine(partialDirectory, "run.json");
            File.WriteAllText(collisionPath, "other report");
            var service = new EmbyMetadataSyncService(new ThrowingEmbyClient(), new EmbyNfoProviderIdService());

            var result = service.MarkOutputReportsDone([reportPath], [first]);

            Assert.Empty(result.FailedReports);
            var moved = Assert.Single(result.MovedReports);
            Assert.NotEqual(collisionPath, moved.TargetPath);
            Assert.Equal("other report", File.ReadAllText(collisionPath));
            var report = BatchOutputMetadataReportJson.Deserialize(File.ReadAllText(moved.TargetPath))!;
            Assert.True(report.Items[0].EmbySyncDone);
            Assert.Null(report.Items[1].EmbySyncDone);
            Assert.Null(report.Items[1].EmbyReview);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadNewOutputReport_RejectsLegacyTextLists()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-report-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var reportPath = Path.Combine(directory, "Neu erzeugte Ausgabedateien.txt");
            File.WriteAllLines(reportPath,
            [
                "Neu erzeugte Ausgabedateien",
                "Erstellt am: 13.04.2026 10:00:00",
                @"Z:\Videos\Serien\Serie\Season 01\Serie - S01E01 - Pilot.mkv",
                @"Z:\Videos\Serien\Serie\Season 01\Serie - S01E01 - Pilot.mkv",
                @"Z:\Videos\Serien\Serie\Season 01\Serie - S01E01 - Pilot.nfo"
            ]);

            var service = new EmbyMetadataSyncService(new ThrowingEmbyClient(), new EmbyNfoProviderIdService());

            var ex = Assert.Throws<InvalidDataException>(() => service.LoadNewOutputReport(reportPath));
            Assert.Contains("JSON", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadNewOutputReport_ReadsStructuredMetadataReportProviderIds()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-report-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var mediaPath = Path.Combine(directory, "Serie - S01E02 - Pilot.mkv");
            var reportPath = Path.Combine(directory, "Neu erzeugte Ausgabedateien.metadata.json");
            File.WriteAllText(
                reportPath,
                BatchOutputMetadataReportJson.Serialize(new BatchOutputMetadataReport
                {
                    CreatedAt = DateTimeOffset.Now,
                    SourceDirectory = directory,
                    OutputDirectory = directory,
                    Items =
                    [
                        new BatchOutputMetadataEntry
                        {
                            OutputPath = mediaPath,
                            TvdbEpisodeId = "100",
                            ProviderIds = new BatchOutputProviderIds
                            {
                                Tvdb = "100",
                                Imdb = "tt1234567"
                            },
                            Tvdb = new BatchOutputTvdbMetadata
                            {
                                SeriesId = 42,
                                SeriesName = "Serie",
                                EpisodeId = 100
                            }
                        }
                    ]
                }));

            var service = new EmbyMetadataSyncService(new ThrowingEmbyClient(), new EmbyNfoProviderIdService());

            var entries = service.LoadNewOutputReport(reportPath);

            var entry = Assert.Single(entries);
            Assert.Equal(mediaPath, entry.MediaFilePath);
            Assert.Equal("100", entry.ProviderIds.TvdbId);
            Assert.Equal("tt1234567", entry.ProviderIds.ImdbId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MarkOutputReportsDone_MarksItemsAndMovesCompletedReportToDoneDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-report-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var firstMediaPath = Path.Combine(directory, "Serie - S01E01 - Pilot.mkv");
            var secondMediaPath = Path.Combine(directory, "Serie - S01E02 - Finale.mkv");
            var reportPath = Path.Combine(directory, "Neu erzeugte Ausgabedateien.metadata.json");
            WriteReport(reportPath, firstMediaPath, secondMediaPath);
            var service = new EmbyMetadataSyncService(new ThrowingEmbyClient(), new EmbyNfoProviderIdService());

            var partialResult = service.MarkOutputReportsDone([reportPath], [firstMediaPath]);

            Assert.Empty(partialResult.UpdatedReportPaths);
            var partialPath = Assert.Single(partialResult.MovedReports).TargetPath;
            Assert.Equal(Path.Combine(directory, "partial", Path.GetFileName(reportPath)), partialPath);
            var partialReport = BatchOutputMetadataReportJson.Deserialize(File.ReadAllText(partialPath))!;
            Assert.True(partialReport.Items[0].EmbySyncDone);
            Assert.Null(partialReport.Items[1].EmbySyncDone);
            Assert.Null(partialReport.EmbySyncCompletedAt);

            var completedResult = service.MarkOutputReportsDone([partialPath], [secondMediaPath]);

            var movedReport = Assert.Single(completedResult.MovedReports);
            Assert.Equal(partialPath, movedReport.SourcePath);
            Assert.StartsWith(Path.Combine(directory, "done"), movedReport.TargetPath, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(reportPath));
            Assert.True(File.Exists(movedReport.TargetPath));
            var completedReport = BatchOutputMetadataReportJson.Deserialize(File.ReadAllText(movedReport.TargetPath))!;
            Assert.All(completedReport.Items, item => Assert.True(item.EmbySyncDone));
            Assert.NotNull(completedReport.EmbySyncCompletedAt);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MarkOutputReportsDone_MovesAlreadyCompletedReport_WhenPreviousMoveFailed()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-report-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var firstMediaPath = Path.Combine(directory, "Serie - S01E01 - Pilot.mkv");
            var secondMediaPath = Path.Combine(directory, "Serie - S01E02 - Finale.mkv");
            var reportPath = Path.Combine(directory, "Neu erzeugte Ausgabedateien.metadata.json");
            WriteReport(reportPath, firstMediaPath, secondMediaPath);
            var report = BatchOutputMetadataReportJson.Deserialize(File.ReadAllText(reportPath))!;
            foreach (var item in report.Items)
            {
                item.EmbySyncDone = true;
                item.EmbySyncDoneAt = DateTimeOffset.Now.AddMinutes(-5);
            }

            report.EmbySyncCompletedAt = DateTimeOffset.Now.AddMinutes(-5);
            File.WriteAllText(reportPath, BatchOutputMetadataReportJson.Serialize(report));
            var service = new EmbyMetadataSyncService(new ThrowingEmbyClient(), new EmbyNfoProviderIdService());

            var result = service.MarkOutputReportsDone([reportPath], [firstMediaPath]);

            var movedReport = Assert.Single(result.MovedReports);
            Assert.Equal(reportPath, movedReport.SourcePath);
            Assert.False(File.Exists(reportPath));
            Assert.True(File.Exists(movedReport.TargetPath));
            Assert.Empty(result.UpdatedReportPaths);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TriggerSeriesLibraryScanAsync_UsesArchiveRootItem_WhenEmbyFindsIt()
    {
        var client = new RecordingEmbyClient
        {
            Libraries =
            [
                new EmbyLibraryFolder(
                    "library-1",
                    "Serien",
                    [@"Z:\Videos\Serien"],
                    RefreshProgress: null,
                    RefreshStatus: null)
            ]
        };
        var service = new EmbyMetadataSyncService(client, new EmbyNfoProviderIdService());

        var result = await service.TriggerSeriesLibraryScanAsync(
            new AppEmbySettings { ServerUrl = "http://t-emby:8096", ApiKey = "token" },
            @"Z:\Videos\Serien");

        Assert.False(result.UsedGlobalLibraryScan);
        Assert.Equal("library-1", client.LastItemFileScanItemId);
        Assert.Equal(0, client.TriggerLibraryScanCallCount);
        Assert.Equal("library-1", result.Library?.Id);
        Assert.Equal(@"Z:\Videos\Serien", result.MatchedLibraryPath);
    }

    private static void WriteReport(string reportPath, params string[] mediaPaths)
    {
        File.WriteAllText(
            reportPath,
            BatchOutputMetadataReportJson.Serialize(new BatchOutputMetadataReport
            {
                CreatedAt = DateTimeOffset.Now,
                SourceDirectory = Path.GetDirectoryName(reportPath)!,
                OutputDirectory = Path.GetDirectoryName(reportPath)!,
                Items = mediaPaths
                    .Select(path => new BatchOutputMetadataEntry
                    {
                        OutputPath = path,
                        TvdbEpisodeId = "100",
                        ProviderIds = new BatchOutputProviderIds
                        {
                            Tvdb = "100",
                            Imdb = "tt1234567"
                        }
                    })
                    .ToList()
            }));
    }

    [Fact]
    public async Task TriggerSeriesLibraryScanAsync_RefusesGlobalScan_WhenArchiveRootIsNotFound()
    {
        var client = new RecordingEmbyClient();
        var service = new EmbyMetadataSyncService(client, new EmbyNfoProviderIdService());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TriggerSeriesLibraryScanAsync(
            new AppEmbySettings { ServerUrl = "http://t-emby:8096", ApiKey = "token" },
            @"Z:\Videos\Serien"));

        Assert.Equal(0, client.TriggerLibraryScanCallCount);
        Assert.Null(client.LastItemFileScanItemId);
    }

    [Theory]
    [InlineData("series", "/srv/TV", true)]
    [InlineData("wrong", "/srv/TV", false)]
    [InlineData("series", "/srv/tv", false)]
    [InlineData("series", "/srv/TV-other", false)]
    public async Task ExplicitMappingDoesNotGuessWhenConfigurationIsWrong(string libraryId, string serverRoot, bool expected)
    {
        var path = "/srv/TV/Show/episode.mkv";
        var client = new RecordingEmbyClient
        {
            Libraries = [new("series", "Serien", ["/srv/TV"], null, null)],
            ItemByPath = new Dictionary<string, EmbyItem> { [path] = new("episode", "Episode", path, new Dictionary<string, string>()) }
        };
        var service = new EmbyMetadataSyncService(client, new EmbyNfoProviderIdService());
        var settings = new AppEmbySettings { SeriesLibraryId = libraryId, ServerArchiveRootPath = serverRoot };
        var match = await service.FindSeriesLibraryAsync(settings, @"Z:\Videos\Serien");
        Assert.Equal(expected, match is not null);
        var item = await service.FindItemByPathAsync(settings, @"Z:\Videos\Serien\Show\episode.mkv", @"Z:\Videos\Serien", match);
        Assert.Equal(expected, item is not null);
        if (expected)
        {
            await service.TriggerSeriesLibraryScanAsync(settings, @"Z:\Videos\Serien");
            Assert.Equal("series", client.LastItemFileScanItemId);
        }
        else await Assert.ThrowsAsync<InvalidOperationException>(() => service.TriggerSeriesLibraryScanAsync(settings, @"Z:\Videos\Serien"));
        Assert.Equal(0, client.TriggerLibraryScanCallCount);
        Assert.Equal(serverRoot, settings.Clone().ServerArchiveRootPath);
        Assert.Equal(libraryId, settings.Clone().SeriesLibraryId);
    }

    [Fact]
    public async Task FindSeriesLibraryAsync_MatchesNestedConfiguredArchivePath_ToLibraryLocation()
    {
        var client = new RecordingEmbyClient
        {
            Libraries =
            [
                new EmbyLibraryFolder(
                    "library-1",
                    "Serien",
                    [@"Z:\Videos\Serien"],
                    RefreshProgress: null,
                    RefreshStatus: null)
            ]
        };
        var service = new EmbyMetadataSyncService(client, new EmbyNfoProviderIdService());

        var result = await service.FindSeriesLibraryAsync(
            new AppEmbySettings { ServerUrl = "http://t-emby:8096", ApiKey = "token" },
            @"Z:\Videos\Serien\Unterordner");

        Assert.NotNull(result);
        Assert.Equal("library-1", result.Library.Id);
        Assert.Equal(@"Z:\Videos\Serien", result.MatchedLocation);
    }

    [Fact]
    public async Task FindSeriesLibraryAsync_PrefersClosestParentLibrary_WhenMultipleParentsMatch()
    {
        var client = new RecordingEmbyClient
        {
            Libraries =
            [
                new EmbyLibraryFolder(
                    "library-broad",
                    "Videos",
                    [@"Z:\Videos"],
                    RefreshProgress: null,
                    RefreshStatus: null),
                new EmbyLibraryFolder(
                    "library-series",
                    "Serien",
                    [@"Z:\Videos\Serien"],
                    RefreshProgress: null,
                    RefreshStatus: null)
            ]
        };
        var service = new EmbyMetadataSyncService(client, new EmbyNfoProviderIdService());

        var result = await service.FindSeriesLibraryAsync(
            new AppEmbySettings { ServerUrl = "http://t-emby:8096", ApiKey = "token" },
            @"Z:\Videos\Serien\Kids");

        Assert.NotNull(result);
        Assert.Equal("library-series", result.Library.Id);
        Assert.Equal(@"Z:\Videos\Serien", result.MatchedLocation);
    }

    [Fact]
    public async Task FindSeriesLibraryAsync_MatchesLinuxLibraryBySharedTrailingSegments()
    {
        var client = new RecordingEmbyClient
        {
            Libraries =
            [
                new EmbyLibraryFolder(
                    "library-1",
                    "Serien",
                    ["/mnt/raid/Videos/Serien"],
                    RefreshProgress: null,
                    RefreshStatus: null)
            ]
        };
        var service = new EmbyMetadataSyncService(client, new EmbyNfoProviderIdService());

        var result = await service.FindSeriesLibraryAsync(
            new AppEmbySettings { ServerUrl = "http://t-emby:8096", ApiKey = "token" },
            @"Z:\Videos\Serien");

        Assert.NotNull(result);
        Assert.Equal("library-1", result.Library.Id);
        Assert.Equal("/mnt/raid/Videos/Serien", result.MatchedLocation);
    }

    [Fact]
    public async Task FindSeriesLibraryAsync_MatchesLinuxParentLibrary_WhenArchiveRootIsNestedBelowIt()
    {
        var client = new RecordingEmbyClient
        {
            Libraries =
            [
                new EmbyLibraryFolder(
                    "library-1",
                    "Serien",
                    ["/mnt/raid/Videos/Serien"],
                    RefreshProgress: null,
                    RefreshStatus: null)
            ]
        };
        var service = new EmbyMetadataSyncService(client, new EmbyNfoProviderIdService());

        var result = await service.FindSeriesLibraryAsync(
            new AppEmbySettings { ServerUrl = "http://t-emby:8096", ApiKey = "token" },
            @"Z:\Videos\Serien\Kids");

        Assert.NotNull(result);
        Assert.Equal("library-1", result.Library.Id);
        Assert.Equal("/mnt/raid/Videos/Serien", result.MatchedLocation);
    }

    [Fact]
    public async Task FindSeriesLibraryAsync_MatchesLinuxChildLibrary_WhenArchiveRootIsBroaderThanIt()
    {
        var client = new RecordingEmbyClient
        {
            Libraries =
            [
                new EmbyLibraryFolder(
                    "library-1",
                    "Kids",
                    ["/mnt/raid/Videos/Serien/Kids"],
                    RefreshProgress: null,
                    RefreshStatus: null)
            ]
        };
        var service = new EmbyMetadataSyncService(client, new EmbyNfoProviderIdService());

        var result = await service.FindSeriesLibraryAsync(
            new AppEmbySettings { ServerUrl = "http://t-emby:8096", ApiKey = "token" },
            @"Z:\Videos\Serien");

        Assert.NotNull(result);
        Assert.Equal("library-1", result.Library.Id);
        Assert.Equal("/mnt/raid/Videos/Serien/Kids", result.MatchedLocation);
    }

    [Fact]
    public async Task FindSeriesLibraryAsync_DoesNotMatchSiblingLibrary_WhenOnlyParentSegmentsOverlap()
    {
        var client = new RecordingEmbyClient
        {
            Libraries =
            [
                new EmbyLibraryFolder(
                    "library-1",
                    "Movies",
                    ["/mnt/raid/Videos/Serien/Movies"],
                    RefreshProgress: null,
                    RefreshStatus: null)
            ]
        };
        var service = new EmbyMetadataSyncService(client, new EmbyNfoProviderIdService());

        var result = await service.FindSeriesLibraryAsync(
            new AppEmbySettings { ServerUrl = "http://t-emby:8096", ApiKey = "token" },
            @"Z:\Videos\Serien\Kids");

        Assert.Null(result);
    }

    [Fact]
    public async Task FindItemByPathAsync_UsesTranslatedLibraryPath_WhenEmbyStoresLinuxPaths()
    {
        var expectedLookupPath = "/mnt/raid/Videos/Serien/Serie/Season 01/Serie - S01E01 - Pilot.mkv";
        var client = new RecordingEmbyClient
        {
            Libraries =
            [
                new EmbyLibraryFolder(
                    "library-1",
                    "Serien",
                    ["/mnt/raid/Videos/Serien"],
                    RefreshProgress: null,
                    RefreshStatus: null)
            ],
            ItemByPath = new Dictionary<string, EmbyItem>(StringComparer.OrdinalIgnoreCase)
            {
                [expectedLookupPath] = new EmbyItem(
                    "emby-1",
                    "Pilot",
                    expectedLookupPath,
                    new Dictionary<string, string>())
            }
        };
        var service = new EmbyMetadataSyncService(client, new EmbyNfoProviderIdService());

        var item = await service.FindItemByPathAsync(
            new AppEmbySettings { ServerUrl = "http://t-emby:8096", ApiKey = "token" },
            @"Z:\Videos\Serien\Serie\Season 01\Serie - S01E01 - Pilot.mkv",
            @"Z:\Videos\Serien");

        Assert.NotNull(item);
        Assert.Equal("emby-1", item!.Id);
        Assert.Contains(expectedLookupPath, client.FindRequests);
        Assert.DoesNotContain(@"Z:\Videos\Serien\Serie\Season 01\Serie - S01E01 - Pilot.mkv", client.FindRequests);
    }

    [Fact]
    public async Task FindItemByPathAsync_UsesParentLibraryTranslation_WhenArchiveRootIsNested()
    {
        var expectedLookupPath = "/mnt/raid/Videos/Serien/Kids/Serie/Season 01/Serie - S01E01 - Pilot.mkv";
        var client = new RecordingEmbyClient
        {
            Libraries =
            [
                new EmbyLibraryFolder(
                    "library-1",
                    "Serien",
                    ["/mnt/raid/Videos/Serien"],
                    RefreshProgress: null,
                    RefreshStatus: null)
            ],
            ItemByPath = new Dictionary<string, EmbyItem>(StringComparer.OrdinalIgnoreCase)
            {
                [expectedLookupPath] = new EmbyItem(
                    "emby-1",
                    "Pilot",
                    expectedLookupPath,
                    new Dictionary<string, string>())
            }
        };
        var service = new EmbyMetadataSyncService(client, new EmbyNfoProviderIdService());

        var item = await service.FindItemByPathAsync(
            new AppEmbySettings { ServerUrl = "http://t-emby:8096", ApiKey = "token" },
            @"Z:\Videos\Serien\Kids\Serie\Season 01\Serie - S01E01 - Pilot.mkv",
            @"Z:\Videos\Serien\Kids");

        Assert.NotNull(item);
        Assert.Equal("emby-1", item!.Id);
        Assert.Contains(expectedLookupPath, client.FindRequests);
    }

    [Fact]
    public async Task FindItemByPathAsync_UsesChildLibraryTranslation_WhenArchiveRootIsBroader()
    {
        var expectedLookupPath = "/mnt/raid/Videos/Serien/Kids/Serie/Season 01/Serie - S01E01 - Pilot.mkv";
        var client = new RecordingEmbyClient
        {
            Libraries =
            [
                new EmbyLibraryFolder(
                    "library-1",
                    "Kids",
                    ["/mnt/raid/Videos/Serien/Kids"],
                    RefreshProgress: null,
                    RefreshStatus: null)
            ],
            ItemByPath = new Dictionary<string, EmbyItem>(StringComparer.OrdinalIgnoreCase)
            {
                [expectedLookupPath] = new EmbyItem(
                    "emby-1",
                    "Pilot",
                    expectedLookupPath,
                    new Dictionary<string, string>())
            }
        };
        var service = new EmbyMetadataSyncService(client, new EmbyNfoProviderIdService());

        var item = await service.FindItemByPathAsync(
            new AppEmbySettings { ServerUrl = "http://t-emby:8096", ApiKey = "token" },
            @"Z:\Videos\Serien\Kids\Serie\Season 01\Serie - S01E01 - Pilot.mkv",
            @"Z:\Videos\Serien");

        Assert.NotNull(item);
        Assert.Equal("emby-1", item!.Id);
        Assert.Contains(expectedLookupPath, client.FindRequests);
    }

    [Fact]
    public void ReportProgress_PreservesUnknownFieldsAtEveryLevelIncludingReview()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-report-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var mediaPath = Path.Combine(directory, "Episode.mkv");
            var reportPath = Path.Combine(directory, "run.json");
            File.WriteAllText(reportPath, $$$"""
                {
                  "schemaVersion":1,
                  "futureRoot":{"values":[1,null,"keep"]},
                  "items":[{
                    "outputPath":{{{System.Text.Json.JsonSerializer.Serialize(mediaPath)}}},
                    "futureItem":"keep",
                    "providerIds":{"tvdb":"100","tmdb":"200"},
                    "tvdb":{"episodeId":100,"futureOrigin":true},
                    "embyReview":{"tvdbId":"100","futureReview":{"keep":true}}
                  }]
                }
                """);
            var service = new EmbyMetadataSyncService(new ThrowingEmbyClient(), new EmbyNfoProviderIdService());
            var reviews = new Dictionary<string, BatchOutputEmbyReview>(StringComparer.OrdinalIgnoreCase)
            {
                [mediaPath] = new() { TvdbId = "100", ImdbUnavailable = true, ImdbManuallyReviewed = true }
            };

            var result = service.MarkOutputReportsDone([reportPath], [mediaPath], reviews);

            Assert.Empty(result.FailedReports);
            var savedPath = Assert.Single(result.MovedReports).TargetPath;
            using var saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(savedPath));
            var root = saved.RootElement;
            var item = root.GetProperty("items")[0];
            Assert.Equal("keep", root.GetProperty("futureRoot").GetProperty("values")[2].GetString());
            Assert.Equal("keep", item.GetProperty("futureItem").GetString());
            Assert.Equal("200", item.GetProperty("providerIds").GetProperty("tmdb").GetString());
            Assert.True(item.GetProperty("tvdb").GetProperty("futureOrigin").GetBoolean());
            Assert.True(item.GetProperty("embyReview").GetProperty("futureReview").GetProperty("keep").GetBoolean());
            var bytes = File.ReadAllBytes(savedPath);
            var repeated = service.MarkOutputReportsDone([savedPath], [mediaPath], reviews);
            Assert.Empty(repeated.UpdatedReportPaths);
            Assert.Empty(repeated.MovedReports);
            Assert.Equal(bytes, File.ReadAllBytes(savedPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"items\":[]}")]
    [InlineData("{\"schemaVersion\":0,\"items\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"items\":null}")]
    [InlineData("{\"schemaVersion\":1,\"items\":[null]}")]
    public void InvalidReport_IsRejectedWithoutRewritingOrMoving(string json)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-report-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var reportPath = Path.Combine(directory, "run.json");
            File.WriteAllText(reportPath, json);
            var service = new EmbyMetadataSyncService(new ThrowingEmbyClient(), new EmbyNfoProviderIdService());

            Assert.Throws<InvalidDataException>(() => service.LoadNewOutputReport(reportPath));
            var completion = service.MarkOutputReportsDone([reportPath], [Path.Combine(directory, "Episode.mkv")]);

            Assert.Single(completion.FailedReports);
            Assert.Empty(completion.MovedReports);
            Assert.Equal(json, File.ReadAllText(reportPath));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MarkOutputReportsDone_ReportsDisappearedSource()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.json");
        var service = new EmbyMetadataSyncService(new ThrowingEmbyClient(), new EmbyNfoProviderIdService());

        var completion = service.MarkOutputReportsDone([missingPath], [Path.ChangeExtension(missingPath, ".mkv")]);

        Assert.Single(completion.FailedReports);
        Assert.Empty(completion.UpdatedReportPaths);
        Assert.Empty(completion.MovedReports);
    }

    [Fact]
    public async Task FindSeriesLibraryAsync_DoesNotChooseBetweenDuplicateExactLibraryRoots()
    {
        const string root = @"Z:\Videos\Serien";
        var client = new RecordingEmbyClient
        {
            Libraries = [new("first", "First", [root], null, null), new("second", "Second", [root], null, null)]
        };
        var service = new EmbyMetadataSyncService(client, new EmbyNfoProviderIdService());

        Assert.Null(await service.FindSeriesLibraryAsync(new AppEmbySettings(), root));
    }

    [Fact]
    public async Task FindItemByPathAsync_TranslatesEscapedSmbLibraryRoots()
    {
        const string root = @"Z:\Videos\Meine Serien";
        const string mediaPath = root + @"\Season 01\Episode #1.mkv";
        const string location = "smb://server/share/Videos/Meine%20Serien";
        const string expectedPath = location + "/Season%2001/Episode%20%231.mkv";
        var client = new RecordingEmbyClient
        {
            Libraries = [new("library", "Series", [location], null, null)],
            ItemByPath = new Dictionary<string, EmbyItem>
            {
                [expectedPath] = new("episode", "Episode", expectedPath, new Dictionary<string, string>())
            }
        };
        var service = new EmbyMetadataSyncService(client, new EmbyNfoProviderIdService());

        var item = await service.FindItemByPathAsync(new AppEmbySettings(), mediaPath, root);

        Assert.Equal("episode", item?.Id);
        Assert.Equal(expectedPath, Assert.Single(client.FindRequests));
    }

    [Fact]
    public async Task AnalyzeFileAsync_ObservesCancellationEvenForLocalOnlyAnalysis()
    {
        var service = new EmbyMetadataSyncService(new ThrowingEmbyClient(), new EmbyNfoProviderIdService());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AnalyzeFileAsync(
            new AppEmbySettings(), Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mkv"),
            queryEmby: false, cancellationToken: cancellation.Token));
    }

    [Fact]
    public void EffectiveProviderIds_DoesNotUseSeriesIdAsEpisodeId()
    {
        var item = new EmbyItem("episode", "Episode", "Episode.mkv", new Dictionary<string, string> { ["TvdbSeries"] = "999" });
        var analysis = new EmbyFileAnalysis("Episode.mkv", "Episode.nfo", true, true, EmbyProviderIds.Empty, item, null);

        Assert.Null(analysis.EffectiveProviderIds.TvdbId);
    }

    [Fact]
    public async Task LibraryLookup_CachesMissesButNotScanProgressOrNewScanRequests()
    {
        var client = new RecordingEmbyClient();
        var service = new EmbyMetadataSyncService(client, new EmbyNfoProviderIdService());
        var settings = new AppEmbySettings();
        for (var i = 0; i < 100; i++)
            Assert.Null(await service.FindSeriesLibraryAsync(settings, @"Z:\Videos\Serien"));
        Assert.Equal(1, client.LibraryCallCount);
        await service.GetLibraryByIdAsync(settings, "id");
        await service.GetLibraryByIdAsync(settings, "id");
        Assert.Equal(3, client.LibraryCallCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TriggerSeriesLibraryScanAsync(settings, @"Z:\Videos\Serien"));
        Assert.Equal(4, client.LibraryCallCount);
        settings.ServerArchiveRootPath = "/another/root";
        await service.FindSeriesLibraryAsync(settings, @"Z:\Videos\Serien");
        Assert.Equal(5, client.LibraryCallCount);
    }

    private sealed class ThrowingEmbyClient : IEmbyClient
    {
        public Task<IReadOnlyList<EmbyLibraryFolder>> GetLibrariesAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<EmbyServerInfo> GetSystemInfoAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task TriggerLibraryScanAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task TriggerItemFileScanAsync(AppEmbySettings settings, string itemId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<EmbyItem?> FindItemByPathAsync(AppEmbySettings settings, string mediaFilePath, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task RefreshItemMetadataAsync(AppEmbySettings settings, string itemId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingEmbyClient : IEmbyClient
    {
        public int LibraryCallCount { get; private set; }
        public IReadOnlyList<EmbyLibraryFolder> Libraries { get; init; } = [];

        public IReadOnlyDictionary<string, EmbyItem> ItemByPath { get; init; } = new Dictionary<string, EmbyItem>(StringComparer.OrdinalIgnoreCase);

        public string? LastFindPath { get; private set; }

        public string? LastItemFileScanItemId { get; private set; }

        public int TriggerLibraryScanCallCount { get; private set; }

        public List<string> FindRequests { get; } = [];

        public Task<IReadOnlyList<EmbyLibraryFolder>> GetLibrariesAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
        {
            LibraryCallCount++;
            return Task.FromResult(Libraries);
        }

        public Task<EmbyServerInfo> GetSystemInfoAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task TriggerLibraryScanAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
        {
            TriggerLibraryScanCallCount++;
            return Task.CompletedTask;
        }

        public Task TriggerItemFileScanAsync(AppEmbySettings settings, string itemId, CancellationToken cancellationToken = default)
        {
            LastItemFileScanItemId = itemId;
            return Task.CompletedTask;
        }

        public Task<EmbyItem?> FindItemByPathAsync(AppEmbySettings settings, string mediaFilePath, CancellationToken cancellationToken = default)
        {
            LastFindPath = mediaFilePath;
            FindRequests.Add(mediaFilePath);
            return Task.FromResult(ItemByPath.TryGetValue(mediaFilePath, out var item) ? item : null);
        }

        public Task RefreshItemMetadataAsync(AppEmbySettings settings, string itemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
