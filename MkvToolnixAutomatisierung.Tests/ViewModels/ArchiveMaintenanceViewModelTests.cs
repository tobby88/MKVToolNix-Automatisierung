using System.IO;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

[Collection("PortableStorage")]
public sealed class ArchiveMaintenanceViewModelTests
{
    private readonly PortableStorageFixture _storageFixture;

    public ArchiveMaintenanceViewModelTests(PortableStorageFixture storageFixture)
    {
        _storageFixture = storageFixture;
        _storageFixture.Reset();
    }

    [Fact]
    public void SummaryText_WhenNoItems_ShowsPlaceholder()
    {
        var viewModel = CreateViewModel();

        Assert.Equal("Noch kein Scan durchgeführt.", viewModel.SummaryText);
    }

    [Fact]
    public void ToggleSelectedItemSelectionCommand_TogglesWritableItem()
    {
        var viewModel = CreateViewModel();
        var item = new ArchiveMaintenanceItemViewModel(CreateWritableAnalysis());
        item.IsSelected = false;
        viewModel.Items.Add(item);
        viewModel.SelectedItem = item;

        viewModel.ToggleSelectedItemSelectionCommand.Execute(null);

        Assert.True(item.IsSelected);
        Assert.Contains("1 ausgewählt", viewModel.SummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectAllWritableCommand_LeavesRemuxOnlyItemsUnselected()
    {
        var viewModel = CreateViewModel();
        var writable = new ArchiveMaintenanceItemViewModel(CreateWritableAnalysis());
        var remuxOnly = new ArchiveMaintenanceItemViewModel(new ArchiveMaintenanceItemAnalysis(
            @"C:\Archiv\Serie\Season 1\Serie - S01E02 - Folge 2.mkv",
            "Folge 2",
            RenameOperation: null,
            ContainerTitleEdit: null,
            TrackHeaderEdits: [],
            Issues: [new ArchiveMaintenanceIssue(ArchiveMaintenanceIssueKind.RemuxRequired, "Doppelte AD-Spuren.")],
            ChangeNotes: [],
            ErrorMessage: null));
        writable.IsSelected = false;
        viewModel.Items.Add(writable);
        viewModel.Items.Add(remuxOnly);

        viewModel.SelectAllWritableCommand.Execute(null);

        Assert.True(writable.IsSelected);
        Assert.False(remuxOnly.IsSelected);
    }

    private static ArchiveMaintenanceItemAnalysis CreateWritableAnalysis()
    {
        return new ArchiveMaintenanceItemAnalysis(
            @"C:\Archiv\Serie\Season 1\Serie - S01E01 - Pilot.mkv",
            "Pilot",
            RenameOperation: null,
            ContainerTitleEdit: new ContainerTitleEditOperation("Alt", "Pilot"),
            TrackHeaderEdits: [],
            Issues: [],
            ChangeNotes: ["MKV-Titel: Alt -> Pilot"],
            ErrorMessage: null);
    }

    private static ArchiveMaintenanceViewModel CreateViewModel()
    {
        var settingsStore = new AppSettingsStore();
        var archiveSettingsStore = new AppArchiveSettingsStore(settingsStore);
        var services = new ArchiveMaintenanceModuleServices(
            new ArchiveMaintenanceService(
                new MkvMergeProbeService(),
                new StubMkvToolNixLocator(),
                new MuxExecutionService()),
            archiveSettingsStore);
        return new ArchiveMaintenanceViewModel(services, new NullDialogService());
    }

    private sealed class StubMkvToolNixLocator : IMkvToolNixLocator
    {
        public string FindMkvMergePath() => @"C:\Tools\mkvmerge.exe";

        public string FindMkvPropEditPath() => @"C:\Tools\mkvpropedit.exe";
    }

    private sealed class NullDialogService : IUserDialogService
    {
        public string? SelectMainVideo(string initialDirectory) => throw new NotSupportedException();

        public string? SelectAudioDescription(string initialDirectory) => throw new NotSupportedException();

        public string[]? SelectSubtitles(string initialDirectory) => throw new NotSupportedException();

        public string[]? SelectAttachments(string initialDirectory) => throw new NotSupportedException();

        public string? SelectOutput(string initialDirectory, string fileName) => throw new NotSupportedException();

        public string? SelectFolder(string title, string initialDirectory) => throw new NotSupportedException();

        public string? SelectExecutable(string title, string filter, string initialDirectory) => throw new NotSupportedException();

        public string? SelectFile(string title, string filter, string initialDirectory) => throw new NotSupportedException();

        public string[]? SelectFiles(string title, string filter, string initialDirectory) => throw new NotSupportedException();

        public MessageBoxResult AskAudioDescriptionChoice() => throw new NotSupportedException();

        public MessageBoxResult AskSubtitlesChoice() => throw new NotSupportedException();

        public MessageBoxResult AskAttachmentChoice() => throw new NotSupportedException();

        public bool ConfirmMuxStart() => throw new NotSupportedException();

        public bool ConfirmBatchExecution(int itemCount, int archiveFileCount, long archiveTotalBytes) => throw new NotSupportedException();

        public bool ConfirmApplyBatchSelectionToAllItems(bool selectItems) => throw new NotSupportedException();

        public bool ConfirmArchiveCopy(FileCopyPlan copyPlan) => throw new NotSupportedException();

        public bool ConfirmSingleEpisodeCleanup(IReadOnlyList<string> usedFiles, IReadOnlyList<string> unusedFiles) => throw new NotSupportedException();

        public bool ConfirmBatchRecycleDoneFiles(int fileCount, string doneDirectory) => throw new NotSupportedException();

        public bool AskOpenDoneDirectory(string doneDirectory) => throw new NotSupportedException();

        public bool ConfirmPlanReview(string episodeTitle, string reviewText) => throw new NotSupportedException();

        public bool TryOpenFilesWithDefaultApp(IEnumerable<string> filePaths) => throw new NotSupportedException();

        public void OpenPathWithDefaultApp(string path) => throw new NotSupportedException();

        public MessageBoxResult AskSourceReviewResult(string fileName, bool canTryAlternative) => throw new NotSupportedException();

        public void ShowInfo(string title, string message) => throw new NotSupportedException();

        public void ShowWarning(string title, string message) => throw new NotSupportedException();

        public void ShowError(string message) => throw new NotSupportedException();
    }
}
