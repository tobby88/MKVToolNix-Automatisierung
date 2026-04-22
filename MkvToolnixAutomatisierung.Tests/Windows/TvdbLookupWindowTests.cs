using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.ViewModels;
using MkvToolnixAutomatisierung.Windows;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Windows;

public sealed class TvdbLookupWindowTests
{
    [Fact]
    public async Task ApplyButton_RemainsDisabledWhileSeriesSearchIsBusy()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var client = new BlockingTvdbClient(
                [new TvdbSeriesSearchResult(42, "Beispielserie", "2024", null)],
                [new TvdbEpisodeRecord(100, "Pilot", 1, 1, "2024-01-01")]);
            var service = new EpisodeMetadataLookupService(
                new FakeMetadataStore(new AppMetadataSettings
                {
                    TvdbApiKey = "key"
                }),
                client);
            var window = new TvdbLookupWindow(
                service,
                new EpisodeMetadataGuess("Beispielserie", "Pilot", "01", "01"));

            try
            {
                window.Show();

                var viewModel = Assert.IsType<TvdbLookupWindowViewModel>(window.DataContext);
                Assert.True(await WaitUntilAsync(() => viewModel.IsInteractive && viewModel.CanApply, TimeSpan.FromSeconds(2)));
                await WpfTestHost.WaitForIdleAsync();

                var applyButton = FindButtonByContent(window, "Übernehmen");
                Assert.NotNull(applyButton);
                Assert.True(applyButton.IsEnabled);

                viewModel.SeriesSearchText = "Beispielserie neu";
                client.BlockNextSeriesSearch();
                var runningSearch = viewModel.SearchSeriesAsync(autoLoadEpisodes: true);

                Assert.True(await WaitUntilAsync(() => viewModel.IsBusy, TimeSpan.FromSeconds(2)));
                await WpfTestHost.WaitForIdleAsync();

                Assert.True(viewModel.CanApply);
                Assert.False(applyButton.IsEnabled);

                client.ReleaseBlockedSeriesSearch();
                await runningSearch;

                Assert.True(await WaitUntilAsync(() => viewModel.IsInteractive && viewModel.CanApply, TimeSpan.FromSeconds(2)));
                await WpfTestHost.WaitForIdleAsync();

                Assert.True(applyButton.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow <= deadline)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(50);
            await WpfTestHost.WaitForIdleAsync();
        }

        return predicate();
    }

    private static Button FindButtonByContent(DependencyObject parent, string content)
    {
        return FindVisualChildren<Button>(parent)
            .First(button => string.Equals(button.Content as string, content, StringComparison.Ordinal));
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? parent)
        where T : DependencyObject
    {
        if (parent is null)
        {
            yield break;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typedChild)
            {
                yield return typedChild;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
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

    private sealed class BlockingTvdbClient : ITvdbClient
    {
        private readonly IReadOnlyList<TvdbSeriesSearchResult> _seriesResults;
        private readonly IReadOnlyList<TvdbEpisodeRecord> _episodeResults;
        private TaskCompletionSource<IReadOnlyList<TvdbSeriesSearchResult>>? _pendingSearch;
        private bool _blockNextSeriesSearch;

        public BlockingTvdbClient(
            IReadOnlyList<TvdbSeriesSearchResult> seriesResults,
            IReadOnlyList<TvdbEpisodeRecord> episodeResults)
        {
            _seriesResults = seriesResults;
            _episodeResults = episodeResults;
        }

        public void BlockNextSeriesSearch()
        {
            _blockNextSeriesSearch = true;
        }

        public void ReleaseBlockedSeriesSearch()
        {
            _pendingSearch?.TrySetResult(_seriesResults);
        }

        public Task<IReadOnlyList<TvdbSeriesSearchResult>> SearchSeriesAsync(
            string apiKey,
            string? pin,
            string query,
            CancellationToken cancellationToken = default)
        {
            if (!_blockNextSeriesSearch)
            {
                return Task.FromResult(_seriesResults);
            }

            _blockNextSeriesSearch = false;
            _pendingSearch = new TaskCompletionSource<IReadOnlyList<TvdbSeriesSearchResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _pendingSearch.Task;
        }

        public Task<IReadOnlyList<TvdbEpisodeRecord>> GetSeriesEpisodesAsync(
            string apiKey,
            string? pin,
            int seriesId,
            string? language = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_episodeResults);
        }

        public void Dispose()
        {
        }
    }
}
