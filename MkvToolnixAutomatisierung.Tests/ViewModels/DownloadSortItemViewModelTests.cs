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
    public void Constructor_RespectsExplicitInitialSelectionFlag()
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

    [Fact]
    public void Constructor_ClearsSelectionForNonSortableItems_EvenWhenCandidateRequestsSelection()
    {
        var item = new DownloadSortItemViewModel(new DownloadSortCandidate(
            "Prueffall",
            [@"C:\Downloads\Prueffall.txt"],
            null,
            string.Empty,
            DownloadSortItemState.NeedsReview,
            "Manuelle Prüfung erforderlich.",
            IsInitiallySelected: true));

        Assert.False(item.IsSelected);
        Assert.False(item.CanSelect);
        Assert.Equal("Pruefen", item.StatusText);
    }

    [Fact]
    public void ApplyEvaluation_ClearsSelection_AndPreservesPersistentDefectNote()
    {
        var item = new DownloadSortItemViewModel(new DownloadSortCandidate(
            "Neues aus Büttenwarder",
            [@"C:\Downloads\episode.mp4", @"C:\Downloads\episode.txt"],
            "Neues aus Büttenwarder",
            "Neues aus Büttenwarder",
            DownloadSortItemState.Ready,
            "MP4 ist deutlich kleiner als die in der TXT erwartete Größe. Begleitdateien bleiben regulär nutzbar.",
            IsInitiallySelected: true,
            DefectiveFilePaths: [@"C:\Downloads\episode.mp4"],
            PersistentNote: "MP4 ist deutlich kleiner als die in der TXT erwartete Größe. Begleitdateien bleiben regulär nutzbar.",
            ContainsDefectiveFiles: true));

        Assert.True(item.IsSelected);
        Assert.Equal("Bereit + Defekt", item.StatusText);

        item.ApplyEvaluation(new DownloadSortTargetEvaluation(
            DownloadSortItemState.NeedsReview,
            "Kein Zielordner erkannt. Bitte pruefen."));

        Assert.False(item.IsSelected);
        Assert.Equal("Pruefen + Defekt", item.StatusText);
        Assert.Contains("deutlich kleiner", item.Note, StringComparison.Ordinal);
        Assert.Contains("Kein Zielordner erkannt", item.Note, StringComparison.Ordinal);
    }
}
