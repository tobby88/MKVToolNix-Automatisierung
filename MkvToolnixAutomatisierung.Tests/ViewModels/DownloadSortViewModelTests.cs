using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class DownloadSortViewModelTests
{
    private static DownloadSortViewModel CreateViewModel(IUserDialogService? dialogService = null)
    {
        var services = new DownloadSortModuleServices(new DownloadSortService());
        return new DownloadSortViewModel(services, dialogService ?? new NullDialogService());
    }

    private static DownloadSortItemViewModel CreateSortableItem(string displayName = "Testserie - Pilot.mp4")
    {
        return new DownloadSortItemViewModel(new DownloadSortCandidate(
            displayName,
            [$@"C:\Downloads\{displayName}"],
            "Testserie",
            "Testserie",
            DownloadSortItemState.Ready,
            string.Empty));
    }

    private static DownloadSortItemViewModel CreateNonSortableItem(string displayName = "pruefen.mp4")
    {
        return new DownloadSortItemViewModel(new DownloadSortCandidate(
            displayName,
            [$@"C:\Downloads\{displayName}"],
            null,
            string.Empty,
            DownloadSortItemState.NeedsReview,
            "Manuelle Prüfung erforderlich."));
    }

    [Fact]
    public void SummaryText_WhenNoItems_ShowsEmptyPlaceholder()
    {
        var vm = CreateViewModel();

        Assert.Equal("Keine losen Download-Dateien erkannt.", vm.SummaryText);
    }

    [Fact]
    public void SummaryText_AfterItemsAdded_ContainsItemCount()
    {
        var vm = CreateViewModel();
        vm.Items.Add(CreateSortableItem("S01E01.mp4"));
        vm.Items.Add(CreateSortableItem("S01E02.mp4"));

        Assert.Contains("2 Paket(e)", vm.SummaryText);
    }

    [Fact]
    public void IsInteractive_Initially_IsTrue()
    {
        var vm = CreateViewModel();

        Assert.True(vm.IsInteractive);
    }

    [Fact]
    public void SelectAllSortableCommand_SelectsOnlySortableItems()
    {
        var vm = CreateViewModel();
        var sortable = CreateSortableItem("S01E01.mp4");
        var defective = CreateNonSortableItem("defekt.mp4");
        sortable.IsSelected = false;
        defective.IsSelected = false;
        vm.Items.Add(sortable);
        vm.Items.Add(defective);

        vm.SelectAllSortableCommand.Execute(null);

        Assert.True(sortable.IsSelected);
        Assert.False(defective.IsSelected);
    }

    [Fact]
    public void DeselectAllCommand_DeselectsAllSelectedItems()
    {
        var vm = CreateViewModel();
        var item1 = CreateSortableItem("S01E01.mp4");
        var item2 = CreateSortableItem("S01E02.mp4");
        item1.IsSelected = true;
        item2.IsSelected = true;
        vm.Items.Add(item1);
        vm.Items.Add(item2);

        vm.DeselectAllCommand.Execute(null);

        Assert.False(item1.IsSelected);
        Assert.False(item2.IsSelected);
    }

    [Fact]
    public void RunSortCommand_CannotExecute_WhenNoItemsSelected()
    {
        var vm = CreateViewModel();
        var item = CreateSortableItem();
        item.IsSelected = false;
        vm.Items.Add(item);

        Assert.False(vm.RunSortCommand.CanExecute(null));
    }

    [Fact]
    public void ReadyCount_ReflectsOnlyReadyItems()
    {
        var vm = CreateViewModel();
        vm.Items.Add(CreateSortableItem("S01E01.mp4"));
        vm.Items.Add(CreateNonSortableItem("pruefen.mp4"));

        Assert.Equal(1, vm.ReadyCount);
        Assert.Equal(1, vm.ReviewCount);
    }

    [Fact]
    public void ItemCount_ReflectsTotalItems()
    {
        var vm = CreateViewModel();
        vm.Items.Add(CreateSortableItem("A.mp4"));
        vm.Items.Add(CreateSortableItem("B.mp4"));
        vm.Items.Add(CreateNonSortableItem("C.mp4"));

        Assert.Equal(3, vm.ItemCount);
    }

    private sealed class NullDialogService : IUserDialogService
    {
        public string? SelectMainVideo(string initialDirectory) => null;
        public string? SelectAudioDescription(string initialDirectory) => null;
        public string[]? SelectSubtitles(string initialDirectory) => null;
        public string[]? SelectAttachments(string initialDirectory) => null;
        public string? SelectOutput(string initialDirectory, string fileName) => null;
        public string? SelectFolder(string title, string initialDirectory) => null;
        public string? SelectExecutable(string title, string filter, string initialDirectory) => null;
        public string? SelectFile(string title, string filter, string initialDirectory) => null;
        public MessageBoxResult AskAudioDescriptionChoice() => MessageBoxResult.Cancel;
        public MessageBoxResult AskSubtitlesChoice() => MessageBoxResult.Cancel;
        public MessageBoxResult AskAttachmentChoice() => MessageBoxResult.Cancel;
        public bool ConfirmMuxStart() => false;
        public bool ConfirmBatchExecution(int itemCount, int archiveFileCount, long archiveTotalBytes) => false;
        public bool ConfirmArchiveCopy(FileCopyPlan copyPlan) => false;
        public bool ConfirmSingleEpisodeCleanup(IReadOnlyList<string> usedFiles, IReadOnlyList<string> unusedFiles) => false;
        public bool ConfirmBatchRecycleDoneFiles(int fileCount, string doneDirectory) => false;
        public bool AskOpenDoneDirectory(string doneDirectory) => false;
        public bool ConfirmPlanReview(string episodeTitle, string reviewText) => false;
        public bool TryOpenFilesWithDefaultApp(IEnumerable<string> filePaths) => false;
        public void OpenPathWithDefaultApp(string path) { }
        public MessageBoxResult AskSourceReviewResult(string fileName, bool canTryAlternative) => MessageBoxResult.Cancel;
        public void ShowInfo(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string message) { }
    }
}
