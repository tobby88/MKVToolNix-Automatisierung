using System.IO;
using MkvToolnixAutomatisierung.IntegrationTests.TestInfrastructure;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using Xunit;

namespace MkvToolnixAutomatisierung.IntegrationTests.Services;

[Collection("PortableStorage")]
public sealed class BatchScanCoordinatorIntegrationTests : IDisposable
{
    private readonly PortableStorageFixture _storageFixture;
    private readonly string _tempDirectory;

    public BatchScanCoordinatorIntegrationTests(PortableStorageFixture storageFixture)
    {
        _storageFixture = storageFixture;
        _storageFixture.Reset();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-integration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task ScanAsync_MergesTvdbSelection_AndBuildsArchiveOutputPath()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "source");
        var archiveDirectory = Path.Combine(_tempDirectory, "archive");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).srt");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02).txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");
        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var coordinator = CreateBatchScanCoordinator(archiveDirectory, new StubTvdbClient
        {
            SearchResults =
            [
                new TvdbSeriesSearchResult(42, "Beispielserie", null, null)
            ],
            Episodes =
            [
                new TvdbEpisodeRecord(101, "Pilot", 3, 4, null)
            ]
        });

        var result = await coordinator.ScanAsync(mainVideoPath, archiveDirectory);

        Assert.Equal("Pilot", result.Detected.SuggestedTitle);
        Assert.Equal("03", result.Detected.SeasonNumber);
        Assert.Equal("04", result.Detected.EpisodeNumber);
        Assert.False(result.MetadataResolution.RequiresReview);
        Assert.Equal(
            Path.Combine(archiveDirectory, "Beispielserie", "Season 3", "Beispielserie - S03E04 - Pilot.mkv"),
            result.OutputPath);
    }

    [Fact]
    public void FindMainVideoFiles_FiltersAudioDescription_AndSortsAlphabetically()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "batch-files");
        Directory.CreateDirectory(sourceDirectory);

        CreateFile(sourceDirectory, "Zulu - Finale (S01_E03).mp4");
        CreateFile(sourceDirectory, "Alpha - Pilot (S01_E01).mp4");
        CreateFile(sourceDirectory, "Alpha - Pilot (S01_E01) Audiodeskription.mp4");

        var coordinator = CreateBatchScanCoordinator(Path.Combine(_tempDirectory, "archive"), new StubTvdbClient());

        var files = coordinator.FindMainVideoFiles(sourceDirectory);

        Assert.Equal(2, files.Count);
        Assert.EndsWith("Alpha - Pilot (S01_E01).mp4", files[0], StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("Zulu - Finale (S01_E03).mp4", files[1], StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private BatchScanCoordinator CreateBatchScanCoordinator(string archiveDirectory, StubTvdbClient tvdbClient)
    {
        var settingsStore = new AppSettingsStore();
        new AppToolPathStore(settingsStore).Save(new AppToolPathSettings
        {
            MkvToolNixDirectoryPath = FakeMkvMergeTestHelper.ResolveExecutablePath()
        });

        var metadataStore = new AppMetadataStore(settingsStore);
        metadataStore.Save(new AppMetadataSettings
        {
            TvdbApiKey = "integration-test-key"
        });

        var probeService = new MkvMergeProbeService();
        var archiveService = new SeriesArchiveService(probeService, new AppArchiveSettingsStore(settingsStore));
        archiveService.ConfigureArchiveRootDirectory(archiveDirectory);
        var muxService = new SeriesEpisodeMuxService(
            new SeriesEpisodeMuxPlanner(
                new MkvToolNixLocator(new AppToolPathStore(settingsStore)),
                probeService,
                archiveService,
                new NullDurationProbe()),
            new MuxExecutionService(),
            new MkvMergeOutputParser());

        return new BatchScanCoordinator(
            muxService,
            new EpisodeMetadataLookupService(metadataStore, tvdbClient),
            new EpisodeOutputPathService(archiveService));
    }

    private string CreateFile(string directory, string fileName, string content = "data")
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private static object CreateVideoTrack(int id, string codec, string pixelDimensions)
    {
        return new
        {
            id,
            type = "video",
            codec,
            properties = new
            {
                pixel_dimensions = pixelDimensions,
                language_ietf = "de"
            }
        };
    }

    private static object CreateAudioTrack(int id, string codec)
    {
        return new
        {
            id,
            type = "audio",
            codec,
            properties = new
            {
                language_ietf = "de"
            }
        };
    }

    private sealed class NullDurationProbe : IMediaDurationProbe
    {
        public TimeSpan? TryReadDuration(string filePath)
        {
            return null;
        }
    }

    private sealed class StubTvdbClient : TvdbClient
    {
        public IReadOnlyList<TvdbSeriesSearchResult> SearchResults { get; init; } = [];

        public IReadOnlyList<TvdbEpisodeRecord> Episodes { get; init; } = [];

        public override Task<IReadOnlyList<TvdbSeriesSearchResult>> SearchSeriesAsync(
            string apiKey,
            string? pin,
            string query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SearchResults);
        }

        public override Task<IReadOnlyList<TvdbEpisodeRecord>> GetSeriesEpisodesAsync(
            string apiKey,
            string? pin,
            int seriesId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Episodes);
        }
    }
}
