using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class EpisodeUiStateTests
{
    [Theory]
    [InlineData(false, true, false, true, EpisodeReviewState.NoneNeeded)]
    [InlineData(true, true, false, true, EpisodeReviewState.Approved)]
    [InlineData(true, false, false, true, EpisodeReviewState.ManualCheckPending)]
    [InlineData(false, true, true, false, EpisodeReviewState.MetadataReviewPending)]
    [InlineData(true, false, true, false, EpisodeReviewState.ManualAndMetadataPending)]
    public void GetReviewState_ReturnsExpectedState(
        bool requiresManualCheck,
        bool isManualCheckApproved,
        bool requiresMetadataReview,
        bool isMetadataReviewApproved,
        EpisodeReviewState expectedState)
    {
        var actualState = EpisodeEditTextBuilder.GetReviewState(
            requiresManualCheck,
            isManualCheckApproved,
            requiresMetadataReview,
            isMetadataReviewApproved);

        Assert.Equal(expectedState, actualState);
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
}
