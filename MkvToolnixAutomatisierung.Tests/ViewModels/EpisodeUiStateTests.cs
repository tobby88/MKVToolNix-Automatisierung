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
}
