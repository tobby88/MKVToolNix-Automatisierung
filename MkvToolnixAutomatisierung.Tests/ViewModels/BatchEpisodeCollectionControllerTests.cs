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

    private sealed class TestRow;
}
