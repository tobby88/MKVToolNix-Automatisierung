using System.Windows.Controls;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.Windows;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Windows;

public sealed class ImdbLookupWindowTests
{
    [Fact]
    public async Task Window_OffersLiveSeriesSeasonAndEpisodeBrowser()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var window = new ImdbLookupWindow(
                new EpisodeMetadataGuess("Der Alte", "Die Wahrheit im Dunkeln", "55", "02"),
                currentImdbId: null);
            try
            {
                window.Show();
                await WpfTestHost.WaitForIdleAsync();

                var seriesSearch = Assert.IsType<TextBox>(window.FindName("LocalSeriesSearchTextBox"));
                var episodeSearch = Assert.IsType<TextBox>(window.FindName("LocalEpisodeSearchTextBox"));
                var seriesCandidates = Assert.IsType<ComboBox>(window.FindName("LocalSeriesCandidatesComboBox"));
                var seasonFilter = Assert.IsType<ComboBox>(window.FindName("SeasonFilterComboBox"));
                var episodeCandidates = Assert.IsType<ListView>(window.FindName("LocalCandidatesListView"));

                Assert.Contains("automatisch", seriesSearch.ToolTip?.ToString(), StringComparison.OrdinalIgnoreCase);
                Assert.Contains("unscharf", episodeSearch.ToolTip?.ToString(), StringComparison.OrdinalIgnoreCase);
                Assert.Null(window.FindName("RefreshLocalCandidatesButton"));
                Assert.NotNull(seriesCandidates.ItemsSource);
                Assert.NotNull(seasonFilter.ItemsSource);
                Assert.NotNull(episodeCandidates.ItemsSource);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
