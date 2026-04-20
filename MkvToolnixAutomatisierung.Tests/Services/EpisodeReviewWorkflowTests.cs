using System.Windows;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class EpisodeReviewWorkflowTests
{
    [Fact]
    public async Task ReviewManualSourceAsync_ReviewsAllPendingSourcesBeforeReturning()
    {
        var dialogService = new FakeDialogService(MessageBoxResult.Yes, MessageBoxResult.Yes);
        var workflow = new EpisodeReviewWorkflow(dialogService, CreateEpisodeMetadataService());
        var item = new FakeEpisodeReviewItem(
            @"C:\Temp\episode-1.mp4",
            @"C:\Temp\episode-2.mp4");

        var approved = await workflow.ReviewManualSourceAsync(
            item,
            static (_, _) => { },
            currentProgress: 50,
            reviewStatusText: "Prüfe Quelle...",
            cancelledStatusText: "Abgebrochen",
            openFailedStatusText: "Öffnen fehlgeschlagen",
            approvedStatusText: "Freigegeben",
            alternativeStatusText: "Alternative gewählt",
            _ => Task.FromResult(false));

        Assert.True(approved);
        Assert.True(item.IsManualCheckApproved);
        Assert.Equal(
        [
            @"C:\Temp\episode-1.mp4",
            @"C:\Temp\episode-2.mp4"
        ],
            dialogService.OpenedFilePaths);
        Assert.Equal(2, dialogService.ReviewPromptCallCount);
    }

    [Fact]
    public async Task ReviewManualSourceAsync_RechecksAfterAlternativeSourceWasChosen()
    {
        var dialogService = new FakeDialogService(MessageBoxResult.No, MessageBoxResult.Yes);
        var workflow = new EpisodeReviewWorkflow(dialogService, CreateEpisodeMetadataService());
        var item = new FakeEpisodeReviewItem(@"C:\Temp\episode-alt-1.mp4");
        var alternativeWasTried = false;

        var approved = await workflow.ReviewManualSourceAsync(
            item,
            static (_, _) => { },
            currentProgress: 50,
            reviewStatusText: "Prüfe Quelle...",
            cancelledStatusText: "Abgebrochen",
            openFailedStatusText: "Öffnen fehlgeschlagen",
            approvedStatusText: "Freigegeben",
            alternativeStatusText: "Alternative gewählt",
            tentativeExclusions =>
            {
                alternativeWasTried = true;
                item.ReplaceExcludedSourcePaths(tentativeExclusions);
                item.ReplaceManualCheckFilePaths(@"C:\Temp\episode-alt-2.mp4");
                return Task.FromResult(true);
            });

        Assert.True(approved);
        Assert.True(alternativeWasTried);
        Assert.True(item.IsManualCheckApproved);
        Assert.Equal(
        [
            @"C:\Temp\episode-alt-1.mp4",
            @"C:\Temp\episode-alt-2.mp4"
        ],
            dialogService.OpenedFilePaths);
        Assert.Contains(@"C:\Temp\episode-alt-1.mp4", item.ExcludedSourcePaths);
    }

    [Fact]
    public async Task ReviewManualSourceAsync_StopsWhenReviewFileCouldNotBeOpened()
    {
        var dialogService = new FakeDialogService(canOpenFiles: false, MessageBoxResult.Yes);
        var workflow = new EpisodeReviewWorkflow(dialogService, CreateEpisodeMetadataService());
        var item = new FakeEpisodeReviewItem(@"C:\Temp\episode-open-fail.mp4");
        var reportedStates = new List<string>();

        var approved = await workflow.ReviewManualSourceAsync(
            item,
            (status, _) => reportedStates.Add(status),
            currentProgress: 50,
            reviewStatusText: "Prüfe Quelle...",
            cancelledStatusText: "Abgebrochen",
            openFailedStatusText: "Öffnen fehlgeschlagen",
            approvedStatusText: "Freigegeben",
            alternativeStatusText: "Alternative gewählt",
            _ => Task.FromResult(false));

        Assert.False(approved);
        Assert.False(item.IsManualCheckApproved);
        Assert.Single(dialogService.OpenedFilePaths);
        Assert.Equal(0, dialogService.ReviewPromptCallCount);
        Assert.Equal(["Prüfe Quelle...", "Öffnen fehlgeschlagen"], reportedStates);
    }

    private static EpisodeMetadataLookupService CreateEpisodeMetadataService()
    {
        return new EpisodeMetadataLookupService(
            new AppMetadataStore(new AppSettingsStore()),
            new TvdbClient());
    }

    private sealed class FakeDialogService : IUserDialogService
    {
        private readonly Queue<MessageBoxResult> _reviewResults;

        public FakeDialogService(params MessageBoxResult[] reviewResults)
            : this(canOpenFiles: true, reviewResults)
        {
        }

        public FakeDialogService(bool canOpenFiles, params MessageBoxResult[] reviewResults)
        {
            CanOpenFiles = canOpenFiles;
            _reviewResults = new Queue<MessageBoxResult>(reviewResults);
        }

        public bool CanOpenFiles { get; }

        public List<string> OpenedFilePaths { get; } = [];

        public int ReviewPromptCallCount { get; private set; }

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

        public bool ConfirmArchiveCopy(MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux.FileCopyPlan copyPlan) => throw new NotSupportedException();

        public bool ConfirmSingleEpisodeCleanup(IReadOnlyList<string> usedFiles, IReadOnlyList<string> unusedFiles) => throw new NotSupportedException();

        public bool ConfirmBatchRecycleDoneFiles(int fileCount, string doneDirectory) => throw new NotSupportedException();

        public bool AskOpenDoneDirectory(string doneDirectory) => throw new NotSupportedException();

        public bool ConfirmPlanReview(string episodeTitle, string reviewText) => throw new NotSupportedException();

        public bool TryOpenFilesWithDefaultApp(IEnumerable<string> filePaths)
        {
            OpenedFilePaths.AddRange(filePaths);
            return CanOpenFiles;
        }

        public void OpenPathWithDefaultApp(string path) => throw new NotSupportedException();

        public MessageBoxResult AskSourceReviewResult(string fileName, bool canTryAlternative)
        {
            ReviewPromptCallCount++;
            return _reviewResults.Dequeue();
        }

        public void ShowInfo(string title, string message) => throw new NotSupportedException();

        public void ShowWarning(string title, string message) => throw new NotSupportedException();

        public void ShowError(string message) => throw new NotSupportedException();
    }

    private sealed class FakeEpisodeReviewItem : IEpisodeReviewItem
    {
        private readonly List<string> _manualCheckFilePaths;
        private readonly HashSet<string> _approvedPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _excludedSourcePaths = new(StringComparer.OrdinalIgnoreCase);

        public FakeEpisodeReviewItem(params string[] manualCheckFilePaths)
        {
            _manualCheckFilePaths = manualCheckFilePaths.ToList();
        }

        public string ReviewTitle => "Beispiel";

        public string SeriesName => "Beispielserie";

        public string SeasonNumber => "01";

        public string EpisodeNumber => "01";

        public string Title => "Pilot";

        public bool RequiresManualCheck => _manualCheckFilePaths.Count > 0;

        public bool IsManualCheckApproved => string.IsNullOrWhiteSpace(CurrentReviewTargetPath);

        public string? CurrentReviewTargetPath => _manualCheckFilePaths.FirstOrDefault(path => !_approvedPaths.Contains(path));

        public string DetectionSeedPath => _manualCheckFilePaths.FirstOrDefault() ?? string.Empty;

        public IReadOnlyCollection<string> ExcludedSourcePaths => _excludedSourcePaths;

        public bool RequiresMetadataReview => false;

        public bool IsMetadataReviewApproved => true;

        public string LocalSeriesName => SeriesName;

        public string LocalSeasonNumber => SeasonNumber;

        public string LocalEpisodeNumber => EpisodeNumber;

        public string LocalTitle => Title;

        public void ApproveCurrentReviewTarget()
        {
            if (!string.IsNullOrWhiteSpace(CurrentReviewTargetPath))
            {
                _approvedPaths.Add(CurrentReviewTargetPath);
            }
        }

        public void ApplyLocalMetadataGuess()
        {
        }

        public void ApplyTvdbSelection(TvdbEpisodeSelection selection)
        {
        }

        public void ApproveMetadataReview(string statusText)
        {
        }

        public void ReplaceManualCheckFilePaths(params string[] filePaths)
        {
            _manualCheckFilePaths.Clear();
            _manualCheckFilePaths.AddRange(filePaths);
            _approvedPaths.RemoveWhere(path => !_manualCheckFilePaths.Contains(path, StringComparer.OrdinalIgnoreCase));
        }

        public void ReplaceExcludedSourcePaths(IEnumerable<string> filePaths)
        {
            _excludedSourcePaths.Clear();
            foreach (var filePath in filePaths)
            {
                _excludedSourcePaths.Add(filePath);
            }
        }
    }
}
