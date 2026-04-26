using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class EpisodeUiStateTests
{
    [Theory]
    [InlineData(false, true, false, true, 0)]
    [InlineData(true, true, false, true, 1)]
    [InlineData(true, false, false, true, 2)]
    [InlineData(false, true, true, false, 3)]
    [InlineData(true, false, true, false, 4)]
    public void GetReviewState_ReturnsExpectedState(
        bool requiresManualCheck,
        bool isManualCheckApproved,
        bool requiresMetadataReview,
        bool isMetadataReviewApproved,
        int expectedState)
    {
        var actualState = EpisodeEditTextBuilder.GetReviewState(
            requiresManualCheck,
            isManualCheckApproved,
            requiresMetadataReview,
            isMetadataReviewApproved);

        Assert.Equal(expectedState, (int)actualState);
    }

    [Fact]
    public void BatchEpisodeItemStatus_UsesExplicitStatusKindForDetailedErrorText()
    {
        var item = BatchEpisodeItemViewModel.CreateErrorItem(@"C:\Temp\episode.mp4", "boom");

        item.SetStatus(BatchEpisodeStatusKind.Error, "Fehler (2)");

        Assert.Equal(BatchEpisodeStatusKind.Error, item.StatusKind);
        Assert.Equal(0, item.StatusSortKey);
        Assert.Equal("Fehler (2)", item.Status);
    }

    [Theory]
    [InlineData("Bitte diese Quelle prüfen.", false)]
    [InlineData("Begleit-TXT und ermittelte Dateilaufzeit widersprechen sich deutlich. Bitte Archivtreffer und Episodencode manuell prüfen.", true)]
    [InlineData("Mehrere getrennt erkannte Quellen zeigen auf dieselbe Ausgabedatei 'Pilot.mkv'. Bitte Episodencode und Ausgabeziel prüfen.", true)]
    public void IsActionablePlanReviewNote_UsesExplicitPlannerSignals(string note, bool expected)
    {
        Assert.Equal(expected, EpisodeEditTextBuilder.IsActionablePlanReviewNote(note));
    }

    [Theory]
    [InlineData("Begleit-TXT und ermittelte Dateilaufzeit widersprechen sich deutlich. Bitte Archivtreffer und Episodencode manuell prüfen.", "Archiv prüfen")]
    [InlineData("Mehrere getrennt erkannte Quellen zeigen auf dieselbe Ausgabedatei 'Pilot.mkv'. Bitte Episodencode und Ausgabeziel prüfen.", "Ziel prüfen")]
    [InlineData("In der Bibliothek existiert zusätzlich eine Mehrfachfolge mit demselben Titel (S01E01-E02). Bitte prüfen, ob die aktuelle Quelle zu einer Doppel- oder Mehrfachfolge gehört.", "Mehrfachfolge prüfen")]
    public void BuildPlanReviewLabel_UsesExplicitReviewCategory(string note, string expectedLabel)
    {
        Assert.Equal(expectedLabel, EpisodeEditTextBuilder.BuildPlanReviewLabel([note]));
    }

    [Fact]
    public void LanguageOverrideOptions_ExposeSupportedNonGermanLanguages()
    {
        Assert.Contains(MuxLanguageOverrideOptions.All, option => option.Code == "fr" && option.DisplayName == "Français");
        Assert.Contains(MuxLanguageOverrideOptions.All, option => option.Code == "sv" && option.DisplayName == "Svenska");
        Assert.Contains(MuxLanguageOverrideOptions.All, option => option.Code == "ja" && option.DisplayName == "日本語");
    }
}
