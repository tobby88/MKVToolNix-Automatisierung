using System.Windows.Controls;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.Windows;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Windows;

public sealed class ImdbLookupWindowTests
{
    [Fact]
    public async Task Window_OffersExplicitLocalRefreshForEditedSearchFields()
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

                var refreshButton = Assert.IsType<Button>(window.FindName("RefreshLocalCandidatesButton"));
                Assert.Equal("Lokal neu suchen", refreshButton.Content);
                Assert.False(refreshButton.IsEnabled);
                Assert.Contains("Offlineindex", refreshButton.ToolTip?.ToString(), StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
