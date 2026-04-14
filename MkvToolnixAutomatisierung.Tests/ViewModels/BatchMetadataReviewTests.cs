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
        Assert.Contains("AD-Quelle", item.PlanSummaryText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nur AD", item.Status, StringComparison.OrdinalIgnoreCase);
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
            reviewWorkflow);
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

    private sealed class FakeDialogService : IUserDialogService
    {
        public int ConfirmPlanReviewCallCount { get; private set; }

        public bool ConfirmPlanReviewResult { get; init; } = true;

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
        public MessageBoxResult AskAudioDescriptionChoice() => throw new NotSupportedException();
        public MessageBoxResult AskSubtitlesChoice() => throw new NotSupportedException();
        public MessageBoxResult AskAttachmentChoice() => throw new NotSupportedException();
        public bool ConfirmMuxStart() => throw new NotSupportedException();
        public bool ConfirmBatchExecution(int itemCount, int archiveFileCount, long archiveTotalBytes) => throw new NotSupportedException();
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
