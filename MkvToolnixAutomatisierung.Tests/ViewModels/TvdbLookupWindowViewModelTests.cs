using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class TvdbLookupWindowViewModelTests
{
    [Fact]
    public async Task InitializeAsync_DoesNotSearchWithoutApiKey_AndShowsSetupStatus()
    {
        var store = new FakeMetadataStore(new AppMetadataSettings());
        var client = new FakeTvdbClient();
        var service = new EpisodeMetadataLookupService(store, client);
        var viewModel = new TvdbLookupWindowViewModel(
            service,
            new EpisodeMetadataGuess("Beispielserie", "Pilot", "01", "01"));

        await viewModel.InitializeAsync();

        Assert.Empty(viewModel.SeriesResults);
        Assert.Empty(viewModel.EpisodeResults);
        Assert.Null(viewModel.SelectedSeriesItem);
        Assert.Null(viewModel.SelectedEpisodeItem);
        Assert.Contains("TVDB-API-Key fehlt", viewModel.StatusText);
        Assert.Equal(0, client.SearchSeriesCallCount);
    }

    [Fact]
    public async Task SearchSeriesAsync_AutoSelectsPreferredSeriesAndEpisode()
    {
        var store = new FakeMetadataStore(new AppMetadataSettings
        {
            TvdbApiKey = "key"
        });
        var client = new FakeTvdbClient
        {
            SearchSeriesResultFactory = _ =>
            [
                new TvdbSeriesSearchResult(42, "Beispielserie", "2024", null),
                new TvdbSeriesSearchResult(99, "Andere Serie", "2023", null)
            ],
            EpisodesResultFactory = seriesId => seriesId == 42
                ? [new TvdbEpisodeRecord(100, "Pilot", 1, 1, "2024-01-01")]
                : [new TvdbEpisodeRecord(200, "Falsche Folge", 1, 1, "2024-01-01")]
        };
        var service = new EpisodeMetadataLookupService(store, client);
        var viewModel = new TvdbLookupWindowViewModel(
            service,
            new EpisodeMetadataGuess("Beispielserie", "Pilot", "01", "01"));

        await viewModel.SearchSeriesAsync(autoLoadEpisodes: true);

        Assert.Equal(42, viewModel.SelectedSeriesItem?.Series.Id);
        Assert.Equal(100, viewModel.SelectedEpisodeItem?.Episode.Id);
        Assert.Equal("TVDB stimmt mit der lokalen Erkennung überein.", viewModel.ComparisonSummaryText);
        Assert.Contains("TVDB-Vorschlag", viewModel.StatusText);
    }

    [Fact]
    public async Task EpisodeSearchText_FiltersEpisodesAndClearsSelectionWhenMatchChanges()
    {
        var service = CreateServiceWithEpisodes(
            episodes:
            [
                new TvdbEpisodeRecord(100, "Pilot", 1, 1, "2024-01-01"),
                new TvdbEpisodeRecord(101, "Finale", 1, 2, "2024-01-08")
            ]);
        var viewModel = new TvdbLookupWindowViewModel(
            service,
            new EpisodeMetadataGuess("Beispielserie", "Pilot", "01", "01"));

        await viewModel.SearchSeriesAsync(autoLoadEpisodes: true);
        viewModel.EpisodeSearchText = "Finale";

        Assert.Single(viewModel.EpisodeResults);
        Assert.Equal(101, viewModel.EpisodeResults[0].Episode.Id);
        Assert.Null(viewModel.SelectedEpisodeItem);
        Assert.Equal("Noch keine TVDB-Episode ausgewählt.", viewModel.ComparisonSummaryText);
    }

    [Fact]
    public async Task EpisodeSearchText_FiltersEpisodesByEpisodeCode()
    {
        var service = CreateServiceWithEpisodes(
            episodes:
            [
                new TvdbEpisodeRecord(100, "Pilot", 1, 1, "2024-01-01"),
                new TvdbEpisodeRecord(101, "Finale", 1, 2, "2024-01-08")
            ]);
        var viewModel = new TvdbLookupWindowViewModel(
            service,
            new EpisodeMetadataGuess("Beispielserie", "Pilot", "01", "01"));

        await viewModel.SearchSeriesAsync(autoLoadEpisodes: true);
        viewModel.EpisodeSearchText = "S01E02";

        Assert.Single(viewModel.EpisodeResults);
        Assert.Equal(101, viewModel.EpisodeResults[0].Episode.Id);
    }

    [Fact]
    public async Task TryBuildSelection_SavesMappingAndReturnsSelection()
    {
        var store = new FakeMetadataStore(new AppMetadataSettings
        {
            TvdbApiKey = "key"
        });
        var client = new FakeTvdbClient
        {
            SearchSeriesResultFactory = _ => [new TvdbSeriesSearchResult(42, "Beispielserie", "2024", null)],
            EpisodesResultFactory = _ => [new TvdbEpisodeRecord(100, "Pilot", 1, 1, "2024-01-01")]
        };
        var service = new EpisodeMetadataLookupService(store, client);
        var viewModel = new TvdbLookupWindowViewModel(
            service,
            new EpisodeMetadataGuess("Beispielserie", "Pilot", "01", "01"));

        await viewModel.SearchSeriesAsync(autoLoadEpisodes: true);

        var success = viewModel.TryBuildSelection(out var selection, out var validationMessage);

        Assert.True(success);
        Assert.Null(validationMessage);
        Assert.NotNull(selection);
        Assert.Equal(42, selection!.TvdbSeriesId);
        Assert.Equal(100, selection.TvdbEpisodeId);
        Assert.Contains(store.CurrentSettings.SeriesMappings, mapping => mapping.TvdbSeriesId == 42);
    }

    private static EpisodeMetadataLookupService CreateServiceWithEpisodes(IReadOnlyList<TvdbEpisodeRecord> episodes)
    {
        return new EpisodeMetadataLookupService(
            new FakeMetadataStore(new AppMetadataSettings
            {
                TvdbApiKey = "key"
            }),
            new FakeTvdbClient
            {
                SearchSeriesResultFactory = _ => [new TvdbSeriesSearchResult(42, "Beispielserie", "2024", null)],
                EpisodesResultFactory = _ => episodes
            });
    }

    private sealed class FakeMetadataStore : IAppMetadataStore
    {
        public FakeMetadataStore(AppMetadataSettings initialSettings)
        {
            CurrentSettings = initialSettings.Clone();
        }

        public AppMetadataSettings CurrentSettings { get; private set; }

        public string SettingsFilePath => "test-settings.json";

        public AppMetadataSettings Load()
        {
            return CurrentSettings.Clone();
        }

        public void Save(AppMetadataSettings settings)
        {
            CurrentSettings = settings.Clone();
        }
    }

    private sealed class FakeTvdbClient : ITvdbClient
    {
        public int SearchSeriesCallCount { get; private set; }

        public int GetSeriesEpisodesCallCount { get; private set; }

        public Func<string, IReadOnlyList<TvdbSeriesSearchResult>>? SearchSeriesResultFactory { get; init; }

        public Func<int, IReadOnlyList<TvdbEpisodeRecord>>? EpisodesResultFactory { get; init; }

        public Task<IReadOnlyList<TvdbSeriesSearchResult>> SearchSeriesAsync(
            string apiKey,
            string? pin,
            string query,
            CancellationToken cancellationToken = default)
        {
            SearchSeriesCallCount++;
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
            GetSeriesEpisodesCallCount++;
            IReadOnlyList<TvdbEpisodeRecord> results = EpisodesResultFactory?.Invoke(seriesId) ?? [];
            return Task.FromResult(results);
        }

        public void Dispose()
        {
        }
    }
}
