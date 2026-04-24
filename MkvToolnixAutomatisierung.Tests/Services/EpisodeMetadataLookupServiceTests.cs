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
    public async Task SearchSeriesAsync_UsesTrimmedStoredCredentials()
    {
        var store = new FakeMetadataStore(new AppMetadataSettings
        {
            TvdbApiKey = "  key  ",
            TvdbPin = "  pin  "
        });
        var client = new FakeTvdbClient
        {
            SearchSeriesResultFactory = _ => [new TvdbSeriesSearchResult(42, "Beispielserie", "2024", null)]
        };
        var service = new EpisodeMetadataLookupService(store, client);

        var results = await service.SearchSeriesAsync("Beispielserie");

        Assert.Single(results);
        Assert.Equal("key", client.LastApiKey);
        Assert.Equal("pin", client.LastPin);
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
    public async Task SearchSeriesAsync_DeduplicatesConcurrentRequests()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<TvdbSeriesSearchResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new FakeMetadataStore(new AppMetadataSettings());
        var client = new FakeTvdbClient
        {
            SearchSeriesAsyncOverride = (query, _) =>
            {
                started.TrySetResult(true);
                return release.Task;
            }
        };
        var service = new EpisodeMetadataLookupService(store, client);
        var settings = new AppMetadataSettings
        {
            TvdbApiKey = "key",
            TvdbPin = "pin"
        };

        var firstTask = service.SearchSeriesAsync("Beispielserie", settings);
        await started.Task;
        var secondTask = service.SearchSeriesAsync("Beispielserie", settings);

        release.SetResult([new TvdbSeriesSearchResult(42, "Beispielserie", "2024", null)]);
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Same(results[0], results[1]);
        Assert.Equal(1, client.SearchSeriesCallCount);
    }

    [Fact]
    public async Task LoadEpisodesAsync_DeduplicatesConcurrentRequests()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<TvdbEpisodeRecord>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new FakeMetadataStore(new AppMetadataSettings());
        var client = new FakeTvdbClient
        {
            GetSeriesEpisodesAsyncOverride = (seriesId, _) =>
            {
                started.TrySetResult(true);
                return release.Task;
            }
        };
        var service = new EpisodeMetadataLookupService(store, client);
        var settings = new AppMetadataSettings
        {
            TvdbApiKey = "key",
            TvdbPin = "pin"
        };

        var firstTask = service.LoadEpisodesAsync(42, settings);
        await started.Task;
        var secondTask = service.LoadEpisodesAsync(42, settings);

        release.SetResult([new TvdbEpisodeRecord(7, "Pilot", 1, 1, "2024-01-01")]);
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Same(results[0], results[1]);
        Assert.Equal(1, client.LoadEpisodesCallCount);
    }

    [Fact]
    public async Task SearchSeriesAsync_CancelledWaiter_DoesNotAbortSharedLookupForOtherCallers()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<TvdbSeriesSearchResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new FakeMetadataStore(new AppMetadataSettings());
        var client = new FakeTvdbClient
        {
            SearchSeriesAsyncOverride = (query, _) =>
            {
                started.TrySetResult(true);
                return release.Task;
            }
        };
        var service = new EpisodeMetadataLookupService(store, client);
        var settings = new AppMetadataSettings
        {
            TvdbApiKey = "key"
        };
        using var cancellationSource = new CancellationTokenSource();

        var cancelledWaiter = service.SearchSeriesAsync("Beispielserie", settings, cancellationSource.Token);
        await started.Task;
        var survivingWaiter = service.SearchSeriesAsync("Beispielserie", settings);

        cancellationSource.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledWaiter);

        release.SetResult([new TvdbSeriesSearchResult(42, "Beispielserie", "2024", null)]);
        var result = await survivingWaiter;

        Assert.Single(result);
        Assert.Equal(1, client.SearchSeriesCallCount);
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

    [Fact]
    public async Task ResolveAutomaticallyAsync_PrefersMultipartEpisodeTitle_OverGenericSeriesLikeEpisode()
    {
        var settings = new AppMetadataSettings
        {
            TvdbApiKey = "key"
        };
        var store = new FakeMetadataStore(settings);
        var client = new FakeTvdbClient
        {
            SearchSeriesResultFactory = _ => [new TvdbSeriesSearchResult(42, "Pippi Långstrump", "1969", null)],
            EpisodesResultFactory = _ =>
            [
                new TvdbEpisodeRecord(100, "Pippi Långstrump", 1, 1, "1969-02-08"),
                new TvdbEpisodeRecord(101, "Pippi und die Seeräuber - Teil 2", 1, 2, "1969-02-15")
            ]
        };
        var service = new EpisodeMetadataLookupService(store, client);

        var result = await service.ResolveAutomaticallyAsync(
            new EpisodeMetadataGuess("Pippi Langstrumpf", "Pippi und die Seeräuber 2. Teil", "xx", "xx"));

        Assert.True(result.QuerySucceeded);
        Assert.False(result.RequiresReview);
        Assert.NotNull(result.Selection);
        Assert.Equal(101, result.Selection!.TvdbEpisodeId);
        Assert.Equal("Pippi und die Seeräuber - Teil 2", result.Selection.EpisodeTitle);
    }

    [Fact]
    public async Task ResolveAutomaticallyAsync_RequiresReview_WhenLocalGuessUsesEpisodeRange()
    {
        var settings = new AppMetadataSettings
        {
            TvdbApiKey = "key"
        };
        var store = new FakeMetadataStore(settings);
        var client = new FakeTvdbClient
        {
            SearchSeriesResultFactory = _ => [new TvdbSeriesSearchResult(42, "Beispielserie", "2024", null)],
            EpisodesResultFactory = _ => [new TvdbEpisodeRecord(100, "Rififi", 2014, 5, "2024-01-01")]
        };
        var service = new EpisodeMetadataLookupService(store, client);

        var result = await service.ResolveAutomaticallyAsync(
            new EpisodeMetadataGuess("Beispielserie", "Rififi", "2014", "05-E06"));

        Assert.True(result.QuerySucceeded);
        Assert.True(result.RequiresReview);
        Assert.NotNull(result.Selection);
        Assert.Contains("TVDB-Vorschlag prüfen", result.StatusText);
    }

    [Fact]
    public async Task ResolveAutomaticallyAsync_KeepsTvdbSpecialsAsSeasonZero()
    {
        var settings = new AppMetadataSettings
        {
            TvdbApiKey = "key"
        };
        var store = new FakeMetadataStore(settings);
        var client = new FakeTvdbClient
        {
            SearchSeriesResultFactory = _ => [new TvdbSeriesSearchResult(42, "Beispielserie", "2024", null)],
            EpisodesResultFactory = _ => [new TvdbEpisodeRecord(100, "Sonderfolge", 0, 7, "2024-01-01")]
        };
        var service = new EpisodeMetadataLookupService(store, client);

        var result = await service.ResolveAutomaticallyAsync(
            new EpisodeMetadataGuess("Beispielserie", "Sonderfolge", "xx", "xx"));

        Assert.True(result.QuerySucceeded);
        Assert.NotNull(result.Selection);
        Assert.Equal("00", result.Selection!.SeasonNumber);
        Assert.Equal("07", result.Selection.EpisodeNumber);
    }

    [Theory]
    [InlineData("Extra: Blindenhund")]
    [InlineData("Extra: Tangostunde für Blinde")]
    [InlineData("Making-of: Blindentennis")]
    [InlineData("Extra: Blind mit Kind")]
    public async Task ResolveAutomaticallyAsync_DoesNotMapSpecialMaterialToNormalBlindEpisode(string localTitle)
    {
        var settings = new AppMetadataSettings
        {
            TvdbApiKey = "key"
        };
        var store = new FakeMetadataStore(settings);
        var client = new FakeTvdbClient
        {
            SearchSeriesResultFactory = _ => [new TvdbSeriesSearchResult(42, "Die Heiland - Wir sind Anwalt", "2018", null)],
            EpisodesResultFactory = _ => [new TvdbEpisodeRecord(510, "Blind", 5, 10, "2024-01-01")]
        };
        var service = new EpisodeMetadataLookupService(store, client);

        var result = await service.ResolveAutomaticallyAsync(
            new EpisodeMetadataGuess("Die Heiland - Wir sind Anwalt", localTitle, "xx", "xx"));

        Assert.True(result.QuerySucceeded);
        Assert.True(result.RequiresReview);
        Assert.Null(result.Selection);
        Assert.Contains("keine Episode sicher zuordenbar", result.StatusText);
    }

    [Fact]
    public async Task ResolveAutomaticallyAsync_DoesNotMapTrailerSuffixToNormalEpisode()
    {
        var settings = new AppMetadataSettings
        {
            TvdbApiKey = "key"
        };
        var store = new FakeMetadataStore(settings);
        var client = new FakeTvdbClient
        {
            SearchSeriesResultFactory = _ => [new TvdbSeriesSearchResult(42, "Pettersson und Findus", "1999", null)],
            EpisodesResultFactory = _ => [new TvdbEpisodeRecord(210, "Findus zieht um", 2, 10, "2024-01-01")]
        };
        var service = new EpisodeMetadataLookupService(store, client);

        var result = await service.ResolveAutomaticallyAsync(
            new EpisodeMetadataGuess("Pettersson und Findus", "Findus zieht um Trailer", "xx", "xx"));

        Assert.True(result.QuerySucceeded);
        Assert.True(result.RequiresReview);
        Assert.Null(result.Selection);
    }

    [Fact]
    public async Task ResolveAutomaticallyAsync_DoesNotMapWeakTitleMatchByEpisodeNumberOnly()
    {
        var settings = new AppMetadataSettings
        {
            TvdbApiKey = "key"
        };
        var store = new FakeMetadataStore(settings);
        var client = new FakeTvdbClient
        {
            SearchSeriesResultFactory = _ => [new TvdbSeriesSearchResult(42, "XY gelöst", "2022", null)],
            EpisodesResultFactory = _ => [new TvdbEpisodeRecord(202, "Tödliche Freiheit", 2, 2, "2024-01-01")]
        };
        var service = new EpisodeMetadataLookupService(store, client);

        var result = await service.ResolveAutomaticallyAsync(
            new EpisodeMetadataGuess("XY gelöst", "Tödliche Nachtschicht", "05", "02"));

        Assert.True(result.QuerySucceeded);
        Assert.True(result.RequiresReview);
        Assert.Null(result.Selection);
        Assert.Contains("keine Episode sicher zuordenbar", result.StatusText);
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

        public Func<string, CancellationToken, Task<IReadOnlyList<TvdbSeriesSearchResult>>>? SearchSeriesAsyncOverride { get; init; }

        public Func<int, CancellationToken, Task<IReadOnlyList<TvdbEpisodeRecord>>>? GetSeriesEpisodesAsyncOverride { get; init; }

        public Exception? SearchSeriesException { get; init; }

        public int SearchSeriesCallCount { get; private set; }

        public int LoadEpisodesCallCount { get; private set; }

        public string? LastApiKey { get; private set; }

        public string? LastPin { get; private set; }

        public Task<IReadOnlyList<TvdbSeriesSearchResult>> SearchSeriesAsync(
            string apiKey,
            string? pin,
            string query,
            CancellationToken cancellationToken = default)
        {
            SearchSeriesCallCount++;
            LastApiKey = apiKey;
            LastPin = pin;

            if (SearchSeriesAsyncOverride is not null)
            {
                return SearchSeriesAsyncOverride(query, cancellationToken);
            }

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
            string? language = null,
            CancellationToken cancellationToken = default)
        {
            LoadEpisodesCallCount++;
            LastApiKey = apiKey;
            LastPin = pin;

            if (GetSeriesEpisodesAsyncOverride is not null)
            {
                return GetSeriesEpisodesAsyncOverride(seriesId, cancellationToken);
            }

            IReadOnlyList<TvdbEpisodeRecord> results = EpisodesResultFactory?.Invoke(seriesId) ?? [];
            return Task.FromResult(results);
        }

        public void Dispose()
        {
        }
    }
}
