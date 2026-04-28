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
            ContainerTitle: "Folge 2",
            RenameOperation: null,
            ContainerTitleEdit: null,
            TrackHeaderEdits: [],
            TrackHeaderCorrectionCandidates: [],
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

    [Fact]
    public void CreateApplyRequest_UsesManualContainerTitleAndFileName()
    {
        var item = new ArchiveMaintenanceItemViewModel(CreateNeutralAnalysis());

        item.TargetContainerTitle = "Manuell korrigierter Titel";
        item.TargetFileName = "Serie - S01E01 - Manuell korrigierter Titel.mkv";
        var request = item.CreateApplyRequest();

        Assert.Equal("Alter Titel", request.ContainerTitleEdit?.CurrentTitle);
        Assert.Equal("Manuell korrigierter Titel", request.ContainerTitleEdit?.ExpectedTitle);
        Assert.EndsWith(
            "Serie - S01E01 - Manuell korrigierter Titel.mkv",
            request.RenameOperation?.TargetPath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateApplyRequest_UsesManualTrackHeaderTargetValue()
    {
        var item = new ArchiveMaintenanceItemViewModel(CreateAnalysisWithTrackCorrectionCandidate());
        var defaultFlagCorrection = Assert.Single(item.HeaderCorrections);

        defaultFlagCorrection.TargetValue = "ja";
        var request = item.CreateApplyRequest();
        var valueEdit = Assert.Single(Assert.Single(request.TrackHeaderEdits).ValueEdits!);

        Assert.Equal("flag-default", valueEdit.PropertyName);
        Assert.Equal("ja", valueEdit.ExpectedDisplayValue);
        Assert.Equal("1", valueEdit.ExpectedMkvPropEditValue);
    }

    [Fact]
    public void VisibleHeaderCorrections_HidesUnchangedValuesUntilRequested()
    {
        var item = new ArchiveMaintenanceItemViewModel(CreateAnalysisWithTrackCorrectionCandidate());
        var defaultFlagCorrection = Assert.Single(item.HeaderCorrections);

        Assert.Empty(item.VisibleHeaderCorrections);

        item.ShowAllHeaderCorrections = true;
        Assert.Single(item.VisibleHeaderCorrections);

        defaultFlagCorrection.TargetValue = "ja";
        item.ShowAllHeaderCorrections = false;
        Assert.Single(item.VisibleHeaderCorrections);
    }

    [Fact]
    public void InvalidManualFileName_DisablesWritableSelection()
    {
        var item = new ArchiveMaintenanceItemViewModel(CreateWritableAnalysis());
        Assert.True(item.IsSelected);

        item.TargetFileName = "ungueltig.txt";

        Assert.False(item.CanSelect);
        Assert.False(item.IsSelected);
        Assert.Contains(".mkv", item.ManualValidationMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static ArchiveMaintenanceItemAnalysis CreateWritableAnalysis()
    {
        return new ArchiveMaintenanceItemAnalysis(
            @"C:\Archiv\Serie\Season 1\Serie - S01E01 - Pilot.mkv",
            "Pilot",
            ContainerTitle: "Alt",
            RenameOperation: null,
            ContainerTitleEdit: new ContainerTitleEditOperation("Alt", "Pilot"),
            TrackHeaderEdits: [],
            TrackHeaderCorrectionCandidates: [],
            Issues: [],
            ChangeNotes: ["MKV-Titel: Alt -> Pilot"],
            ErrorMessage: null);
    }

    private static ArchiveMaintenanceItemAnalysis CreateNeutralAnalysis()
    {
        return new ArchiveMaintenanceItemAnalysis(
            @"C:\Archiv\Serie\Season 1\Serie - S01E01 - Alter Titel.mkv",
            "Alter Titel",
            ContainerTitle: "Alter Titel",
            RenameOperation: null,
            ContainerTitleEdit: null,
            TrackHeaderEdits: [],
            TrackHeaderCorrectionCandidates: [],
            Issues: [],
            ChangeNotes: [],
            ErrorMessage: null);
    }

    private static ArchiveMaintenanceItemAnalysis CreateAnalysisWithTrackCorrectionCandidate()
    {
        return new ArchiveMaintenanceItemAnalysis(
            @"C:\Archiv\Serie\Season 1\Serie - S01E01 - Pilot.mkv",
            "Pilot",
            ContainerTitle: "Pilot",
            RenameOperation: null,
            ContainerTitleEdit: null,
            TrackHeaderEdits: [],
            TrackHeaderCorrectionCandidates:
            [
                new ArchiveTrackHeaderCorrectionCandidate(
                    "track:1",
                    "Deutsch - AAC",
                    "Deutsch - AAC",
                    [
                        new ArchiveTrackHeaderValueCandidate(
                            "flag-default",
                            "Standard",
                            "nein",
                            "nein",
                            "0",
                            IsFlag: true)
                    ])
            ],
            Issues: [],
            ChangeNotes: [],
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
