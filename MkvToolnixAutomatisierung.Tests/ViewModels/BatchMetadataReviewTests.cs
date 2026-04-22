using System.IO;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class BatchMetadataReviewTests
{
    [Fact]
    public void CreateFromDetection_ExistingCustomOutputTarget_StaysReady()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "batch-custom-target-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var outputPath = Path.Combine(tempDirectory, "Pilot.mkv");
            File.WriteAllText(outputPath, "existing");

            var item = BatchEpisodeItemViewModel.CreateFromDetection(
                requestedMainVideoPath: @"C:\Temp\episode.mp4",
                CreateLocalGuess(),
                CreateDetectedEpisode(),
                new EpisodeMetadataResolutionResult(
                    CreateLocalGuess(),
                    Selection: null,
                    StatusText: "TVDB-Automatik wurde nicht ausgeführt.",
                    ConfidenceScore: 0,
                    RequiresReview: false,
                    QueryWasAttempted: false,
                    QuerySucceeded: false),
                outputPath: outputPath,
                statusKind: BatchEpisodeStatusKind.Ready,
                isSelected: false,
                isArchiveTargetPath: false);

            item.RefreshArchivePresence();

            Assert.Equal(EpisodeArchiveState.Existing, item.ArchiveState);
            Assert.False(item.HasArchiveComparisonTarget);
            Assert.Equal(BatchEpisodeStatusKind.Ready, item.StatusKind);
            Assert.Contains("überschrieben", item.PlanSummaryText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateFromDetection_DoesNotAutoApprove_WhenTvdbLookupWasSkipped()
    {
        var item = BatchEpisodeItemViewModel.CreateFromDetection(
            requestedMainVideoPath: @"C:\Temp\episode.mp4",
            CreateLocalGuess(),
            CreateDetectedEpisode(),
            new EpisodeMetadataResolutionResult(
                CreateLocalGuess(),
                Selection: null,
                StatusText: "TVDB-Automatik wurde nicht ausgeführt.",
                ConfidenceScore: 0,
                RequiresReview: false,
                QueryWasAttempted: false,
                QuerySucceeded: false),
            outputPath: @"C:\Temp\output.mkv",
            statusKind: BatchEpisodeStatusKind.Ready,
            isSelected: false);

        Assert.False(item.RequiresMetadataReview);
        Assert.False(item.IsMetadataReviewApproved);
    }

    [Fact]
    public void CreateFromDetection_OnlyAudioDescriptionWithoutExistingArchiveTarget_StartsWithWarning()
    {
        var audioDescriptionPath = @"C:\Temp\episode-ad.mp4";
        var detected = new AutoDetectedEpisodeFiles(
            MainVideoPath: audioDescriptionPath,
            AdditionalVideoPaths: [],
            AudioDescriptionPath: audioDescriptionPath,
            SubtitlePaths: [],
            AttachmentPaths: [],
            RelatedFilePaths: [],
            SuggestedOutputFilePath: @"C:\Temp\output.mkv",
            SuggestedTitle: "Pilot",
            SeriesName: "Beispielserie",
            SeasonNumber: "01",
            EpisodeNumber: "02",
            RequiresManualCheck: false,
            ManualCheckFilePaths: [],
            Notes: ["Nur AD erkannt."],
            HasPrimaryVideoSource: false);

        var item = BatchEpisodeItemViewModel.CreateFromDetection(
            requestedMainVideoPath: audioDescriptionPath,
            CreateLocalGuess(),
            detected,
            new EpisodeMetadataResolutionResult(
                CreateLocalGuess(),
                Selection: null,
                StatusText: "TVDB-Automatik wurde nicht ausgeführt.",
                ConfidenceScore: 0,
                RequiresReview: false,
                QueryWasAttempted: false,
                QuerySucceeded: false),
            outputPath: @"C:\Temp\output.mkv",
            statusKind: BatchEpisodeStatusKind.Warning,
            isSelected: true);

        Assert.False(item.HasPrimaryVideoSource);
        Assert.Equal(BatchEpisodeStatusKind.Warning, item.StatusKind);
        Assert.Contains("Zusatzmaterial", item.PlanSummaryText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Zusatzmaterial", item.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateFromDetection_AutoApproves_WhenTvdbLookupSucceededWithoutReview()
    {
        var selection = new TvdbEpisodeSelection(42, "Beispielserie", 100, "Pilot", "01", "02");
        var item = BatchEpisodeItemViewModel.CreateFromDetection(
            requestedMainVideoPath: @"C:\Temp\episode.mp4",
            CreateLocalGuess(),
            CreateDetectedEpisode(),
            new EpisodeMetadataResolutionResult(
                CreateLocalGuess(),
                selection,
                StatusText: "TVDB-Zuordnung automatisch bestätigt.",
                ConfidenceScore: 100,
                RequiresReview: false,
                QueryWasAttempted: true,
                QuerySucceeded: true),
            outputPath: @"C:\Temp\output.mkv",
            statusKind: BatchEpisodeStatusKind.Ready,
            isSelected: false);

        Assert.False(item.RequiresMetadataReview);
        Assert.True(item.IsMetadataReviewApproved);
        Assert.Equal(42, item.TvdbSeriesId);
        Assert.Equal(100, item.TvdbEpisodeId);
    }

    [Fact]
    public async Task ReviewSelectedMetadataCommand_InvokesWorkflow_ForSkippedAutomaticLookup()
    {
        var workflow = new FakeEpisodeReviewWorkflow();
        var viewModel = CreateBatchViewModel(workflow);
        var item = BatchEpisodeItemViewModel.CreateFromDetection(
            requestedMainVideoPath: @"C:\Temp\episode.mp4",
            CreateLocalGuess(),
            CreateDetectedEpisode(),
            new EpisodeMetadataResolutionResult(
                CreateLocalGuess(),
                Selection: null,
                StatusText: "TVDB-Automatik wurde nicht ausgeführt.",
                ConfidenceScore: 0,
                RequiresReview: false,
                QueryWasAttempted: false,
                QuerySucceeded: false),
            outputPath: @"C:\Temp\output.mkv",
            statusKind: BatchEpisodeStatusKind.Ready,
            isSelected: true);

        viewModel.EpisodeItems.Add(item);
        viewModel.SelectedEpisodeItem = item;

        viewModel.ReviewSelectedMetadataCommand.Execute(null);
        await workflow.WaitForMetadataReviewAsync();

        Assert.Equal(1, workflow.MetadataReviewCallCount);
        Assert.Same(item, workflow.LastMetadataItem);
    }

    [Fact]
    public async Task ReviewSelectedMetadataCommand_InvokesWorkflow_ForAlreadyApprovedItem()
    {
        var workflow = new FakeEpisodeReviewWorkflow();
        var viewModel = CreateBatchViewModel(workflow);
        var selection = new TvdbEpisodeSelection(42, "Beispielserie", 100, "Pilot", "01", "02");
        var item = BatchEpisodeItemViewModel.CreateFromDetection(
            requestedMainVideoPath: @"C:\Temp\episode.mp4",
            CreateLocalGuess(),
            CreateDetectedEpisode(),
            new EpisodeMetadataResolutionResult(
                CreateLocalGuess(),
                selection,
                StatusText: "TVDB-Zuordnung automatisch bestätigt.",
                ConfidenceScore: 100,
                RequiresReview: false,
                QueryWasAttempted: true,
                QuerySucceeded: true),
            outputPath: @"C:\Temp\output.mkv",
            statusKind: BatchEpisodeStatusKind.Ready,
            isSelected: true);

        viewModel.EpisodeItems.Add(item);
        viewModel.SelectedEpisodeItem = item;

        viewModel.ReviewSelectedMetadataCommand.Execute(null);
        await workflow.WaitForMetadataReviewAsync();

        Assert.Equal(1, workflow.MetadataReviewCallCount);
        Assert.Same(item, workflow.LastMetadataItem);
    }

    [Fact]
    public void ManualMetadataEdit_ApprovesPendingMetadataReview()
    {
        var item = CreatePendingReviewItem();
        item.ApplyTvdbSelection(new TvdbEpisodeSelection(42, "Beispielserie", 100, "Pilot", "01", "02"));

        item.Title = "Manuell korrigierter Titel";

        Assert.False(item.RequiresMetadataReview);
        Assert.True(item.IsMetadataReviewApproved);
        Assert.Equal("Metadaten manuell angepasst.", item.MetadataStatusText);
        Assert.Null(item.TvdbEpisodeId);
    }

    [Fact]
    public void ApplyTvdbSelection_LeavesPendingReviewState_UntilExplicitApproval()
    {
        var item = CreatePendingReviewItem();

        item.ApplyTvdbSelection(new TvdbEpisodeSelection(42, "Beispielserie", 100, "Pilot", "01", "02"));

        Assert.True(item.RequiresMetadataReview);
        Assert.False(item.IsMetadataReviewApproved);
        Assert.Equal("TVDB-Prüfung erforderlich.", item.MetadataStatusText);
        Assert.Equal(100, item.TvdbEpisodeId);
    }

    [Fact]
    public void ApplyTvdbSelection_ClearsStalePlanReviewHints()
    {
        var item = CreatePendingReviewItem();
        item.SetPlanNotes([
            "In der Bibliothek existiert zusätzlich eine Mehrfachfolge mit demselben Titel (S2014E05-E06). Bitte prüfen, ob die aktuelle Quelle zu einer Doppel- oder Mehrfachfolge gehört."
        ]);

        item.ApplyTvdbSelection(new TvdbEpisodeSelection(42, "Beispielserie", 100, "Mit Pippi Langstrumpf auf der Walz", "01", "04"));

        Assert.False(item.HasPendingPlanReview);
        Assert.False(item.HasActionablePlanNotes);
        Assert.DoesNotContain("Archiv prüfen", item.ReviewHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyTvdbSelection_BumpsComparisonInputVersion()
    {
        var item = CreatePendingReviewItem();
        var before = item.ComparisonInputVersion;

        item.ApplyTvdbSelection(new TvdbEpisodeSelection(42, "Pippi Langstrumpf", 100, "Mit Pippi Langstrumpf auf der Walz", "01", "04"));

        Assert.True(item.ComparisonInputVersion > before);
    }

    [Fact]
    public void SetSubtitles_BumpsComparisonInputVersion()
    {
        var item = CreatePendingReviewItem();
        var before = item.ComparisonInputVersion;

        item.SetSubtitles([@"C:\Temp\episode.ass"]);

        Assert.True(item.ComparisonInputVersion > before);
    }

    [Fact]
    public void RefreshOutputTargetCollisions_ClearsCollisionReview_WhenOutputPathChanges()
    {
        var viewModel = CreateBatchViewModel(new FakeEpisodeReviewWorkflow());
        var firstItem = CreatePendingReviewItem();
        firstItem.SetOutputPathWithContext(
            @"C:\Archive\Pippi Langstrumpf - S01E03 - Pippi auf Sachen-Suche.mkv",
            isArchiveTargetPath: false);
        var secondItem = BatchEpisodeItemViewModel.CreateFromDetection(
            requestedMainVideoPath: @"C:\Temp\episode-2.mp4",
            CreateLocalGuess(),
            CreateDetectedEpisode() with
            {
                MainVideoPath = @"C:\Temp\episode-2.mp4",
                SuggestedOutputFilePath = @"C:\Archive\Pippi Langstrumpf - S01E03 - Pippi auf Sachen-Suche.mkv"
            },
            new EpisodeMetadataResolutionResult(
                CreateLocalGuess(),
                Selection: null,
                StatusText: "TVDB-Prüfung erforderlich.",
                ConfidenceScore: 25,
                RequiresReview: true,
                QueryWasAttempted: true,
                QuerySucceeded: false),
            outputPath: @"C:\Archive\Pippi Langstrumpf - S01E03 - Pippi auf Sachen-Suche.mkv",
            statusKind: BatchEpisodeStatusKind.Ready,
            isSelected: true);

        viewModel.EpisodeItems.Add(firstItem);
        viewModel.EpisodeItems.Add(secondItem);

        viewModel.RefreshOutputTargetCollisions(viewModel.EpisodeItems);

        Assert.True(firstItem.HasPendingPlanReview);
        Assert.True(secondItem.HasPendingPlanReview);

        secondItem.SetOutputPathWithContext(
            @"C:\Archive\Pippi Langstrumpf - S01E04 - Mit Pippi Langstrumpf auf der Walz.mkv",
            isArchiveTargetPath: false);
        viewModel.RefreshOutputTargetCollisions(viewModel.EpisodeItems);

        Assert.False(firstItem.HasPendingPlanReview);
        Assert.False(secondItem.HasPendingPlanReview);
        Assert.DoesNotContain("dieselbe Ausgabedatei", firstItem.NotesDisplayText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dieselbe Ausgabedatei", secondItem.NotesDisplayText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefreshOutputTargetCollisions_UsesFullOutputPath_InsteadOfOnlyFileName()
    {
        var viewModel = CreateBatchViewModel(new FakeEpisodeReviewWorkflow());
        var firstItem = BatchEpisodeItemViewModel.CreateFromDetection(
            requestedMainVideoPath: @"C:\Temp\episode-a.mp4",
            CreateLocalGuess(),
            CreateDetectedEpisode() with
            {
                MainVideoPath = @"C:\Temp\episode-a.mp4",
                SuggestedOutputFilePath = @"C:\ArchiveA\Pilot.mkv"
            },
            new EpisodeMetadataResolutionResult(
                CreateLocalGuess(),
                Selection: null,
                StatusText: "TVDB-Automatik wurde nicht ausgeführt.",
                ConfidenceScore: 0,
                RequiresReview: false,
                QueryWasAttempted: false,
                QuerySucceeded: false),
            outputPath: @"C:\ArchiveA\Pilot.mkv",
            statusKind: BatchEpisodeStatusKind.Ready,
            isSelected: true);
        var secondItem = BatchEpisodeItemViewModel.CreateFromDetection(
            requestedMainVideoPath: @"D:\Temp\episode-b.mp4",
            CreateLocalGuess(),
            CreateDetectedEpisode() with
            {
                MainVideoPath = @"D:\Temp\episode-b.mp4",
                SuggestedOutputFilePath = @"D:\ArchiveB\Pilot.mkv"
            },
            new EpisodeMetadataResolutionResult(
                CreateLocalGuess(),
                Selection: null,
                StatusText: "TVDB-Automatik wurde nicht ausgeführt.",
                ConfidenceScore: 0,
                RequiresReview: false,
                QueryWasAttempted: false,
                QuerySucceeded: false),
            outputPath: @"D:\ArchiveB\Pilot.mkv",
            statusKind: BatchEpisodeStatusKind.Ready,
            isSelected: true);

        viewModel.EpisodeItems.Add(firstItem);
        viewModel.EpisodeItems.Add(secondItem);

        viewModel.RefreshOutputTargetCollisions(viewModel.EpisodeItems);

        Assert.False(firstItem.HasPendingPlanReview);
        Assert.False(secondItem.HasPendingPlanReview);
        Assert.DoesNotContain("dieselbe Ausgabedatei", firstItem.NotesDisplayText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dieselbe Ausgabedatei", secondItem.NotesDisplayText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetPlanNotes_MultipartHint_PromotesBatchReviewState_AndHintText()
    {
        var item = BatchEpisodeItemViewModel.CreateFromDetection(
            requestedMainVideoPath: @"C:\Temp\episode.mp4",
            CreateLocalGuess(),
            CreateDetectedEpisode(),
            new EpisodeMetadataResolutionResult(
                CreateLocalGuess(),
                Selection: null,
                StatusText: "TVDB-Automatik wurde nicht ausgeführt.",
                ConfidenceScore: 0,
                RequiresReview: false,
                QueryWasAttempted: false,
                QuerySucceeded: false),
            outputPath: @"C:\Temp\output.mkv",
            statusKind: BatchEpisodeStatusKind.Ready,
            isSelected: true);

        item.SetPlanNotes([
            "In der Bibliothek existiert zusätzlich eine Mehrfachfolge mit demselben Titel (S2014E05-E06). Bitte prüfen, ob die aktuelle Quelle zu einer Doppel- oder Mehrfachfolge gehört."
        ]);
        item.RefreshArchivePresence();

        Assert.True(item.HasActionablePlanNotes);
        Assert.Equal("Mehrfachfolge prüfen", item.ReviewHint);
        Assert.Contains("Mehrfachfolge", item.ReviewHintTooltip, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(BatchEpisodeStatusKind.ReviewPending, item.StatusKind);

        item.ApprovePlanReview();
        item.RefreshArchivePresence();

        Assert.False(item.HasPendingPlanReview);
        Assert.False(item.HasActionablePlanNotes);
        Assert.Equal("Keine nötig", item.ReviewHint);
        Assert.Equal(BatchEpisodeStatusKind.Ready, item.StatusKind);
    }

    [Fact]
    public void ReviewPendingSourcesCommand_ApprovesPendingPlanReviewHints()
    {
        var dialogService = new FakeDialogService();
        var viewModel = CreateBatchViewModel(new FakeEpisodeReviewWorkflow(), dialogService);
        var item = BatchEpisodeItemViewModel.CreateFromDetection(
            requestedMainVideoPath: @"C:\Temp\episode.mp4",
            CreateLocalGuess(),
            CreateDetectedEpisode(),
            new EpisodeMetadataResolutionResult(
                CreateLocalGuess(),
                Selection: null,
                StatusText: "TVDB-Automatik wurde nicht ausgeführt.",
                ConfidenceScore: 0,
                RequiresReview: false,
                QueryWasAttempted: false,
                QuerySucceeded: false),
            outputPath: @"C:\Temp\output.mkv",
            statusKind: BatchEpisodeStatusKind.Ready,
            isSelected: true);
        item.SetPlanNotes([
            "In der Bibliothek existiert zusätzlich eine Mehrfachfolge mit demselben Titel (S2014E05-E06). Bitte prüfen, ob die aktuelle Quelle zu einer Doppel- oder Mehrfachfolge gehört."
        ]);
        item.RefreshArchivePresence();
        viewModel.EpisodeItems.Add(item);
        viewModel.SelectedEpisodeItem = item;

        viewModel.ReviewPendingSourcesCommand.Execute(null);

        Assert.Equal(1, dialogService.ConfirmPlanReviewCallCount);
        Assert.False(item.HasPendingPlanReview);
        Assert.Equal(BatchEpisodeStatusKind.Ready, item.StatusKind);
    }

    [Fact]
    public async Task ReviewPendingSourcesCommand_SourceReviewIntroducesMetadataReview_ProcessesMetadataInSameRun()
    {
        var workflow = new SourceChangingEpisodeReviewWorkflow();
        var viewModel = CreateBatchViewModel(workflow);
        var item = CreateManualCheckItem();
        viewModel.EpisodeItems.Add(item);
        viewModel.SelectedEpisodeItem = item;

        viewModel.ReviewPendingSourcesCommand.Execute(null);
        await workflow.WaitForMetadataReviewAsync();

        Assert.Equal(1, workflow.ManualSourceReviewCallCount);
        Assert.Equal(1, workflow.MetadataReviewCallCount);
        Assert.False(item.RequiresManualCheck);
        Assert.False(item.RequiresMetadataReview);
        Assert.True(item.IsMetadataReviewApproved);
    }

    [Fact]
    public void SelectAllEpisodesCommand_WithActiveFilter_AsksWhetherHiddenItemsShouldBeIncluded()
    {
        var dialogService = new FakeDialogService
        {
            ConfirmApplyBatchSelectionToAllItemsResult = false
        };
        var viewModel = CreateBatchViewModel(new FakeEpisodeReviewWorkflow(), dialogService);
        var pendingItem = BatchEpisodeItemViewModel.CreateFromDetection(
            requestedMainVideoPath: @"C:\Temp\pending.mp4",
            CreateLocalGuess(),
            CreateDetectedEpisode() with { MainVideoPath = @"C:\Temp\pending.mp4" },
            new EpisodeMetadataResolutionResult(
                CreateLocalGuess(),
                Selection: null,
                StatusText: "TVDB-Automatik wurde nicht ausgeführt.",
                ConfidenceScore: 0,
                RequiresReview: false,
                QueryWasAttempted: false,
                QuerySucceeded: false),
            outputPath: @"C:\Temp\pending.mkv",
            statusKind: BatchEpisodeStatusKind.Ready,
            isSelected: false);
        var hiddenReadyItem = BatchEpisodeItemViewModel.CreateFromDetection(
            requestedMainVideoPath: @"C:\Temp\ready.mp4",
            CreateLocalGuess(),
            CreateDetectedEpisode() with { MainVideoPath = @"C:\Temp\ready.mp4" },
            new EpisodeMetadataResolutionResult(
                CreateLocalGuess(),
                Selection: new TvdbEpisodeSelection(42, "Beispielserie", 100, "Pilot", "01", "02"),
                StatusText: "TVDB-Zuordnung automatisch bestätigt.",
                ConfidenceScore: 100,
                RequiresReview: false,
                QueryWasAttempted: true,
                QuerySucceeded: true),
            outputPath: @"C:\Temp\ready.mkv",
            statusKind: BatchEpisodeStatusKind.Ready,
            isSelected: false);

        pendingItem.SetPlanNotes([
            "In der Bibliothek existiert zusätzlich eine Mehrfachfolge mit demselben Titel (S2014E05-E06). Bitte prüfen, ob die aktuelle Quelle zu einer Doppel- oder Mehrfachfolge gehört."
        ]);
        viewModel.EpisodeItems.Add(pendingItem);
        viewModel.EpisodeItems.Add(hiddenReadyItem);
        viewModel.SelectedFilterMode = viewModel.FilterModes.Single(mode => mode.Key == BatchEpisodeFilterMode.PendingChecks);

        viewModel.SelectAllEpisodesCommand.Execute(null);

        Assert.Equal(1, dialogService.ConfirmApplyBatchSelectionToAllItemsCallCount);
        Assert.True(pendingItem.IsSelected);
        Assert.False(hiddenReadyItem.IsSelected);
        Assert.Contains("Gefilterte Episoden", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectAllEpisodesCommand_WithActiveFilter_RemainsReachable_WhenOnlyHiddenItemsAreUnselected()
    {
        var dialogService = new FakeDialogService
        {
            ConfirmApplyBatchSelectionToAllItemsResult = true
        };
        var viewModel = CreateBatchViewModel(new FakeEpisodeReviewWorkflow(), dialogService);
        var visiblePendingItem = CreatePendingReviewItem();
        visiblePendingItem.IsSelected = true;
        var hiddenReadyItem = CreateReadyItem(@"C:\Temp\ready-hidden.mp4", @"C:\Temp\ready-hidden.mkv");
        hiddenReadyItem.IsSelected = false;

        viewModel.EpisodeItems.Add(visiblePendingItem);
        viewModel.EpisodeItems.Add(hiddenReadyItem);
        viewModel.SelectedFilterMode = viewModel.FilterModes.Single(mode => mode.Key == BatchEpisodeFilterMode.PendingChecks);

        Assert.True(viewModel.SelectAllEpisodesCommand.CanExecute(null));

        viewModel.SelectAllEpisodesCommand.Execute(null);

        Assert.Equal(1, dialogService.ConfirmApplyBatchSelectionToAllItemsCallCount);
        Assert.True(hiddenReadyItem.IsSelected);
    }

    [Fact]
    public void DeselectAllEpisodesCommand_WithActiveFilter_RemainsReachable_WhenOnlyHiddenItemsAreSelected()
    {
        var dialogService = new FakeDialogService
        {
            ConfirmApplyBatchSelectionToAllItemsResult = true
        };
        var viewModel = CreateBatchViewModel(new FakeEpisodeReviewWorkflow(), dialogService);
        var visiblePendingItem = CreatePendingReviewItem();
        visiblePendingItem.IsSelected = false;
        var hiddenReadyItem = CreateReadyItem(@"C:\Temp\selected-hidden.mp4", @"C:\Temp\selected-hidden.mkv");
        hiddenReadyItem.IsSelected = true;

        viewModel.EpisodeItems.Add(visiblePendingItem);
        viewModel.EpisodeItems.Add(hiddenReadyItem);
        viewModel.SelectedFilterMode = viewModel.FilterModes.Single(mode => mode.Key == BatchEpisodeFilterMode.PendingChecks);

        Assert.True(viewModel.DeselectAllEpisodesCommand.CanExecute(null));

        viewModel.DeselectAllEpisodesCommand.Execute(null);

        Assert.Equal(1, dialogService.ConfirmApplyBatchSelectionToAllItemsCallCount);
        Assert.False(hiddenReadyItem.IsSelected);
    }

    [Fact]
    public void ToggleSelectedEpisodeSelectionCommand_TogglesCurrentBatchItem()
    {
        var viewModel = CreateBatchViewModel(new FakeEpisodeReviewWorkflow());
        var item = BatchEpisodeItemViewModel.CreateFromDetection(
            requestedMainVideoPath: @"C:\Temp\episode.mp4",
            CreateLocalGuess(),
            CreateDetectedEpisode(),
            new EpisodeMetadataResolutionResult(
                CreateLocalGuess(),
                Selection: null,
                StatusText: "TVDB-Automatik wurde nicht ausgeführt.",
                ConfidenceScore: 0,
                RequiresReview: false,
                QueryWasAttempted: false,
                QuerySucceeded: false),
            outputPath: @"C:\Temp\output.mkv",
            statusKind: BatchEpisodeStatusKind.Ready,
            isSelected: false);

        viewModel.EpisodeItems.Add(item);
        viewModel.SelectedEpisodeItem = item;

        Assert.True(viewModel.ToggleSelectedEpisodeSelectionCommand.CanExecute(null));

        viewModel.ToggleSelectedEpisodeSelectionCommand.Execute(null);

        Assert.True(item.IsSelected);
        Assert.Same(item, viewModel.SelectedEpisodeItem);
    }

    [Fact]
    public void ToggleSelectedEpisodeSelectionCommand_DoesNotInvalidateItself()
    {
        var viewModel = CreateBatchViewModel(new FakeEpisodeReviewWorkflow());
        var item = BatchEpisodeItemViewModel.CreateFromDetection(
            requestedMainVideoPath: @"C:\Temp\episode.mp4",
            CreateLocalGuess(),
            CreateDetectedEpisode(),
            new EpisodeMetadataResolutionResult(
                CreateLocalGuess(),
                Selection: null,
                StatusText: "TVDB-Automatik wurde nicht ausgeführt.",
                ConfidenceScore: 0,
                RequiresReview: false,
                QueryWasAttempted: false,
                QuerySucceeded: false),
            outputPath: @"C:\Temp\output.mkv",
            statusKind: BatchEpisodeStatusKind.Ready,
            isSelected: false);

        viewModel.EpisodeItems.Add(item);
        viewModel.SelectedEpisodeItem = item;
        var canExecuteRefreshCount = 0;
        viewModel.ToggleSelectedEpisodeSelectionCommand.CanExecuteChanged += (_, _) => canExecuteRefreshCount++;

        viewModel.ToggleSelectedEpisodeSelectionCommand.Execute(null);

        Assert.True(item.IsSelected);
        Assert.Equal(0, canExecuteRefreshCount);
    }

    [Fact]
    public void EditSelectedOutputCommand_UsesNearestExistingOutputParentDirectory()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "batch-output-dialog-tests", Guid.NewGuid().ToString("N"));
        var seriesDirectory = Path.Combine(tempDirectory, "Beispielserie");
        Directory.CreateDirectory(seriesDirectory);
        try
        {
            var dialogService = new FakeDialogService();
            var viewModel = CreateBatchViewModel(new FakeEpisodeReviewWorkflow(), dialogService);
            var outputPath = Path.Combine(
                seriesDirectory,
                "Season 2014",
                "Beispielserie - S2014E05-E06 - Rififi.mkv");
            var item = BatchEpisodeItemViewModel.CreateFromDetection(
                requestedMainVideoPath: @"C:\Temp\episode.mp4",
                CreateLocalGuess(),
                CreateDetectedEpisode(),
                new EpisodeMetadataResolutionResult(
                    CreateLocalGuess(),
                    Selection: null,
                    StatusText: "TVDB-Automatik wurde nicht ausgeführt.",
                    ConfidenceScore: 0,
                    RequiresReview: false,
                    QueryWasAttempted: false,
                    QuerySucceeded: false),
                outputPath: outputPath,
                statusKind: BatchEpisodeStatusKind.Ready,
                isSelected: true,
                isArchiveTargetPath: true);

            viewModel.EpisodeItems.Add(item);
            viewModel.SelectedEpisodeItem = item;

            viewModel.EditSelectedOutputCommand.Execute(null);

            Assert.Equal(seriesDirectory, dialogService.LastOutputInitialDirectory);
            Assert.Equal(Path.GetFileName(outputPath), dialogService.LastOutputFileName);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void HandleArchiveConfigurationChanged_ReclassifiesManualOutputPath_WhenArchiveRootStartsCoveringIt()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "batch-archive-root-tests", Guid.NewGuid().ToString("N"));
        var oldArchiveRoot = Path.Combine(tempDirectory, "Archive-A");
        var newArchiveRoot = Path.Combine(tempDirectory, "Archive-B");
        Directory.CreateDirectory(oldArchiveRoot);
        Directory.CreateDirectory(newArchiveRoot);
        try
        {
            var outputPath = Path.Combine(newArchiveRoot, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, "existing");

            var viewModel = CreateBatchViewModel(new FakeEpisodeReviewWorkflow());
            GetBatchServices(viewModel).Archive.ConfigureArchiveRootDirectory(oldArchiveRoot);
            var item = CreateReadyItem(@"C:\Temp\manual-archive.mp4", outputPath);
            item.SetOutputPathWithContext(outputPath, isArchiveTargetPath: false);
            item.RefreshArchivePresence();
            viewModel.EpisodeItems.Add(item);

            Assert.False(item.HasArchiveComparisonTarget);
            Assert.Equal(BatchEpisodeStatusKind.Ready, item.StatusKind);

            GetBatchServices(viewModel).Archive.ConfigureArchiveRootDirectory(newArchiveRoot);

            viewModel.HandleArchiveConfigurationChanged();

            Assert.True(item.HasArchiveComparisonTarget);
            Assert.Equal(BatchEpisodeStatusKind.ComparisonPending, item.StatusKind);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void HandleArchiveConfigurationChanged_ReclassifiesManualOutputPath_WhenArchiveRootStopsCoveringIt()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "batch-archive-root-tests", Guid.NewGuid().ToString("N"));
        var oldArchiveRoot = Path.Combine(tempDirectory, "Archive-A");
        var newArchiveRoot = Path.Combine(tempDirectory, "Archive-B");
        Directory.CreateDirectory(oldArchiveRoot);
        Directory.CreateDirectory(newArchiveRoot);
        try
        {
            var outputPath = Path.Combine(oldArchiveRoot, "Beispielserie", "Season 1", "Beispielserie - S01E02 - Pilot.mkv");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, "existing");

            var viewModel = CreateBatchViewModel(new FakeEpisodeReviewWorkflow());
            GetBatchServices(viewModel).Archive.ConfigureArchiveRootDirectory(oldArchiveRoot);
            var item = CreateReadyItem(@"C:\Temp\manual-unarchive.mp4", outputPath);
            item.SetOutputPathWithContext(outputPath, isArchiveTargetPath: true);
            item.RefreshArchivePresence();
            viewModel.EpisodeItems.Add(item);

            Assert.True(item.HasArchiveComparisonTarget);
            Assert.Equal(BatchEpisodeStatusKind.ComparisonPending, item.StatusKind);

            GetBatchServices(viewModel).Archive.ConfigureArchiveRootDirectory(newArchiveRoot);

            viewModel.HandleArchiveConfigurationChanged();

            Assert.False(item.HasArchiveComparisonTarget);
            Assert.Equal(BatchEpisodeStatusKind.Ready, item.StatusKind);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FreezeSelectedItemPlanSummaryForExecution_CancelsPendingSelectionRefresh()
    {
        var viewModel = CreateBatchViewModel(new FakeEpisodeReviewWorkflow());
        var item = BatchEpisodeItemViewModel.CreateFromDetection(
            requestedMainVideoPath: @"C:\Temp\episode.mp4",
            CreateLocalGuess(),
            CreateDetectedEpisode(),
            new EpisodeMetadataResolutionResult(
                CreateLocalGuess(),
                Selection: null,
                StatusText: "TVDB-Automatik wurde nicht ausgeführt.",
                ConfidenceScore: 0,
                RequiresReview: false,
                QueryWasAttempted: false,
                QuerySucceeded: false),
            outputPath: @"C:\Temp\output.mkv",
            statusKind: BatchEpisodeStatusKind.Ready,
            isSelected: true);

        item.SetPlanSummary("Stabile Batch-Details");
        item.SetUsageSummary(EpisodeUsageSummary.CreatePending("Stabil", "Bleibt waehrend des Batch-Laufs sichtbar"));
        viewModel.SelectedEpisodeItem = item;
        var pendingRefresh = viewModel.SelectedItemPlanSummaryRefreshTask;

        viewModel.FreezeSelectedItemPlanSummaryForExecution();
        if (pendingRefresh is not null)
        {
            await pendingRefresh;
        }

        Assert.Equal("Stabile Batch-Details", item.PlanSummaryText);
        Assert.Equal("Stabil", item.UsageSummary?.ArchiveAction);
        Assert.Equal("Bleibt waehrend des Batch-Laufs sichtbar", item.UsageSummary?.ArchiveDetails);
    }

    [Fact]
    public async Task UnfreezeSelectedItemPlanSummaryAfterExecution_ReschedulesRefreshForSelectedItem()
    {
        var viewModel = CreateBatchViewModel(new FakeEpisodeReviewWorkflow());
        var item = CreateReadyItem(@"C:\Temp\episode-refresh.mp4", @"C:\Temp\episode-refresh.mkv");
        viewModel.SelectedEpisodeItem = item;

        var pendingRefresh = viewModel.SelectedItemPlanSummaryRefreshTask;
        viewModel.FreezeSelectedItemPlanSummaryForExecution();
        if (pendingRefresh is not null)
        {
            await pendingRefresh;
        }

        viewModel.UnfreezeSelectedItemPlanSummaryAfterExecution();

        var resumedRefresh = viewModel.SelectedItemPlanSummaryRefreshTask;
        Assert.NotNull(resumedRefresh);
        await resumedRefresh!;
        Assert.Null(viewModel.SelectedItemPlanSummaryRefreshTask);
    }

    [Fact]
    public async Task ReviewSelectedMetadataCommand_MetadataChange_CancelsPendingSelectionRefresh()
    {
        var workflow = new MetadataChangingEpisodeReviewWorkflow();
        var viewModel = CreateBatchViewModel(workflow);
        var item = CreatePendingReviewItem();
        viewModel.EpisodeItems.Add(item);
        viewModel.SelectedEpisodeItem = item;

        var pendingRefresh = viewModel.SelectedItemPlanSummaryRefreshTask;

        viewModel.ReviewSelectedMetadataCommand.Execute(null);
        await workflow.WaitForMetadataReviewAsync();
        if (pendingRefresh is not null)
        {
            await pendingRefresh;
        }

        Assert.Null(viewModel.SelectedItemPlanSummaryRefreshTask);
        Assert.Equal("Mit Pippi Langstrumpf auf der Walz", item.Title);
        Assert.Equal("01", item.SeasonNumber);
        Assert.Equal("04", item.EpisodeNumber);
    }

    [Fact]
    public void ResetCompletedBatchSession_ClearsEpisodeItems_AndSelection()
    {
        var viewModel = CreateBatchViewModel(new FakeEpisodeReviewWorkflow());
        var item = BatchEpisodeItemViewModel.CreateFromDetection(
            requestedMainVideoPath: @"C:\Temp\episode.mp4",
            CreateLocalGuess(),
            CreateDetectedEpisode(),
            new EpisodeMetadataResolutionResult(
                CreateLocalGuess(),
                Selection: null,
                StatusText: "TVDB-Automatik wurde nicht ausgeführt.",
                ConfidenceScore: 0,
                RequiresReview: false,
                QueryWasAttempted: false,
                QuerySucceeded: false),
            outputPath: @"C:\Temp\output.mkv",
            statusKind: BatchEpisodeStatusKind.Ready,
            isSelected: true);

        viewModel.EpisodeItems.Add(item);
        viewModel.SelectedEpisodeItem = item;

        viewModel.ResetCompletedBatchSession();

        Assert.Empty(viewModel.EpisodeItems);
        Assert.Null(viewModel.SelectedEpisodeItem);
    }

    private static BatchMuxViewModel CreateBatchViewModel(
        IEpisodeReviewWorkflow reviewWorkflow,
        FakeDialogService? dialogService = null)
    {
        return new BatchMuxViewModel(
            ViewModelTestContext.CreateBatchServices(),
            dialogService ?? new FakeDialogService(),
            reviewWorkflow: reviewWorkflow);
    }

    private static BatchEpisodeItemViewModel CreatePendingReviewItem()
    {
        return BatchEpisodeItemViewModel.CreateFromDetection(
            requestedMainVideoPath: @"C:\Temp\episode.mp4",
            CreateLocalGuess(),
            CreateDetectedEpisode(),
            new EpisodeMetadataResolutionResult(
                CreateLocalGuess(),
                Selection: null,
                StatusText: "TVDB-Prüfung erforderlich.",
                ConfidenceScore: 25,
                RequiresReview: true,
                QueryWasAttempted: true,
                QuerySucceeded: false),
            outputPath: @"C:\Temp\output.mkv",
            statusKind: BatchEpisodeStatusKind.Ready,
            isSelected: true);
    }

    private static BatchEpisodeItemViewModel CreateManualCheckItem()
    {
        return BatchEpisodeItemViewModel.CreateFromDetection(
            requestedMainVideoPath: @"C:\Temp\manual-check.mp4",
            CreateLocalGuess(),
            CreateDetectedEpisode() with
            {
                MainVideoPath = @"C:\Temp\manual-check.mp4",
                RequiresManualCheck = true,
                ManualCheckFilePaths = [@"C:\Temp\manual-check.mp4"]
            },
            new EpisodeMetadataResolutionResult(
                CreateLocalGuess(),
                Selection: null,
                StatusText: "TVDB-Automatik wurde nicht ausgeführt.",
                ConfidenceScore: 0,
                RequiresReview: false,
                QueryWasAttempted: false,
                QuerySucceeded: false),
            outputPath: @"C:\Temp\manual-check.mkv",
            statusKind: BatchEpisodeStatusKind.Ready,
            isSelected: true);
    }

    private static BatchEpisodeItemViewModel CreateReadyItem(string requestedMainVideoPath, string outputPath)
    {
        return BatchEpisodeItemViewModel.CreateFromDetection(
            requestedMainVideoPath: requestedMainVideoPath,
            CreateLocalGuess(),
            CreateDetectedEpisode() with
            {
                MainVideoPath = requestedMainVideoPath,
                SuggestedOutputFilePath = outputPath
            },
            new EpisodeMetadataResolutionResult(
                CreateLocalGuess(),
                Selection: new TvdbEpisodeSelection(42, "Beispielserie", 100, "Pilot", "01", "02"),
                StatusText: "TVDB-Zuordnung automatisch bestätigt.",
                ConfidenceScore: 100,
                RequiresReview: false,
                QueryWasAttempted: true,
                QuerySucceeded: true),
            outputPath: outputPath,
            statusKind: BatchEpisodeStatusKind.Ready,
            isSelected: true);
    }

    private static EpisodeMetadataGuess CreateLocalGuess()
    {
        return new EpisodeMetadataGuess("Beispielserie", "Pilot", "01", "02");
    }

    private static AutoDetectedEpisodeFiles CreateDetectedEpisode()
    {
        return new AutoDetectedEpisodeFiles(
            MainVideoPath: @"C:\Temp\episode.mp4",
            AdditionalVideoPaths: [],
            AudioDescriptionPath: null,
            SubtitlePaths: [],
            AttachmentPaths: [],
            RelatedFilePaths: [],
            SuggestedOutputFilePath: @"C:\Temp\output.mkv",
            SuggestedTitle: "Pilot",
            SeriesName: "Beispielserie",
            SeasonNumber: "01",
            EpisodeNumber: "02",
            RequiresManualCheck: false,
            ManualCheckFilePaths: [],
            Notes: []);
    }

    private sealed class FakeEpisodeReviewWorkflow : IEpisodeReviewWorkflow
    {
        private readonly TaskCompletionSource _metadataReviewCompletion = new();

        public int MetadataReviewCallCount { get; private set; }

        public IEpisodeReviewItem? LastMetadataItem { get; private set; }

        public Task<bool> ReviewManualSourceAsync(
            IEpisodeReviewItem item,
            Action<string, int> reportStatus,
            int currentProgress,
            string reviewStatusText,
            string cancelledStatusText,
            string openFailedStatusText,
            string approvedStatusText,
            string alternativeStatusText,
            Func<IReadOnlyCollection<string>, Task<bool>> tryAlternativeAsync)
        {
            throw new NotSupportedException();
        }

        public Task<EpisodeMetadataReviewOutcome> ReviewMetadataAsync(
            IEpisodeReviewItem item,
            Action<string, int> reportStatus,
            int currentProgress,
            string reviewStatusText,
            string cancelledStatusText,
            string localApprovedStatusText,
            string tvdbApprovedStatusText,
            Action onEpisodeChanged)
        {
            MetadataReviewCallCount++;
            LastMetadataItem = item;
            _metadataReviewCompletion.TrySetResult();
            return Task.FromResult(EpisodeMetadataReviewOutcome.Cancelled);
        }

        public async Task WaitForMetadataReviewAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _metadataReviewCompletion.Task.WaitAsync(timeout.Token);
        }
    }

    private sealed class MetadataChangingEpisodeReviewWorkflow : IEpisodeReviewWorkflow
    {
        private readonly TaskCompletionSource _metadataReviewCompletion = new();

        public Task<bool> ReviewManualSourceAsync(
            IEpisodeReviewItem item,
            Action<string, int> reportStatus,
            int currentProgress,
            string reviewStatusText,
            string cancelledStatusText,
            string openFailedStatusText,
            string approvedStatusText,
            string alternativeStatusText,
            Func<IReadOnlyCollection<string>, Task<bool>> tryAlternativeAsync)
        {
            throw new NotSupportedException();
        }

        public Task<EpisodeMetadataReviewOutcome> ReviewMetadataAsync(
            IEpisodeReviewItem item,
            Action<string, int> reportStatus,
            int currentProgress,
            string reviewStatusText,
            string cancelledStatusText,
            string localApprovedStatusText,
            string tvdbApprovedStatusText,
            Action onEpisodeChanged)
        {
            item.ApplyTvdbSelection(new TvdbEpisodeSelection(42, "Pippi Langstrumpf", 100, "Mit Pippi Langstrumpf auf der Walz", "01", "04"));
            onEpisodeChanged();
            item.ApproveMetadataReview("TVDB manuell bestätigt: S01E04 - Mit Pippi Langstrumpf auf der Walz");
            _metadataReviewCompletion.TrySetResult();
            return Task.FromResult(EpisodeMetadataReviewOutcome.AppliedTvdbSelection);
        }

        public async Task WaitForMetadataReviewAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _metadataReviewCompletion.Task.WaitAsync(timeout.Token);
        }
    }

    private sealed class SourceChangingEpisodeReviewWorkflow : IEpisodeReviewWorkflow
    {
        private readonly TaskCompletionSource _metadataReviewCompletion = new();

        public int ManualSourceReviewCallCount { get; private set; }

        public int MetadataReviewCallCount { get; private set; }

        public Task<bool> ReviewManualSourceAsync(
            IEpisodeReviewItem item,
            Action<string, int> reportStatus,
            int currentProgress,
            string reviewStatusText,
            string cancelledStatusText,
            string openFailedStatusText,
            string approvedStatusText,
            string alternativeStatusText,
            Func<IReadOnlyCollection<string>, Task<bool>> tryAlternativeAsync)
        {
            ManualSourceReviewCallCount++;
            var batchItem = Assert.IsType<BatchEpisodeItemViewModel>(item);
            batchItem.ApplyDetection(
                requestedMainVideoPath: batchItem.RequestedMainVideoPath,
                localGuess: CreateLocalGuess(),
                detected: CreateDetectedEpisode() with
                {
                    MainVideoPath = batchItem.RequestedMainVideoPath,
                    RequiresManualCheck = false,
                    ManualCheckFilePaths = []
                },
                metadataResolution: new EpisodeMetadataResolutionResult(
                    CreateLocalGuess(),
                    Selection: null,
                    StatusText: "TVDB-Prüfung erforderlich.",
                    ConfidenceScore: 25,
                    RequiresReview: true,
                    QueryWasAttempted: true,
                    QuerySucceeded: false),
                outputPath: batchItem.OutputPath,
                statusKind: BatchEpisodeStatusKind.Ready,
                isArchiveTargetPath: false);
            return Task.FromResult(true);
        }

        public Task<EpisodeMetadataReviewOutcome> ReviewMetadataAsync(
            IEpisodeReviewItem item,
            Action<string, int> reportStatus,
            int currentProgress,
            string reviewStatusText,
            string cancelledStatusText,
            string localApprovedStatusText,
            string tvdbApprovedStatusText,
            Action onEpisodeChanged)
        {
            MetadataReviewCallCount++;
            item.ApplyTvdbSelection(new TvdbEpisodeSelection(42, "Beispielserie", 100, "Pilot", "01", "02"));
            onEpisodeChanged();
            item.ApproveMetadataReview("TVDB manuell bestätigt.");
            _metadataReviewCompletion.TrySetResult();
            return Task.FromResult(EpisodeMetadataReviewOutcome.AppliedTvdbSelection);
        }

        public async Task WaitForMetadataReviewAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _metadataReviewCompletion.Task.WaitAsync(timeout.Token);
        }
    }

    private static BatchModuleServices GetBatchServices(BatchMuxViewModel viewModel)
    {
        var field = typeof(BatchMuxViewModel).GetField("_services", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return Assert.IsType<BatchModuleServices>(field?.GetValue(viewModel));
    }

    private sealed class FakeDialogService : IUserDialogService
    {
        public int ConfirmPlanReviewCallCount { get; private set; }

        public bool ConfirmPlanReviewResult { get; init; } = true;

        public int ConfirmApplyBatchSelectionToAllItemsCallCount { get; private set; }

        public bool ConfirmApplyBatchSelectionToAllItemsResult { get; init; }

        public string? LastOutputInitialDirectory { get; private set; }

        public string? LastOutputFileName { get; private set; }

        public string? SelectedOutputPath { get; init; }

        public string? SelectMainVideo(string initialDirectory) => throw new NotSupportedException();
        public string? SelectAudioDescription(string initialDirectory) => throw new NotSupportedException();
        public string[]? SelectSubtitles(string initialDirectory) => throw new NotSupportedException();
        public string[]? SelectAttachments(string initialDirectory) => throw new NotSupportedException();
        public string? SelectOutput(string initialDirectory, string fileName)
        {
            LastOutputInitialDirectory = initialDirectory;
            LastOutputFileName = fileName;
            return SelectedOutputPath;
        }
        public string? SelectFolder(string title, string initialDirectory) => throw new NotSupportedException();
        public string? SelectExecutable(string title, string filter, string initialDirectory) => throw new NotSupportedException();
        public string? SelectFile(string title, string filter, string initialDirectory) => throw new NotSupportedException();
        public string[]? SelectFiles(string title, string filter, string initialDirectory) => throw new NotSupportedException();
        public MessageBoxResult AskAudioDescriptionChoice() => throw new NotSupportedException();
        public MessageBoxResult AskSubtitlesChoice() => throw new NotSupportedException();
        public MessageBoxResult AskAttachmentChoice() => throw new NotSupportedException();
        public bool ConfirmMuxStart() => throw new NotSupportedException();
        public bool ConfirmBatchExecution(int itemCount, int archiveFileCount, long archiveTotalBytes) => throw new NotSupportedException();
        public bool ConfirmApplyBatchSelectionToAllItems(bool selectItems)
        {
            ConfirmApplyBatchSelectionToAllItemsCallCount++;
            return ConfirmApplyBatchSelectionToAllItemsResult;
        }
        public bool ConfirmArchiveCopy(FileCopyPlan copyPlan) => throw new NotSupportedException();
        public bool ConfirmSingleEpisodeCleanup(IReadOnlyList<string> usedFiles, IReadOnlyList<string> unusedFiles) => throw new NotSupportedException();
        public bool ConfirmBatchRecycleDoneFiles(int fileCount, string doneDirectory) => throw new NotSupportedException();
        public bool AskOpenDoneDirectory(string doneDirectory) => throw new NotSupportedException();
        public bool ConfirmPlanReview(string episodeTitle, string reviewText)
        {
            ConfirmPlanReviewCallCount++;
            return ConfirmPlanReviewResult;
        }
        public bool TryOpenFilesWithDefaultApp(IEnumerable<string> filePaths) => throw new NotSupportedException();
        public void OpenPathWithDefaultApp(string path) => throw new NotSupportedException();
        public MessageBoxResult AskSourceReviewResult(string fileName, bool canTryAlternative) => throw new NotSupportedException();
        public void ShowInfo(string title, string message)
        {
        }

        public void ShowWarning(string title, string message)
        {
        }

        public void ShowError(string message) => throw new InvalidOperationException(message);
    }
}
