using System.IO;
using System.Reflection;
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
    public void ToggleSelectedItemSelectionCommand_TogglesCurrentItem()
    {
        var vm = CreateViewModel();
        var item = CreateSortableItem("S01E01.mp4");
        item.IsSelected = false;
        vm.Items.Add(item);
        vm.SelectedItem = item;

        Assert.True(vm.ToggleSelectedItemSelectionCommand.CanExecute(null));

        vm.ToggleSelectedItemSelectionCommand.Execute(null);

        Assert.True(item.IsSelected);
        Assert.Same(item, vm.SelectedItem);
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

    [Fact]
    public void ToggleSelectedItemSelectionCommand_CannotExecute_ForNonSortableSelectedItem()
    {
        var vm = CreateViewModel();
        var item = CreateNonSortableItem();
        vm.Items.Add(item);
        vm.SelectedItem = item;

        Assert.False(item.IsSelected);
        Assert.False(vm.ToggleSelectedItemSelectionCommand.CanExecute(null));
    }

    [Fact]
    public void MixedDefectiveItem_TargetChange_ReevaluatesAndClearsSelection()
    {
        var rootDirectory = CreateTemporaryDirectory();
        try
        {
            var videoPath = Path.Combine(rootDirectory, "Neues aus Büttenwarder-Bildungsschock-0186867506.mp4");
            var textPath = Path.Combine(rootDirectory, "Neues aus Büttenwarder-Bildungsschock-0186867506.txt");
            var subtitlePath = Path.Combine(rootDirectory, "Neues aus Büttenwarder-Bildungsschock-0186867506.srt");
            CreateFileWithByteLength(videoPath, length: 1024 * 1024);
            CreateCompanionText(
                textPath,
                topic: "Neues aus Büttenwarder",
                title: "Bildungsschock",
                sizeText: "100,0 MiB");
            CreateEmptyFile(subtitlePath);

            var service = new DownloadSortService();
            var scanResult = service.Scan(rootDirectory);
            var vm = CreateViewModel();
            ApplyScanResult(vm, rootDirectory, scanResult);
            var item = Assert.Single(vm.Items);
            vm.SelectedItem = item;

            Assert.Equal(DownloadSortItemState.Ready, item.State);
            Assert.True(item.IsSelected);

            item.TargetFolderName = "defekt";

            Assert.Equal(DownloadSortItemState.NeedsReview, item.State);
            Assert.False(item.IsSelected);
            Assert.Equal("Pruefen + Defekt", item.StatusText);
            Assert.Contains("deutlich kleiner", item.Note, StringComparison.Ordinal);
            Assert.Contains("reserviert", item.Note, StringComparison.Ordinal);
            Assert.False(vm.RunSortCommand.CanExecute(null));
            Assert.DoesNotContain(vm.TargetFolderOptions, option => string.Equals(option, "defekt", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public void TargetFolderOptions_ExcludeReservedDefectiveFolder()
    {
        var rootDirectory = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(rootDirectory, "defekt"));
            Directory.CreateDirectory(Path.Combine(rootDirectory, "SOKO Leipzig"));

            var vm = CreateViewModel();
            ApplyScanResult(vm, rootDirectory, new DownloadSortService().Scan(rootDirectory));

            Assert.Contains("SOKO Leipzig", vm.TargetFolderOptions);
            Assert.DoesNotContain(vm.TargetFolderOptions, option => string.Equals(option, "defekt", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public void TargetFolderChange_WhenSourceDirectoryDisappears_ShowsNeedsReviewInsteadOfThrowing()
    {
        var rootDirectory = CreateTemporaryDirectory();
        try
        {
            var videoPath = Path.Combine(rootDirectory, "Testserie-Pilot-1234.mp4");
            var textPath = Path.Combine(rootDirectory, "Testserie-Pilot-1234.txt");
            CreateEmptyFile(videoPath);
            CreateCompanionText(
                textPath,
                topic: "Testserie",
                title: "Pilot");

            var vm = CreateViewModel();
            ApplyScanResult(vm, rootDirectory, new DownloadSortService().Scan(rootDirectory));
            var item = Assert.Single(vm.Items);
            vm.SelectedItem = item;

            Directory.Delete(rootDirectory, recursive: true);

            item.TargetFolderName = "Andere Serie";

            Assert.Equal(DownloadSortItemState.NeedsReview, item.State);
            Assert.False(item.IsSelected);
            Assert.Contains("Bitte neu scannen", item.Note, StringComparison.Ordinal);
            Assert.Equal("Scan-Ergebnis veraltet. Bitte neu scannen.", vm.StatusText);
            Assert.False(vm.RunSortCommand.CanExecute(null));
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    private static void ApplyScanResult(DownloadSortViewModel viewModel, string sourceDirectory, DownloadSortScanResult scanResult)
    {
        typeof(DownloadSortViewModel)
            .GetField("_sourceDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(viewModel, sourceDirectory);
        typeof(DownloadSortViewModel)
            .GetMethod("ApplyScanResult", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(viewModel, [scanResult]);
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mkv-auto-download-sort-vm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void CreateEmptyFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "content");
    }

    private static void CreateFileWithByteLength(string path, int length, byte value = 0)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Enumerable.Repeat(value, length).ToArray());
    }

    private static void CreateCompanionText(
        string path,
        string topic,
        string title,
        string duration = "00:05:00",
        string? sizeText = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lines = new List<string>
        {
            $"Thema:       {topic}",
            string.Empty,
            $"Titel:       {title}",
            string.Empty,
            $"Dauer:       {duration}"
        };
        if (!string.IsNullOrWhiteSpace(sizeText))
        {
            lines.Add($"Größe:       {sizeText}");
        }

        File.WriteAllText(path, string.Join(Environment.NewLine, lines));
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
        public string[]? SelectFiles(string title, string filter, string initialDirectory) => null;
        public MessageBoxResult AskAudioDescriptionChoice() => MessageBoxResult.Cancel;
        public MessageBoxResult AskSubtitlesChoice() => MessageBoxResult.Cancel;
        public MessageBoxResult AskAttachmentChoice() => MessageBoxResult.Cancel;
        public bool ConfirmMuxStart() => false;
        public bool ConfirmBatchExecution(int itemCount, int archiveFileCount, long archiveTotalBytes) => false;
        public bool ConfirmApplyBatchSelectionToAllItems(bool selectItems) => false;
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
