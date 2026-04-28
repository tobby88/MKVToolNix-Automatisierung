using System.IO;
using System.Net.Http;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Emby;
using MkvToolnixAutomatisierung.Services.Metadata;
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
    public async Task SelectRootDirectoryCommand_ScansSelectedFolderImmediately()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "archive-maintenance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var viewModel = CreateViewModel(new NullDialogService(tempDirectory));

            await viewModel.SelectRootDirectoryCommand.ExecuteAsync();

            Assert.Equal(tempDirectory, viewModel.RootDirectory);
            Assert.Equal(0, viewModel.TotalCount);
            Assert.Equal("Scan abgeschlossen: 0 Datei(en).", viewModel.StatusText);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
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
            ProviderIds: EmbyProviderIds.Empty,
            NfoExists: false,
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
    public void WritableChangeNotes_ExpandsMultipleTrackHeaderValues()
    {
        var item = new ArchiveMaintenanceItemViewModel(CreateAnalysisWithMultipleTrackCorrections());

        Assert.Equal(2, item.WritableChangeNotes.Count);
        Assert.Contains("HD - H.264 -> Deutsch - HD - H.264", item.WritableChangeNotes);
        Assert.Contains("HD - H.264: Originalsprache: nein -> ja", item.WritableChangeNotes);
        Assert.Equal("Manuelle Korrektur (2 Änderung(en))", item.ManualCorrectionHeaderText);
        Assert.DoesNotContain(item.WritableChangeNotes, note => note.Contains("Name:", StringComparison.Ordinal));
        Assert.Contains("Spurname:", item.ChangeSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateApplyRequest_UsesManualProviderIdTargets()
    {
        var item = new ArchiveMaintenanceItemViewModel(CreateProviderIdAnalysis());

        item.TargetTvdbId = "456";
        item.TargetImdbId = "tt7654321";
        var request = item.CreateApplyRequest();

        Assert.NotNull(request.ProviderIdEdit);
        Assert.Equal("456", request.ProviderIdEdit!.ProviderIds.TvdbId);
        Assert.Equal("tt7654321", request.ProviderIdEdit.ProviderIds.ImdbId);
        Assert.False(request.ProviderIdEdit.RemoveImdbId);
        Assert.Contains("TVDB-ID", item.ChangeSummary, StringComparison.Ordinal);
        Assert.Contains("IMDb-ID", item.ChangeSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkImdbUnavailable_RemovesExistingImdbIdOnApply()
    {
        var item = new ArchiveMaintenanceItemViewModel(CreateProviderIdAnalysis());

        item.MarkImdbUnavailable();
        var request = item.CreateApplyRequest();

        Assert.NotNull(request.ProviderIdEdit);
        Assert.Null(request.ProviderIdEdit!.ProviderIds.ImdbId);
        Assert.True(request.ProviderIdEdit.RemoveImdbId);
        Assert.Contains("keine IMDb-ID", item.ChangeSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearingExistingTvdbId_IsValidationError()
    {
        var item = new ArchiveMaintenanceItemViewModel(CreateProviderIdAnalysis());

        item.TargetTvdbId = string.Empty;

        Assert.Null(item.CreateApplyRequest().ProviderIdEdit);
        Assert.False(item.CanSelect);
        Assert.Contains("TVDB-ID", item.ManualValidationMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void CanReviewImdb_RequiresExistingNfo()
    {
        var withoutNfo = new ArchiveMaintenanceItemViewModel(CreateNeutralAnalysis());
        var withNfo = new ArchiveMaintenanceItemViewModel(CreateProviderIdAnalysis());

        Assert.False(withoutNfo.CanReviewImdb);
        Assert.True(withNfo.CanReviewImdb);
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
            ProviderIds: EmbyProviderIds.Empty,
            NfoExists: false,
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
            ProviderIds: EmbyProviderIds.Empty,
            NfoExists: false,
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
            ProviderIds: EmbyProviderIds.Empty,
            NfoExists: false,
            Issues: [],
            ChangeNotes: [],
            ErrorMessage: null);
    }

    private static ArchiveMaintenanceItemAnalysis CreateAnalysisWithMultipleTrackCorrections()
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
                    "HD - H.264",
                    "HD - H.264",
                    [
                        new ArchiveTrackHeaderValueCandidate(
                            "name",
                            "Name",
                            "HD - H.264",
                            "Deutsch - HD - H.264",
                            "Deutsch - HD - H.264",
                            IsFlag: false),
                        new ArchiveTrackHeaderValueCandidate(
                            "flag-original",
                            "Originalsprache",
                            "nein",
                            "ja",
                            "1",
                            IsFlag: true)
                    ])
            ],
            ProviderIds: EmbyProviderIds.Empty,
            NfoExists: false,
            Issues: [],
            ChangeNotes: [],
            ErrorMessage: null);
    }

    private static ArchiveMaintenanceItemAnalysis CreateProviderIdAnalysis()
    {
        return new ArchiveMaintenanceItemAnalysis(
            @"C:\Archiv\Serie\Season 1\Serie - S01E01 - Pilot.mkv",
            "Pilot",
            ContainerTitle: "Pilot",
            RenameOperation: null,
            ContainerTitleEdit: null,
            TrackHeaderEdits: [],
            TrackHeaderCorrectionCandidates: [],
            ProviderIds: new EmbyProviderIds("123", "tt1234567"),
            NfoExists: true,
            Issues: [],
            ChangeNotes: [],
            ErrorMessage: null);
    }

    private static ArchiveMaintenanceViewModel CreateViewModel(IUserDialogService? dialogService = null)
    {
        var settingsStore = new AppSettingsStore();
        var archiveSettingsStore = new AppArchiveSettingsStore(settingsStore);
        var services = new ArchiveMaintenanceModuleServices(
            new ArchiveMaintenanceService(
                new MkvMergeProbeService(),
                new StubMkvToolNixLocator(),
                new MuxExecutionService()),
            archiveSettingsStore,
            new EpisodeMetadataLookupService(new AppMetadataStore(settingsStore), new TvdbClient()),
            new ImdbLookupService(new HttpClient()),
            new NullSettingsDialogService());
        return new ArchiveMaintenanceViewModel(services, dialogService ?? new NullDialogService());
    }

    private sealed class StubMkvToolNixLocator : IMkvToolNixLocator
    {
        public string FindMkvMergePath() => @"C:\Tools\mkvmerge.exe";

        public string FindMkvPropEditPath() => @"C:\Tools\mkvpropedit.exe";
    }

    private sealed class NullDialogService : IUserDialogService
    {
        private readonly string? _selectedFolder;

        public NullDialogService(string? selectedFolder = null)
        {
            _selectedFolder = selectedFolder;
        }

        public string? SelectMainVideo(string initialDirectory) => throw new NotSupportedException();

        public string? SelectAudioDescription(string initialDirectory) => throw new NotSupportedException();

        public string[]? SelectSubtitles(string initialDirectory) => throw new NotSupportedException();

        public string[]? SelectAttachments(string initialDirectory) => throw new NotSupportedException();

        public string? SelectOutput(string initialDirectory, string fileName) => throw new NotSupportedException();

        public string? SelectFolder(string title, string initialDirectory) => _selectedFolder ?? throw new NotSupportedException();

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

    private sealed class NullSettingsDialogService : IAppSettingsDialogService
    {
        public bool ShowDialog(Window? owner = null, AppSettingsPage initialPage = AppSettingsPage.Archive) => false;
    }
}
