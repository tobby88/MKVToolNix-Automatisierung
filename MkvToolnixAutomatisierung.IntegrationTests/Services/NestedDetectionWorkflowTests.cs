using System.IO;
using System.Reflection;
using System.Windows;
using MkvToolnixAutomatisierung.IntegrationTests.TestInfrastructure;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.IntegrationTests.Services;

[Collection("PortableStorage")]
public sealed class NestedDetectionWorkflowTests(PortableStorageFixture storage)
{
    [Fact]
    public async Task AlternativeDetection_KeepsOuterReviewBusy_AcrossRealScanAndDelayedProvider()
    {
        storage.Reset();
        var root = Path.Combine(Path.GetTempPath(), "nested-detection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var selected = Path.Combine(root, "Review - Pilot (S01_E01).mp4");
            var alternative = Path.Combine(root, "Review - Pilot (S01_E01)-2.mp4");
            foreach (var path in new[] { selected, alternative })
            {
                File.WriteAllText(path, "synthetic");
                File.WriteAllText(Path.ChangeExtension(path, ".txt"), "Thema: Review\nTitel: Pilot (S01_E01)\nSender: ZDF");
                FakeMkvMergeTestHelper.WriteProbeFile(path,
                    new { id = 0, type = "video", codec = "AVC/H.264", properties = new { pixel_dimensions = "1280x720", language_ietf = "de" } },
                    new { id = 1, type = "audio", codec = "AAC", properties = new { language_ietf = "de" } });
            }
            var settings = new AppSettingsStore();
            new AppToolPathStore(settings).Save(new() { MkvToolNixDirectoryPath = FakeMkvMergeTestHelper.ResolveExecutablePath(), MkvToolNixPathExplicitlySelected = true });
            var store = new AppMetadataStore(settings);
            store.Save(new() { TvdbApiKey = "fake-key" });
            var provider = new BlockedProvider();
            await WpfTestHost.RunAsync(async () =>
            {
                ViewModelTestContext.EnsureApplication();
                var services = ViewModelTestContext.CreateBatchServices(new EpisodeMetadataLookupService(store, provider));
                var detected = await services.Shared.SeriesEpisodeMux.DetectFromSelectedVideoAsync(selected);
                var guess = new EpisodeMetadataGuess("Review", "Pilot", "01", "01");
                var item = BatchEpisodeItemViewModel.CreateFromDetection(selected, guess, detected,
                    new(guess, null, "Pending", 0, true, false, false), Path.Combine(root, "out.mkv"), BatchEpisodeStatusKind.Ready, true);
                var viewModel = new BatchMuxViewModel(services, new NoDialogs());
                viewModel.EpisodeItems.Add(item);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var busy = typeof(BatchMuxViewModel).GetMethod("SetBusy", flags)!;
                var detect = typeof(BatchMuxViewModel).GetMethods(flags).Single(m => m.Name == "ApplyDetectionToItemAsync" && m.GetParameters().Length == 4);
                // The outer review owns the busy state. Invoke its actual alternative-detection callback.
                busy.Invoke(viewModel, [true]);
                var scan = (Task<bool>)detect.Invoke(viewModel, [item, selected, new[] { selected }, CancellationToken.None])!;
                try
                {
                    await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    Assert.False(viewModel.IsInteractive);
                    Assert.False(viewModel.RunBatchCommand.CanExecute(null));
                    provider.Release.TrySetResult([]);
                    Assert.True(await scan.WaitAsync(TimeSpan.FromSeconds(15)));
                    Assert.Equal(alternative, item.MainVideoPath);
                    Assert.Contains(selected, item.ExcludedSourcePaths);
                    Assert.False(viewModel.IsInteractive);
                }
                finally
                {
                    provider.Release.TrySetResult([]);
                    await scan;
                    busy.Invoke(viewModel, [false]);
                }
                Assert.True(viewModel.IsInteractive);
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class BlockedProvider : ITvdbClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IReadOnlyList<TvdbSeriesSearchResult>> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<TvdbSeriesSearchResult>> SearchSeriesAsync(string apiKey, string? pin, string query, CancellationToken cancellationToken = default)
        { Started.TrySetResult(); return Release.Task; }
        public Task<IReadOnlyList<TvdbEpisodeRecord>> GetSeriesEpisodesAsync(string apiKey, string? pin, int seriesId, string? language = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TvdbEpisodeRecord>>([]);
        public Task<string?> GetEpisodeImdbIdAsync(string apiKey, string? pin, int episodeId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public void Dispose() { }
    }

    private sealed class NoDialogs : IUserDialogService
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
        public void ShowError(string message) => throw new InvalidOperationException(message);
    }
}
