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
    public async Task CancelScanCommand_CancelsRunningScan()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "archive-maintenance-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var archiveMaintenance = new PendingArchiveMaintenanceService();
            var viewModel = CreateViewModel(archiveMaintenance: archiveMaintenance);
            viewModel.RootDirectory = tempDirectory;

            var scanTask = viewModel.ScanCommand.ExecuteAsync();
            await archiveMaintenance.WaitUntilScanStartedAsync().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(viewModel.IsScanning);
            Assert.True(viewModel.CancelScanCommand.CanExecute(null));

            viewModel.CancelScanCommand.Execute(null);
            await scanTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(archiveMaintenance.ObservedCancellation);
            Assert.False(viewModel.IsBusy);
            Assert.False(viewModel.IsScanning);
            Assert.Equal("Scan abgebrochen.", viewModel.StatusText);
            Assert.Contains("Scan abgebrochen.", viewModel.LogText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ApplySelectedCommand_SelectsCurrentItemDuringProcessing()
    {
        var archiveMaintenance = new RecordingArchiveMaintenanceService();
        var viewModel = CreateViewModel(archiveMaintenance: archiveMaintenance);
        var firstItem = new ArchiveMaintenanceItemViewModel(CreateWritableAnalysis(@"C:\Archiv\Serie\Season 1\Serie - S01E01 - Pilot.mkv"));
        var secondItem = new ArchiveMaintenanceItemViewModel(CreateWritableAnalysis(@"C:\Archiv\Serie\Season 1\Serie - S01E02 - Folge 2.mkv"));
        viewModel.Items.Add(firstItem);
        viewModel.Items.Add(secondItem);

        await viewModel.ApplySelectedCommand.ExecuteAsync();

        Assert.Equal(
            [
                firstItem.FilePath,
                secondItem.FilePath
            ],
            archiveMaintenance.AppliedFilePaths);
        Assert.Same(secondItem, viewModel.SelectedItem);
    }

    [Fact]
    public async Task ApplySelectedCommand_KeepsRemuxHintVisibleAfterWritableChanges()
    {
        var viewModel = CreateViewModel(archiveMaintenance: new RecordingArchiveMaintenanceService());
        var item = new ArchiveMaintenanceItemViewModel(CreateWritableRemuxAnalysis());
        viewModel.Items.Add(item);

        await viewModel.ApplySelectedCommand.ExecuteAsync();

        Assert.Equal("Remux nötig", item.StatusText);
        Assert.Equal("Warning", item.StatusTone);
        Assert.Contains("Doppelte AD-Spuren", item.ChangeSummary, StringComparison.Ordinal);
        Assert.True(item.HasIssues);
    }

    [Fact]
    public async Task ApplySelectedCommand_LogsReturnedOutputLines_WhenServiceDoesNotReportProgress()
    {
        var archiveMaintenance = new SilentOutputArchiveMaintenanceService(["Header aktualisiert.", "Datei umbenannt."]);
        var viewModel = CreateViewModel(archiveMaintenance: archiveMaintenance);
        viewModel.Items.Add(new ArchiveMaintenanceItemViewModel(CreateWritableAnalysis()));

        await viewModel.ApplySelectedCommand.ExecuteAsync();

        Assert.Contains("Wende Änderungen an 1/1", viewModel.LogText, StringComparison.Ordinal);
        Assert.Contains("Header aktualisiert.", viewModel.LogText, StringComparison.Ordinal);
        Assert.Contains("Datei umbenannt.", viewModel.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScanCommand_SortsItemsByMkvFileName()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "archive-maintenance-sort-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var viewModel = CreateViewModel(archiveMaintenance: new StaticScanArchiveMaintenanceService(
            [
                CreateNeutralAnalysis(@"C:\Archiv\Serie\Season 1\Serie - S01E02 - B.mkv"),
                CreateNeutralAnalysis(@"C:\Archiv\Serie\Season 1\Serie - S01E01 - A.mkv")
            ]));
            viewModel.RootDirectory = tempDirectory;

            await viewModel.ScanCommand.ExecuteAsync();

            Assert.Collection(
                viewModel.Items,
                item => Assert.Equal("Serie - S01E01 - A.mkv", item.FileName),
                item => Assert.Equal("Serie - S01E02 - B.mkv", item.FileName));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ScanCommand_PersistsVisibleProtocol_WhenLoggerIsConfigured()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "archive-maintenance-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var moduleLogs = new RecordingModuleLogService();
            var viewModel = CreateViewModel(
                archiveMaintenance: new StaticScanArchiveMaintenanceService([]),
                moduleLogs: moduleLogs);
            viewModel.RootDirectory = tempDirectory;

            await viewModel.ScanCommand.ExecuteAsync();

            var log = Assert.Single(moduleLogs.Logs);
            Assert.Equal("Archivpflege", log.ModuleLabel);
            Assert.Equal("Scan", log.OperationLabel);
            Assert.Equal(tempDirectory, log.Context);
            Assert.Contains("Scan abgeschlossen", log.LogText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void SuppressSelectedFileNameChangeCommand_PersistsConcreteRejectedSuggestion()
    {
        var viewModel = CreateViewModel();
        var item = new ArchiveMaintenanceItemViewModel(CreateRenameAnalysis());
        viewModel.Items.Add(item);
        viewModel.SelectedItem = item;

        viewModel.SuppressSelectedFileNameChangeCommand.Execute(null);

        Assert.Equal(item.CurrentFileName, item.TargetFileName);
        Assert.True(item.HasSuppressedFileNameChange);
        var archiveSettings = new AppArchiveSettingsStore().Load();
        var suppressedChange = Assert.Single(archiveSettings.SuppressedMaintenanceChanges);
        Assert.Equal(ArchiveMaintenanceItemViewModel.FileNameChangeKind, suppressedChange.ChangeKind);
        Assert.Equal("Serie - S01E01 - Alt.mkv", suppressedChange.CurrentValue);
        Assert.Equal("Serie - S01E01 - Neu.mkv", suppressedChange.SuggestedValue);
    }

    [Fact]
    public void RestoreSelectedFileNameSuggestionCommand_RemovesRejectedSuggestion()
    {
        var viewModel = CreateViewModel();
        var item = new ArchiveMaintenanceItemViewModel(CreateRenameAnalysis());
        viewModel.Items.Add(item);
        viewModel.SelectedItem = item;

        viewModel.SuppressSelectedFileNameChangeCommand.Execute(null);
        viewModel.RestoreSelectedFileNameSuggestionCommand.Execute(null);

        Assert.Equal("Serie - S01E01 - Neu.mkv", item.TargetFileName);
        Assert.False(item.HasSuppressedFileNameChange);
        Assert.Empty(new AppArchiveSettingsStore().Load().SuppressedMaintenanceChanges);
    }

    [Fact]
    public void CreateApplyRequest_IncludesNfoTitleEdits()
    {
        var item = new ArchiveMaintenanceItemViewModel(CreateNeutralAnalysis(
            @"C:\Archiv\Serie\Season 1\Serie - S01E01 - Pilot.mkv",
            nfoTitle: "Alt",
            nfoSortTitle: "Alt Sort"));

        item.TargetNfoTitle = "Neu";
        item.TargetNfoSortTitle = "Neu Sort";
        var request = item.CreateApplyRequest();

        Assert.NotNull(request.NfoTextEdit);
        Assert.Equal("Alt", request.NfoTextEdit!.CurrentTitle);
        Assert.Equal("Neu", request.NfoTextEdit.ExpectedTitle);
        Assert.Equal("Alt Sort", request.NfoTextEdit.CurrentSortTitle);
        Assert.Equal("Neu Sort", request.NfoTextEdit.ExpectedSortTitle);
        Assert.False(request.NfoTextEdit.CurrentTitleLocked);
        Assert.True(request.NfoTextEdit.ExpectedTitleLocked);
        Assert.False(request.NfoTextEdit.CurrentSortTitleLocked);
        Assert.True(request.NfoTextEdit.ExpectedSortTitleLocked);
        Assert.Equal("Gesperrt", item.NfoTitleLockButtonText);
        Assert.Equal("Gesperrt", item.NfoSortTitleLockButtonText);
    }

    [Fact]
    public void ToggleNfoLockCommands_CreateNfoLockOnlyEdit()
    {
        var viewModel = CreateViewModel();
        var item = new ArchiveMaintenanceItemViewModel(CreateNeutralAnalysis(
            @"C:\Archiv\Serie\Season 1\Serie - S01E01 - Pilot.mkv",
            nfoTitle: "Pilot",
            nfoSortTitle: "Pilot"));
        viewModel.Items.Add(item);
        viewModel.SelectedItem = item;

        viewModel.ToggleSelectedNfoTitleLockCommand.Execute(null);
        viewModel.ToggleSelectedNfoSortTitleLockCommand.Execute(null);
        var request = item.CreateApplyRequest();

        Assert.NotNull(request.NfoTextEdit);
        Assert.Equal("Pilot", request.NfoTextEdit!.CurrentTitle);
        Assert.Equal("Pilot", request.NfoTextEdit.ExpectedTitle);
        Assert.False(request.NfoTextEdit.CurrentTitleLocked);
        Assert.True(request.NfoTextEdit.ExpectedTitleLocked);
        Assert.False(request.NfoTextEdit.CurrentSortTitleLocked);
        Assert.True(request.NfoTextEdit.ExpectedSortTitleLocked);
        Assert.Contains("NFO-Titel-Sperre", item.ChangeSummary, StringComparison.Ordinal);
        Assert.Contains("NFO-Sortiertitel-Sperre", item.ChangeSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void NfoTextChange_ReturningToCurrentValue_RestoresOriginalLockState()
    {
        var item = new ArchiveMaintenanceItemViewModel(CreateNeutralAnalysis(
            @"C:\Archiv\Serie\Season 1\Serie - S01E01 - Pilot.mkv",
            nfoTitle: "Pilot",
            nfoSortTitle: "Pilot"));

        item.TargetNfoTitle = "Pilot neu";
        item.TargetNfoSortTitle = "Pilot neu";

        Assert.True(item.TargetNfoTitleLocked);
        Assert.True(item.TargetNfoSortTitleLocked);

        item.TargetNfoTitle = "Pilot";
        item.TargetNfoSortTitle = "Pilot";

        Assert.False(item.TargetNfoTitleLocked);
        Assert.False(item.TargetNfoSortTitleLocked);
        Assert.Null(item.CreateApplyRequest().NfoTextEdit);
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

        Assert.False(item.IsSelected);

        item.TargetContainerTitle = "Manuell korrigierter Titel";
        item.TargetFileName = "Serie - S01E01 - Manuell korrigierter Titel.mkv";
        var request = item.CreateApplyRequest();

        Assert.True(item.CanSelect);
        Assert.True(item.IsSelected);
        Assert.Equal("Alter Titel", request.ContainerTitleEdit?.CurrentTitle);
        Assert.Equal("Manuell korrigierter Titel", request.ContainerTitleEdit?.ExpectedTitle);
        Assert.EndsWith(
            "Serie - S01E01 - Manuell korrigierter Titel.mkv",
            request.RenameOperation?.TargetPath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateApplyRequest_UsesCaseOnlyManualFileNameChange()
    {
        var item = new ArchiveMaintenanceItemViewModel(CreateNeutralAnalysis(
            @"C:\Archiv\Serie\Season 1\serie - s01e01 - pilot.mkv"));

        item.TargetFileName = "Serie - S01E01 - Pilot.mkv";
        var request = item.CreateApplyRequest();

        Assert.True(item.CanSelect);
        Assert.True(item.IsSelected);
        Assert.NotNull(request.RenameOperation);
        Assert.Equal("Serie - S01E01 - Pilot.mkv", Path.GetFileName(request.RenameOperation!.TargetPath), StringComparer.Ordinal);
    }

    [Fact]
    public void ManualValidation_AllowsCaseOnlyRenameWhenSourceFileExists()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "archive-case-validation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var mediaPath = Path.Combine(tempRoot, "serie - s01e01 - pilot.mkv");
            File.WriteAllText(mediaPath, "mkv");
            var item = new ArchiveMaintenanceItemViewModel(CreateNeutralAnalysis(mediaPath));

            item.TargetFileName = "Serie - S01E01 - Pilot.mkv";

            Assert.Null(item.ManualValidationMessage);
            Assert.True(item.CanSelect);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ManualChange_SelectsPreviouslyOkItemUntilUserClearsSelection()
    {
        var item = new ArchiveMaintenanceItemViewModel(CreateNeutralAnalysis());

        item.TargetContainerTitle = "Manuell korrigierter Titel";

        Assert.True(item.IsSelected);

        item.IsSelected = false;

        Assert.False(item.IsSelected);
        Assert.True(item.CanSelect);
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

    private static ArchiveMaintenanceItemAnalysis CreateWritableAnalysis(
        string filePath = @"C:\Archiv\Serie\Season 1\Serie - S01E01 - Pilot.mkv")
    {
        return new ArchiveMaintenanceItemAnalysis(
            filePath,
            Path.GetFileNameWithoutExtension(filePath).Split(" - ").LastOrDefault() ?? "Pilot",
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

    private static ArchiveMaintenanceItemAnalysis CreateWritableRemuxAnalysis()
    {
        return CreateWritableAnalysis() with
        {
            Issues = [new ArchiveMaintenanceIssue(ArchiveMaintenanceIssueKind.RemuxRequired, "Doppelte AD-Spuren.")]
        };
    }

    private static ArchiveMaintenanceItemAnalysis CreateNeutralAnalysis(
        string filePath = @"C:\Archiv\Serie\Season 1\Serie - S01E01 - Alter Titel.mkv",
        string? nfoTitle = null,
        string? nfoSortTitle = null)
    {
        return new ArchiveMaintenanceItemAnalysis(
            filePath,
            Path.GetFileNameWithoutExtension(filePath).Split(" - ").LastOrDefault() ?? "Alter Titel",
            ContainerTitle: Path.GetFileNameWithoutExtension(filePath).Split(" - ").LastOrDefault() ?? "Alter Titel",
            RenameOperation: null,
            ContainerTitleEdit: null,
            TrackHeaderEdits: [],
            TrackHeaderCorrectionCandidates: [],
            ProviderIds: EmbyProviderIds.Empty,
            NfoExists: nfoTitle is not null || nfoSortTitle is not null,
            Issues: [],
            ChangeNotes: [],
            ErrorMessage: null,
            NfoTitle: nfoTitle,
            NfoSortTitle: nfoSortTitle);
    }

    private static ArchiveMaintenanceItemAnalysis CreateRenameAnalysis()
    {
        return new ArchiveMaintenanceItemAnalysis(
            @"C:\Archiv\Serie\Season 1\Serie - S01E01 - Alt.mkv",
            "Alt",
            ContainerTitle: "Alt",
            RenameOperation: new ArchiveRenameOperation(
                @"C:\Archiv\Serie\Season 1\Serie - S01E01 - Alt.mkv",
                @"C:\Archiv\Serie\Season 1\Serie - S01E01 - Neu.mkv",
                Sidecars: []),
            ContainerTitleEdit: null,
            TrackHeaderEdits: [],
            TrackHeaderCorrectionCandidates: [],
            ProviderIds: EmbyProviderIds.Empty,
            NfoExists: false,
            Issues: [],
            ChangeNotes: ["Dateiname: Serie - S01E01 - Alt.mkv -> Serie - S01E01 - Neu.mkv"],
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

    private static ArchiveMaintenanceViewModel CreateViewModel(
        IUserDialogService? dialogService = null,
        IArchiveMaintenanceService? archiveMaintenance = null,
        IModuleLogService? moduleLogs = null)
    {
        var settingsStore = new AppSettingsStore();
        var archiveSettingsStore = new AppArchiveSettingsStore(settingsStore);
        var services = new ArchiveMaintenanceModuleServices(
            archiveMaintenance ?? new ArchiveMaintenanceService(
                new MkvMergeProbeService(),
                new StubMkvToolNixLocator(),
                new MuxExecutionService()),
            archiveSettingsStore,
            new EpisodeMetadataLookupService(new AppMetadataStore(settingsStore), new TvdbClient()),
            new ImdbLookupService(new HttpClient()),
            new NullSettingsDialogService());
        return new ArchiveMaintenanceViewModel(services, dialogService ?? new NullDialogService(), moduleLogs);
    }

    private sealed class PendingArchiveMaintenanceService : IArchiveMaintenanceService
    {
        private readonly TaskCompletionSource _scanStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ObservedCancellation { get; private set; }

        public Task WaitUntilScanStartedAsync() => _scanStarted.Task;

        public async Task<ArchiveMaintenanceScanResult> ScanAsync(
            string rootDirectory,
            IProgress<ArchiveMaintenanceProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            _scanStarted.TrySetResult();
            progress?.Report(new ArchiveMaintenanceProgress("Testscan läuft...", 10));
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ObservedCancellation = true;
                throw;
            }

            return new ArchiveMaintenanceScanResult(rootDirectory, []);
        }

        public Task<ArchiveMaintenanceApplyResult> ApplyAsync(
            ArchiveMaintenanceApplyRequest request,
            IProgress<string>? output = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingArchiveMaintenanceService : IArchiveMaintenanceService
    {
        public List<string> AppliedFilePaths { get; } = [];

        public Task<ArchiveMaintenanceScanResult> ScanAsync(
            string rootDirectory,
            IProgress<ArchiveMaintenanceProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ArchiveMaintenanceApplyResult> ApplyAsync(
            ArchiveMaintenanceApplyRequest request,
            IProgress<string>? output = null,
            CancellationToken cancellationToken = default)
        {
            AppliedFilePaths.Add(request.FilePath);
            return Task.FromResult(new ArchiveMaintenanceApplyResult(
                request.FilePath,
                request.FilePath,
                Success: true,
                Message: "OK",
                OutputLines: []));
        }
    }

    private sealed class SilentOutputArchiveMaintenanceService(IReadOnlyList<string> outputLines) : IArchiveMaintenanceService
    {
        public Task<ArchiveMaintenanceScanResult> ScanAsync(
            string rootDirectory,
            IProgress<ArchiveMaintenanceProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ArchiveMaintenanceApplyResult> ApplyAsync(
            ArchiveMaintenanceApplyRequest request,
            IProgress<string>? output = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ArchiveMaintenanceApplyResult(
                request.FilePath,
                request.FilePath,
                Success: true,
                Message: "OK",
                OutputLines: outputLines));
        }
    }

    private sealed class StaticScanArchiveMaintenanceService : IArchiveMaintenanceService
    {
        private readonly IReadOnlyList<ArchiveMaintenanceItemAnalysis> _items;

        public StaticScanArchiveMaintenanceService(IReadOnlyList<ArchiveMaintenanceItemAnalysis> items)
        {
            _items = items;
        }

        public Task<ArchiveMaintenanceScanResult> ScanAsync(
            string rootDirectory,
            IProgress<ArchiveMaintenanceProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ArchiveMaintenanceScanResult(rootDirectory, _items));
        }

        public Task<ArchiveMaintenanceApplyResult> ApplyAsync(
            ArchiveMaintenanceApplyRequest request,
            IProgress<string>? output = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
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
