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
    public void CreateDirectoryContext_KeepsAudioDescriptionOnlyEntries_AndSortsAlphabetically()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "batch-files");
        Directory.CreateDirectory(sourceDirectory);

        CreateFile(sourceDirectory, "Zulu - Finale (S01_E03).mp4");
        CreateFile(sourceDirectory, "Alpha - Pilot (S01_E01).mp4");
        CreateFile(sourceDirectory, "Alpha - Pilot (S01_E01) Audiodeskription.mp4");
        CreateFile(sourceDirectory, "Beta - Spezial (S01_E02) Audiodeskription.mp4");
        CreateFile(
            sourceDirectory,
            "Beta - Spezial (S01_E02) Audiodeskription.txt",
            "Sender: ARD\r\nThema: Beta\r\nTitel: Spezial (S01_E02)\r\nDauer: 00:42:00");

        var coordinator = CreateBatchScanCoordinator(Path.Combine(_tempDirectory, "archive"), new StubTvdbClient());

        var files = coordinator.CreateDirectoryContext(sourceDirectory).MainVideoFiles;

        Assert.Equal(3, files.Count);
        Assert.EndsWith("Alpha - Pilot (S01_E01).mp4", files[0], StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("Beta - Spezial (S01_E02) Audiodeskription.mp4", files[1], StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("Zulu - Finale (S01_E03).mp4", files[2], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanAsync_WithPreparedDirectoryContext_ReusesPreparedMetadataAcrossMultipleScans()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "prepared-context");
        var archiveDirectory = Path.Combine(_tempDirectory, "prepared-context-archive");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var firstVideoPath = CreateFile(sourceDirectory, "Quelle Eins.mp4");
        var secondVideoPath = CreateFile(sourceDirectory, "Quelle Zwei.mp4");
        CreateFile(
            sourceDirectory,
            "Quelle Eins.txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Auftakt (S02_E05)\r\nDauer: 00:42:00");
        var secondAttachmentPath = CreateFile(
            sourceDirectory,
            "Quelle Zwei.txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Finale (S02_E06)\r\nDauer: 00:43:00");
        FakeMkvMergeTestHelper.WriteProbeFile(
            firstVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));
        FakeMkvMergeTestHelper.WriteProbeFile(
            secondVideoPath,
            CreateVideoTrack(0, "HEVC/H.265", "1920x1080"),
            CreateAudioTrack(1, "AAC"));

        var coordinator = CreateBatchScanCoordinator(archiveDirectory, new StubTvdbClient());
        var directoryContext = coordinator.CreateDirectoryContext(sourceDirectory);

        var firstResult = await coordinator.ScanAsync(directoryContext, firstVideoPath, archiveDirectory);
        File.Delete(secondAttachmentPath);
        var secondResult = await coordinator.ScanAsync(directoryContext, secondVideoPath, archiveDirectory);

        Assert.Equal("Beispielserie", firstResult.Detected.SeriesName);
        Assert.Equal("Auftakt", firstResult.Detected.SuggestedTitle);
        Assert.Equal("02", firstResult.Detected.SeasonNumber);
        Assert.Equal("05", firstResult.Detected.EpisodeNumber);
        Assert.Equal("Beispielserie", secondResult.Detected.SeriesName);
        Assert.Equal("Finale", secondResult.Detected.SuggestedTitle);
        Assert.Equal("02", secondResult.Detected.SeasonNumber);
        Assert.Equal("06", secondResult.Detected.EpisodeNumber);
        Assert.Equal(
            Path.Combine(archiveDirectory, "Beispielserie", "Season 2", "Beispielserie - S02E06 - Finale.mkv"),
            secondResult.OutputPath);
    }

    [Fact]
    public async Task ScanAsync_CanBeCancelled_DuringMetadataLookup()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "cancelled-scan");
        var archiveDirectory = Path.Combine(_tempDirectory, "cancelled-scan-archive");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E02).mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E02).txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E02)\r\nDauer: 00:42:00");
        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var searchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = CreateBatchScanCoordinator(archiveDirectory, new StubTvdbClient
        {
            SearchSeriesAsyncOverride = async cancellationToken =>
            {
                searchStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return [];
            }
        });
        using var cancellationSource = new CancellationTokenSource();

        var scanTask = coordinator.ScanAsync(mainVideoPath, archiveDirectory, cancellationToken: cancellationSource.Token);
        await searchStarted.Task;
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanTask);
    }

    [Fact]
    public async Task ScanAsync_AudioDescriptionOnly_KeepsArchiveStructure_WhenOutputDirectoryIsArchiveRootButArchiveIsCurrentlyUnavailable()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "ad-only-source");
        var archiveDirectory = Path.Combine(_tempDirectory, "missing-archive-root");
        Directory.CreateDirectory(sourceDirectory);

        var audioDescriptionPath = CreateFile(sourceDirectory, "Neues aus Büttenwarder-Ostern (Audiodeskription)-2079540621.mp4");
        CreateFile(
            sourceDirectory,
            "Neues aus Büttenwarder-Ostern (Audiodeskription)-2079540621.txt",
            "Sender: NDR\r\nThema: Neues aus Büttenwarder\r\nTitel: Ostern (Audiodeskription)\r\nDauer: 00:25:34");
        FakeMkvMergeTestHelper.WriteProbeFile(
            audioDescriptionPath,
            CreateAudioTrack(0, "AAC"));

        var coordinator = CreateBatchScanCoordinator(archiveDirectory, new StubTvdbClient());

        var result = await coordinator.ScanAsync(audioDescriptionPath, archiveDirectory);

        Assert.False(result.Detected.HasPrimaryVideoSource);
        Assert.Equal("Neues aus Büttenwarder", result.Detected.SeriesName);
        Assert.Equal("Ostern", result.Detected.SuggestedTitle);
        Assert.Equal(
            Path.Combine(archiveDirectory, "Neues aus Büttenwarder", "Season xx", "Neues aus Büttenwarder - SxxExx - Ostern.mkv"),
            result.OutputPath);
    }

    [Fact]
    public async Task ScanAsync_AudioDescriptionOnly_ReusesExistingArchiveOutputPath_WhenLibraryFileAlreadyExists()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "ad-only-existing-source");
        var archiveDirectory = Path.Combine(_tempDirectory, "ad-only-existing-archive");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var audioDescriptionPath = CreateFile(sourceDirectory, "Neues aus Büttenwarder-Ostern (Audiodeskription)-2079540621.mp4");
        CreateFile(
            sourceDirectory,
            "Neues aus Büttenwarder-Ostern (Audiodeskription)-2079540621.txt",
            "Sender: NDR\r\nThema: Neues aus Büttenwarder\r\nTitel: Ostern (Audiodeskription)\r\nDauer: 00:25:34");
        FakeMkvMergeTestHelper.WriteProbeFile(
            audioDescriptionPath,
            CreateAudioTrack(0, "AAC"));

        var archiveFilePath = Path.Combine(
            archiveDirectory,
            "Neues aus Büttenwarder",
            "Season xx",
            "Neues aus Büttenwarder - SxxExx - Ostern.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(archiveFilePath)!);
        CreateFile(Path.GetDirectoryName(archiveFilePath)!, Path.GetFileName(archiveFilePath), "archive");

        var coordinator = CreateBatchScanCoordinator(archiveDirectory, new StubTvdbClient());

        var result = await coordinator.ScanAsync(audioDescriptionPath, archiveDirectory);

        Assert.False(result.Detected.HasPrimaryVideoSource);
        Assert.Equal(archiveFilePath, result.OutputPath);
    }

    [Fact]
    public async Task ScanAsync_AudioDescriptionOnly_ReusesUniqueArchiveTitleMatch_WhenExactCanonicalPathDiffers()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "ad-only-title-match-source");
        var archiveDirectory = Path.Combine(_tempDirectory, "ad-only-title-match-archive");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var audioDescriptionPath = CreateFile(sourceDirectory, "Neues aus Büttenwarder-Ostern (Audiodeskription)-2079540621.mp4");
        CreateFile(
            sourceDirectory,
            "Neues aus Büttenwarder-Ostern (Audiodeskription)-2079540621.txt",
            "Sender: NDR\r\nThema: Neues aus Büttenwarder\r\nTitel: Ostern (Audiodeskription)\r\nDauer: 00:25:34");
        FakeMkvMergeTestHelper.WriteProbeFile(
            audioDescriptionPath,
            CreateAudioTrack(0, "AAC"));

        var archiveFilePath = Path.Combine(
            archiveDirectory,
            "Neues aus Büttenwarder",
            "Season 2001",
            "Neues aus Büttenwarder - S2001E01 - Ostern.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(archiveFilePath)!);
        CreateFile(Path.GetDirectoryName(archiveFilePath)!, Path.GetFileName(archiveFilePath), "archive");

        var coordinator = CreateBatchScanCoordinator(archiveDirectory, new StubTvdbClient());

        var result = await coordinator.ScanAsync(audioDescriptionPath, archiveDirectory);

        Assert.False(result.Detected.HasPrimaryVideoSource);
        Assert.Equal(archiveFilePath, result.OutputPath);
    }

    [Fact]
    public async Task ScanAsync_MarksExistingArchiveMatchForReview_WhenDurationLooksLikeDoubleEpisode()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "double-episode-source");
        var archiveDirectory = Path.Combine(_tempDirectory, "double-episode-archive");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(archiveDirectory);

        var mainVideoPath = CreateFile(sourceDirectory, "Beispielserie - Pilot (S01_E01).mp4");
        CreateFile(
            sourceDirectory,
            "Beispielserie - Pilot (S01_E01).txt",
            "Sender: ZDF\r\nThema: Beispielserie\r\nTitel: Pilot (S01_E01)\r\nDauer: 00:43:00");
        FakeMkvMergeTestHelper.WriteProbeFile(
            mainVideoPath,
            CreateVideoTrack(0, "AVC/H.264", "1920x1080"),
            CreateAudioTrack(1, "E-AC-3"));

        var archiveFilePath = Path.Combine(
            archiveDirectory,
            "Beispielserie",
            "Season 1",
            "Beispielserie - S01E01 - Pilot.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(archiveFilePath)!);
        CreateFile(Path.GetDirectoryName(archiveFilePath)!, Path.GetFileName(archiveFilePath), "archive");

        var coordinator = CreateBatchScanCoordinator(
            archiveDirectory,
            new StubTvdbClient(),
            new DictionaryDurationProbe(new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
            {
                [mainVideoPath] = TimeSpan.FromMinutes(43),
                [archiveFilePath] = TimeSpan.FromMinutes(86)
            }));

        var result = await coordinator.ScanAsync(mainVideoPath, archiveDirectory);

        Assert.Equal(archiveFilePath, result.OutputPath);
        Assert.True(result.MetadataResolution.RequiresReview);
        Assert.Contains("Laufzeitdifferenz", result.MetadataResolution.StatusText, StringComparison.Ordinal);
        Assert.Contains(result.Detected.Notes, note => note.Contains("Doppelfolge", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private BatchScanCoordinator CreateBatchScanCoordinator(
        string archiveDirectory,
        StubTvdbClient tvdbClient,
        IMediaDurationProbe? durationProbe = null)
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
        var effectiveDurationProbe = durationProbe ?? new NullDurationProbe();
        var muxService = new SeriesEpisodeMuxService(
            new SeriesEpisodeMuxPlanner(
                new MkvToolNixLocator(new AppToolPathStore(settingsStore)),
                probeService,
                archiveService,
                effectiveDurationProbe),
            new MuxExecutionService(),
            new MkvMergeOutputParser());

        return new BatchScanCoordinator(
            muxService,
            new EpisodeMetadataLookupService(metadataStore, tvdbClient),
            new EpisodeOutputPathService(archiveService, effectiveDurationProbe));
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

    private sealed class DictionaryDurationProbe : IMediaDurationProbe
    {
        private readonly IReadOnlyDictionary<string, TimeSpan> _durations;

        public DictionaryDurationProbe(IReadOnlyDictionary<string, TimeSpan> durations)
        {
            _durations = durations;
        }

        public TimeSpan? TryReadDuration(string filePath)
        {
            return _durations.TryGetValue(filePath, out var duration)
                ? duration
                : null;
        }
    }

    private sealed class StubTvdbClient : ITvdbClient
    {
        public IReadOnlyList<TvdbSeriesSearchResult> SearchResults { get; init; } = [];

        public IReadOnlyList<TvdbEpisodeRecord> Episodes { get; init; } = [];

        public Func<CancellationToken, Task<IReadOnlyList<TvdbSeriesSearchResult>>>? SearchSeriesAsyncOverride { get; init; }

        public Func<CancellationToken, Task<IReadOnlyList<TvdbEpisodeRecord>>>? GetSeriesEpisodesAsyncOverride { get; init; }

        public Task<IReadOnlyList<TvdbSeriesSearchResult>> SearchSeriesAsync(
            string apiKey,
            string? pin,
            string query,
            CancellationToken cancellationToken = default)
        {
            if (SearchSeriesAsyncOverride is not null)
            {
                return SearchSeriesAsyncOverride(cancellationToken);
            }

            return Task.FromResult(SearchResults);
        }

        public Task<IReadOnlyList<TvdbEpisodeRecord>> GetSeriesEpisodesAsync(
            string apiKey,
            string? pin,
            int seriesId,
            CancellationToken cancellationToken = default)
        {
            if (GetSeriesEpisodesAsyncOverride is not null)
            {
                return GetSeriesEpisodesAsyncOverride(cancellationToken);
            }

            return Task.FromResult(Episodes);
        }

        public void Dispose()
        {
        }
    }
}
