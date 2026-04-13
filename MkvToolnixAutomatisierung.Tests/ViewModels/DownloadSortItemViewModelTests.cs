using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class DownloadSortItemViewModelTests
{
    [Fact]
    public void Constructor_SelectsReplacementItems_AndShowsExplicitReplacementStatus()
    {
        var item = new DownloadSortItemViewModel(new DownloadSortCandidate(
            "Ostfriesensturm",
            [@"C:\Downloads\Ostfriesensturm.mp4"],
            "Ostfriesenkrimis",
            "Ostfriesenkrimis",
            DownloadSortItemState.ReadyWithReplacement,
            "Gleichnamige Zieldatei wird ersetzt."));

        Assert.True(item.IsSelected);
        Assert.Equal("Ersetzen", item.StatusText);
    }
}
