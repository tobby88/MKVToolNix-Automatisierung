using System.Collections;
using System.ComponentModel;
using System.Windows.Data;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class BatchEpisodeCollectionControllerTests
{
    [Fact]
    public void HasOpenEditTransaction_ReturnsTrue_WhenCollectionViewIsEditingItem()
    {
        var items = new ArrayList { new TestRow() };
        var view = new ListCollectionView(items);
        var editableView = Assert.IsAssignableFrom<IEditableCollectionView>(view);

        editableView.EditItem(items[0]);

        Assert.True(BatchEpisodeCollectionController.HasOpenEditTransaction(view));
    }

    [Fact]
    public void HasOpenEditTransaction_ReturnsFalse_WhenCollectionViewIsIdle()
    {
        var items = new ArrayList { new TestRow() };
        var view = new ListCollectionView(items);

        Assert.False(BatchEpisodeCollectionController.HasOpenEditTransaction(view));
    }

    [Fact]
    public void SelectedToggle_DoesNotTriggerSelectedItemPlanInputsChanged()
    {
        using var controller = new BatchEpisodeCollectionController();
        var item = BatchEpisodeItemViewModel.CreateErrorItem(@"C:\Temp\episode.mp4", "boom");
        controller.Reset([item]);
        controller.SelectedItem = item;

        var selectedItemPlanInputsChangedCount = 0;
        controller.SelectedItemPlanInputsChanged += () => selectedItemPlanInputsChangedCount++;

        string? observedPropertyName = null;
        item.PropertyChanged += (_, e) => observedPropertyName = e.PropertyName;

        item.IsSelected = true;

        Assert.Equal(nameof(BatchEpisodeItemViewModel.IsSelected), observedPropertyName);
        Assert.Equal(0, selectedItemPlanInputsChangedCount);
    }

    [Theory]
    [InlineData("video")]
    [InlineData("audio")]
    [InlineData("original")]
    public void LanguageOverrideOnSelectedItem_TriggersSelectedItemPlanInputsChanged(string overrideKind)
    {
        using var controller = new BatchEpisodeCollectionController();
        var item = BatchEpisodeItemViewModel.CreateErrorItem(@"C:\Temp\episode.mp4", "boom");
        controller.Reset([item]);
        controller.SelectedItem = item;

        var selectedItemPlanInputsChangedCount = 0;
        controller.SelectedItemPlanInputsChanged += () => selectedItemPlanInputsChangedCount++;

        switch (overrideKind)
        {
            case "video":
                item.SetVideoLanguageOverride("en");
                break;
            case "audio":
                item.SetAudioLanguageOverride("en");
                break;
            case "original":
                item.SetOriginalLanguageOverride("en");
                break;
        }

        Assert.Equal(1, selectedItemPlanInputsChangedCount);
    }

    [Fact]
    public void SeasonEpisodeSortMode_OrdersByNumericSeasonAndEpisode()
    {
        using var controller = new BatchEpisodeCollectionController();
        var seasonTwo = CreateItem(@"C:\Temp\season-two.mp4", "02", "01");
        var episodeTen = CreateItem(@"C:\Temp\episode-ten.mp4", "01", "10");
        var episodeTwo = CreateItem(@"C:\Temp\episode-two.mp4", "01", "02");
        var unknown = CreateItem(@"C:\Temp\unknown.mp4", "xx", "xx");
        controller.Reset([seasonTwo, episodeTen, unknown, episodeTwo]);

        controller.SetSortMode(controller.SortModes.Single(mode => mode.Key == BatchEpisodeSortMode.SeasonEpisode));

        Assert.Equal(
            [episodeTwo, episodeTen, seasonTwo, unknown],
            controller.View.Cast<BatchEpisodeItemViewModel>().ToList());
    }

    [Fact]
    public void SeasonEpisodeSortMode_RefreshesWhenEpisodeNumberChanges()
    {
        using var controller = new BatchEpisodeCollectionController();
        var first = CreateItem(@"C:\Temp\first.mp4", "01", "01");
        var second = CreateItem(@"C:\Temp\second.mp4", "01", "10");
        controller.Reset([second, first]);
        controller.SetSortMode(controller.SortModes.Single(mode => mode.Key == BatchEpisodeSortMode.SeasonEpisode));

        second.EpisodeNumber = "02";

        Assert.Equal(
            [first, second],
            controller.View.Cast<BatchEpisodeItemViewModel>().ToList());
    }

    private static BatchEpisodeItemViewModel CreateItem(string path, string seasonNumber, string episodeNumber)
    {
        var item = BatchEpisodeItemViewModel.CreateErrorItem(path, "boom");
        item.SeasonNumber = seasonNumber;
        item.EpisodeNumber = episodeNumber;
        return item;
    }

    private sealed class TestRow;
}
