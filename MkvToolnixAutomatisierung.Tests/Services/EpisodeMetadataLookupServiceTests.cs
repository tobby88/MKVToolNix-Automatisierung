using MkvToolnixAutomatisierung.Services.Metadata;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class EpisodeMetadataLookupServiceTests
{
    [Fact]
    public async Task ResolveAutomaticallyAsync_ReturnsSkippedResult_WhenApiKeyIsMissing()
    {
        var store = new FakeMetadataStore(new AppMetadataSettings());
        var client = new FakeTvdbClient();
        var service = new EpisodeMetadataLookupService(store, client);

        var result = await service.ResolveAutomaticallyAsync(
            new EpisodeMetadataGuess("Beispielserie", "Pilot", "01", "01"));

        Assert.False(result.QueryWasAttempted);
        Assert.False(result.QuerySucceeded);
        Assert.False(result.RequiresReview);
        Assert.Null(result.Selection);
        Assert.Contains("API-Key fehlt", result.StatusText);
        Assert.Equal(0, client.SearchSeriesCallCount);
    }

    [Fact]
    public async Task SearchSeriesAsync_UsesNormalizedCacheKey()
    {
        var store = new FakeMetadataStore(new AppMetadataSettings());
        var client = new FakeTvdbClient
        {
            SearchSeriesResultFactory = _ => [new TvdbSeriesSearchResult(42, "Beispielserie", "2024", null)]
        };
        var service = new EpisodeMetadataLookupService(store, client);
        var settings = new AppMetadataSettings
        {
            TvdbApiKey = "key",
            TvdbPin = "pin"
        };

        var first = await service.SearchSeriesAsync("Beispiel-Serie", settings);
        var second = await service.SearchSeriesAsync(" beispiel serie ", settings);

        Assert.Single(first);
        Assert.Same(first, second);
        Assert.Equal(1, client.SearchSeriesCallCount);
    }

    [Fact]
    public async Task LoadEpisodesAsync_CachesEpisodesPerSeries()
    {
        var store = new FakeMetadataStore(new AppMetadataSettings());
        var client = new FakeTvdbClient
        {
            EpisodesResultFactory = _ => [new TvdbEpisodeRecord(7, "Pilot", 1, 1, "2024-01-01")]
        };
        var service = new EpisodeMetadataLookupService(store, client);
        var settings = new AppMetadataSettings
        {
            TvdbApiKey = "key",
            TvdbPin = "pin"
        };

        var first = await service.LoadEpisodesAsync(42, settings);
        var second = await service.LoadEpisodesAsync(42, settings);

        Assert.Single(first);
        Assert.Same(first, second);
        Assert.Equal(1, client.LoadEpisodesCallCount);
    }

    [Fact]
    public async Task ResolveAutomaticallyAsync_ReturnsReviewFailure_WhenTvdbLookupThrows()
    {
        var store = new FakeMetadataStore(new AppMetadataSettings
        {
            TvdbApiKey = "key"
        });
        var client = new FakeTvdbClient
        {
            SearchSeriesException = new InvalidOperationException("kaputt")
        };
        var service = new EpisodeMetadataLookupService(store, client);

        var result = await service.ResolveAutomaticallyAsync(
            new EpisodeMetadataGuess("Beispielserie", "Pilot", "01", "01"));

        Assert.True(result.QueryWasAttempted);
        Assert.False(result.QuerySucceeded);
        Assert.True(result.RequiresReview);
        Assert.Null(result.Selection);
        Assert.Contains("kaputt", result.StatusText);
    }

    [Fact]
    public async Task ResolveAutomaticallyAsync_AcceptsUnambiguousExactMatch_AndStoresSeriesMapping()
    {
        var settings = new AppMetadataSettings
        {
            TvdbApiKey = "key"
        };
        var store = new FakeMetadataStore(settings);
        var client = new FakeTvdbClient
        {
            SearchSeriesResultFactory = _ => [new TvdbSeriesSearchResult(42, "Beispielserie", "2024", null)],
            EpisodesResultFactory = _ => [new TvdbEpisodeRecord(100, "Pilot", 1, 1, "2024-01-01")]
        };
        var service = new EpisodeMetadataLookupService(store, client);

        var result = await service.ResolveAutomaticallyAsync(
            new EpisodeMetadataGuess("Beispielserie", "Pilot", "01", "01"));

        Assert.True(result.QueryWasAttempted);
        Assert.True(result.QuerySucceeded);
        Assert.False(result.RequiresReview);
        Assert.NotNull(result.Selection);
        Assert.Equal(42, result.Selection!.TvdbSeriesId);
        Assert.Equal(100, result.Selection.TvdbEpisodeId);
        Assert.Contains("TVDB automatisch erkannt", result.StatusText);
        Assert.Equal(1, store.SaveCallCount);
        Assert.Contains(store.CurrentSettings.SeriesMappings, mapping => mapping.TvdbSeriesId == 42 && mapping.LocalSeriesName == "Beispielserie");
    }

    [Fact]
    public async Task ResolveAutomaticallyAsync_MatchesGermanUmlautsAndTransliterations()
    {
        var settings = new AppMetadataSettings
        {
            TvdbApiKey = "key"
        };
        var store = new FakeMetadataStore(settings);
        var client = new FakeTvdbClient
        {
            SearchSeriesResultFactory = _ => [new TvdbSeriesSearchResult(42, "Müller & Söhne", "2024", null)],
            EpisodesResultFactory = _ => [new TvdbEpisodeRecord(100, "Überraschung", 1, 1, "2024-01-01")]
        };
        var service = new EpisodeMetadataLookupService(store, client);

        var result = await service.ResolveAutomaticallyAsync(
            new EpisodeMetadataGuess("Mueller und Soehne", "Ueberraschung", "01", "01"));

        Assert.True(result.QuerySucceeded);
        Assert.False(result.RequiresReview);
        Assert.NotNull(result.Selection);
        Assert.Equal("Müller & Söhne", result.Selection!.TvdbSeriesName);
        Assert.Equal("Überraschung", result.Selection.EpisodeTitle);
    }

    private sealed class FakeMetadataStore : IAppMetadataStore
    {
        public FakeMetadataStore(AppMetadataSettings initialSettings)
        {
            CurrentSettings = initialSettings.Clone();
        }

        public AppMetadataSettings CurrentSettings { get; private set; }

        public int SaveCallCount { get; private set; }

        public string SettingsFilePath => "test-settings.json";

        public AppMetadataSettings Load()
        {
            return CurrentSettings.Clone();
        }

        public void Save(AppMetadataSettings settings)
        {
            SaveCallCount++;
            CurrentSettings = settings.Clone();
        }
    }

    private sealed class FakeTvdbClient : ITvdbClient
    {
        public Func<string, IReadOnlyList<TvdbSeriesSearchResult>>? SearchSeriesResultFactory { get; init; }

        public Func<int, IReadOnlyList<TvdbEpisodeRecord>>? EpisodesResultFactory { get; init; }

        public Exception? SearchSeriesException { get; init; }

        public int SearchSeriesCallCount { get; private set; }

        public int LoadEpisodesCallCount { get; private set; }

        public Task<IReadOnlyList<TvdbSeriesSearchResult>> SearchSeriesAsync(
            string apiKey,
            string? pin,
            string query,
            CancellationToken cancellationToken = default)
        {
            SearchSeriesCallCount++;

            if (SearchSeriesException is not null)
            {
                throw SearchSeriesException;
            }

            IReadOnlyList<TvdbSeriesSearchResult> results = SearchSeriesResultFactory?.Invoke(query) ?? [];
            return Task.FromResult(results);
        }

        public Task<IReadOnlyList<TvdbEpisodeRecord>> GetSeriesEpisodesAsync(
            string apiKey,
            string? pin,
            int seriesId,
            CancellationToken cancellationToken = default)
        {
            LoadEpisodesCallCount++;
            IReadOnlyList<TvdbEpisodeRecord> results = EpisodesResultFactory?.Invoke(seriesId) ?? [];
            return Task.FromResult(results);
        }

        public void Dispose()
        {
        }
    }
}
