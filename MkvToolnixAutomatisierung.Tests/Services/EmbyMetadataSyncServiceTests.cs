using System.IO;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Emby;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class EmbyMetadataSyncServiceTests
{
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

            Assert.Equal([reportPath], partialResult.UpdatedReportPaths);
            Assert.Empty(partialResult.MovedReports);
            var partialReport = BatchOutputMetadataReportJson.Deserialize(File.ReadAllText(reportPath))!;
            Assert.True(partialReport.Items[0].EmbySyncDone);
            Assert.Null(partialReport.Items[1].EmbySyncDone);
            Assert.Null(partialReport.EmbySyncCompletedAt);

            var completedResult = service.MarkOutputReportsDone([reportPath], [secondMediaPath]);

            var movedReport = Assert.Single(completedResult.MovedReports);
            Assert.Equal(reportPath, movedReport.SourcePath);
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
    public async Task TriggerSeriesLibraryScanAsync_FallsBackToGlobalScan_WhenArchiveRootIsNotFound()
    {
        var client = new RecordingEmbyClient();
        var service = new EmbyMetadataSyncService(client, new EmbyNfoProviderIdService());

        var result = await service.TriggerSeriesLibraryScanAsync(
            new AppEmbySettings { ServerUrl = "http://t-emby:8096", ApiKey = "token" },
            @"Z:\Videos\Serien");

        Assert.True(result.UsedGlobalLibraryScan);
        Assert.Equal(1, client.TriggerLibraryScanCallCount);
        Assert.Null(client.LastItemFileScanItemId);
        Assert.Null(result.Library);
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
        public IReadOnlyList<EmbyLibraryFolder> Libraries { get; init; } = [];

        public IReadOnlyDictionary<string, EmbyItem> ItemByPath { get; init; } = new Dictionary<string, EmbyItem>(StringComparer.OrdinalIgnoreCase);

        public string? LastFindPath { get; private set; }

        public string? LastItemFileScanItemId { get; private set; }

        public int TriggerLibraryScanCallCount { get; private set; }

        public List<string> FindRequests { get; } = [];

        public Task<IReadOnlyList<EmbyLibraryFolder>> GetLibrariesAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => Task.FromResult(Libraries);

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
