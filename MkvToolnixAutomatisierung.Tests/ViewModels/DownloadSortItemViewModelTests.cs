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
        Assert.Equal("#E5F0FF", item.StatusBadgeBackground);
    }

    [Fact]
    public void Constructor_RespectsInitialSelectionFlag_ForSidecarOnlyFollowUps()
    {
        var item = new DownloadSortItemViewModel(new DownloadSortCandidate(
            "Stralsund-Außer Kontrolle",
            [@"C:\Downloads\Stralsund-Außer Kontrolle.txt"],
            "Stralsund",
            "Stralsund",
            DownloadSortItemState.Ready,
            "Nur Begleitdateien einer defekten MP4; standardmäßig nicht vorausgewählt.",
            IsInitiallySelected: false));

        Assert.False(item.IsSelected);
        Assert.Equal("Bereit", item.StatusText);
    }
}
