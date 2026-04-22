using System.IO;
using System.Reflection;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

[Collection("PortableStorage")]
public sealed class CleanupCancellationViewModelTests : IDisposable
{
    private readonly PortableStorageFixture _storageFixture;
    private readonly string _tempDirectory;

    public CleanupCancellationViewModelTests(PortableStorageFixture storageFixture)
    {
        _storageFixture = storageFixture;
        _storageFixture.Reset();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-cleanup-viewmodel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        ViewModelTestContext.EnsureApplication();
    }

    [Fact]
    public async Task OfferBatchDoneCleanupAsync_ShowsWarning_WhenRecycleWasCanceled()
    {
        var doneDirectory = Path.Combine(_tempDirectory, "done");
        Directory.CreateDirectory(doneDirectory);
        var recycledFile = CreateFile(doneDirectory, "Pilot.mkv");
        var pendingFile = CreateFile(doneDirectory, "Pilot.srt");
        var cleanup = new StubCleanupService(
            recycleResult: new FileRecycleResult([recycledFile], [], [pendingFile], WasCanceled: true));
        var dialogService = new CapturingDialogService
        {
            ConfirmBatchRecycleDoneFilesResult = true
        };
        var viewModel = new BatchMuxViewModel(
            ViewModelTestContext.CreateBatchServices(cleanup: cleanup),
            dialogService);

        await InvokeOfferBatchDoneCleanupAsync(
            viewModel,
            doneDirectory,
            [recycledFile, pendingFile],
            new BatchRunProgressTracker(1, static (_, _) => { }));

        var warning = Assert.Single(dialogService.WarningMessages);
        Assert.Contains("vorzeitig abgebrochen", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFileName(pendingFile), warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OfferSingleEpisodeCleanupAsync_ShowsWarning_WhenRecycleWasCanceled()
    {
        var sourceDirectory = Path.Combine(_tempDirectory, "single-source");
        Directory.CreateDirectory(sourceDirectory);
        var sourceFile = CreateFile(sourceDirectory, "Pilot.mp4");
        var pendingSubtitle = CreateFile(sourceDirectory, "Pilot.srt");
        var outputPath = Path.Combine(_tempDirectory, "Pilot.mkv");
        var cleanup = new StubCleanupService(
            recycleResult: new FileRecycleResult([sourceFile], [], [pendingSubtitle], WasCanceled: true));
        var dialogService = new CapturingDialogService
        {
            ConfirmSingleEpisodeCleanupResult = true
        };
        var viewModel = new SingleEpisodeMuxViewModel(
            ViewModelTestContext.CreateSingleEpisodeServices(cleanup: cleanup),
            dialogService);
        var plan = CreatePlan(sourceFile, pendingSubtitle, outputPath);

        await InvokeOfferSingleEpisodeCleanupAsync(viewModel, plan);

        var warning = Assert.Single(dialogService.WarningMessages);
        Assert.Contains("vorzeitig abgebrochen", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFileName(pendingSubtitle), warning, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string CreateFile(string directory, string fileName)
    {
        var filePath = Path.Combine(directory, fileName);
        File.WriteAllText(filePath, "data");
        return filePath;
    }

    private static async Task InvokeOfferBatchDoneCleanupAsync(
        BatchMuxViewModel viewModel,
        string doneDirectory,
        IReadOnlyList<string> movedDoneFiles,
        BatchRunProgressTracker progressTracker)
    {
        var method = typeof(BatchMuxViewModel).GetMethod(
            "OfferBatchDoneCleanupAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(viewModel, [doneDirectory, movedDoneFiles, progressTracker, CancellationToken.None]));
        await task;
    }

    private static async Task InvokeOfferSingleEpisodeCleanupAsync(
        SingleEpisodeMuxViewModel viewModel,
        SeriesEpisodeMuxPlan plan)
    {
        var method = typeof(SingleEpisodeMuxViewModel).GetMethod(
            "OfferSingleEpisodeCleanupAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(viewModel, [plan, CancellationToken.None]));
        await task;
    }

    private static SeriesEpisodeMuxPlan CreatePlan(string sourceFilePath, string subtitlePath, string outputPath)
    {
        return new SeriesEpisodeMuxPlan(
            mkvMergePath: "mkvmerge.exe",
            outputFilePath: outputPath,
            title: "Pilot",
            videoSources:
            [
                new VideoSourcePlan(sourceFilePath, 0, "Video", true)
            ],
            audioSources:
            [
                new AudioSourcePlan(sourceFilePath, 1, "Deutsch", true)
            ],
            primarySourceAudioTrackIds: null,
            primarySourceSubtitleTrackIds: null,
            primarySourceAttachmentIds: null,
            includePrimarySourceAttachments: false,
            attachmentSourcePath: null,
            attachmentSourceAttachmentIds: null,
            audioDescriptionFilePath: null,
            audioDescriptionTrackId: null,
            audioDescriptionTrackName: null,
            audioDescriptionLanguageCode: null,
            subtitleFiles:
            [
                SubtitleFile.CreateDetectedExternal(subtitlePath, SubtitleKind.FromExtension(Path.GetExtension(subtitlePath)))
            ],
            attachmentFilePaths: [],
            preservedAttachmentNames: [],
            usageComparison: ArchiveUsageComparison.Empty,
            workingCopy: null);
    }

    private sealed class StubCleanupService(FileRecycleResult recycleResult) : IEpisodeCleanupService
    {
        public Task<FileMoveResult> MoveFilesToDirectoryAsync(
            IReadOnlyList<string> sourceFilePaths,
            string targetDirectory,
            Action<int, int, string>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<FileRecycleResult> RecycleFilesAsync(
            IReadOnlyList<string> filePaths,
            Action<int, int, string>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(recycleResult);
        }

        public void DeleteTemporaryFile(string? filePath)
        {
        }

        public void DeleteDirectoryIfEmpty(string? directoryPath)
        {
        }

        public void DeleteEmptyParentDirectories(IEnumerable<string> sourceFilePaths, string? stopAtRoot)
        {
        }
    }

    private sealed class CapturingDialogService : IUserDialogService
    {
        public bool ConfirmSingleEpisodeCleanupResult { get; init; }

        public bool ConfirmBatchRecycleDoneFilesResult { get; init; }

        public List<string> WarningMessages { get; } = [];

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
        public bool ConfirmSingleEpisodeCleanup(IReadOnlyList<string> usedFiles, IReadOnlyList<string> unusedFiles) => ConfirmSingleEpisodeCleanupResult;
        public bool ConfirmBatchRecycleDoneFiles(int fileCount, string doneDirectory) => ConfirmBatchRecycleDoneFilesResult;
        public bool AskOpenDoneDirectory(string doneDirectory) => false;
        public bool ConfirmPlanReview(string episodeTitle, string reviewText) => throw new NotSupportedException();
        public bool TryOpenFilesWithDefaultApp(IEnumerable<string> filePaths) => throw new NotSupportedException();
        public void OpenPathWithDefaultApp(string path) => throw new NotSupportedException();
        public MessageBoxResult AskSourceReviewResult(string fileName, bool canTryAlternative) => throw new NotSupportedException();
        public void ShowInfo(string title, string message) => throw new NotSupportedException();
        public void ShowWarning(string title, string message) => WarningMessages.Add(message);
        public void ShowError(string message) => throw new InvalidOperationException(message);
    }
}
