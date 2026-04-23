using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Emby;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.ViewModels;
using MkvToolnixAutomatisierung.Windows;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Windows;

[Collection("PortableStorage")]
public sealed class AppSettingsWindowTests
{
    [Fact]
    public async Task Window_DisablesFooterAndCancelsCloseWhileEmbyTestIsRunning()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var connectionProbe = new TaskCompletionSource<EmbyServerInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            var viewModel = CreateViewModel(new PendingEmbyClient(connectionProbe.Task));
            viewModel.EmbyApiKey = "emby-key";
            var window = new AppSettingsWindow(viewModel);
            var cancelButton = (Button)window.FindName("CancelButton")!;
            var saveButton = (Button)window.FindName("SaveButton")!;
            window.Show();

            await WpfTestHost.WaitForIdleAsync();
            var connectionTask = viewModel.TestEmbyConnectionAsync();
            await WpfTestHost.WaitForIdleAsync();

            Assert.False(viewModel.IsInteractive);
            Assert.False(cancelButton.IsEnabled);
            Assert.False(saveButton.IsEnabled);

            window.Close();
            await WpfTestHost.WaitForIdleAsync();

            Assert.True(window.IsVisible);

            connectionProbe.SetResult(new EmbyServerInfo("Test-Emby", "4.9.0", "emby-1"));
            await connectionTask;
            await WpfTestHost.WaitForIdleAsync();

            Assert.True(viewModel.IsInteractive);
            Assert.True(cancelButton.IsEnabled);
            Assert.True(saveButton.IsEnabled);

            window.Close();
            await WpfTestHost.WaitForIdleAsync();
            Assert.False(window.IsVisible);
        });
    }

    private static AppSettingsWindowViewModel CreateViewModel(IEmbyClient embyClient)
    {
        var settingsStore = new AppSettingsStore();
        var services = new AppSettingsModuleServices(
            new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(settingsStore)),
            new AppToolPathStore(settingsStore),
            new NullFfprobeLocator(),
            new NullMkvToolNixLocator(),
            new EpisodeMetadataLookupService(new AppMetadataStore(settingsStore), new ThrowingTvdbClient()),
            new AppEmbySettingsStore(settingsStore),
            new EmbyMetadataSyncService(embyClient, new EmbyNfoProviderIdService()),
            new ImdbLookupService(new HttpClient(new StubHttpMessageHandler())));
        return new AppSettingsWindowViewModel(services, new NullDialogService(), AppSettingsPage.Emby);
    }

    private sealed class PendingEmbyClient(Task<EmbyServerInfo> probeTask) : IEmbyClient
    {
        public Task<IReadOnlyList<EmbyLibraryFolder>> GetLibrariesAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EmbyLibraryFolder>>([]);

        public Task<EmbyServerInfo> GetSystemInfoAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => probeTask;

        public Task TriggerLibraryScanAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task TriggerItemFileScanAsync(AppEmbySettings settings, string itemId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<EmbyItem?> FindItemByPathAsync(AppEmbySettings settings, string mediaFilePath, CancellationToken cancellationToken = default)
            => Task.FromResult<EmbyItem?>(null);

        public Task RefreshItemMetadataAsync(AppEmbySettings settings, string itemId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingTvdbClient : ITvdbClient
    {
        public Task<IReadOnlyList<TvdbSeriesSearchResult>> SearchSeriesAsync(string apiKey, string? pin, string query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TvdbEpisodeRecord>> GetSeriesEpisodesAsync(string apiKey, string? pin, int seriesId, string? language = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class NullFfprobeLocator : IFfprobeLocator
    {
        public string? TryFindFfprobePath() => null;
    }

    private sealed class NullMkvToolNixLocator : IMkvToolNixLocator
    {
        public string FindMkvMergePath() => throw new FileNotFoundException();

        public string FindMkvPropEditPath() => throw new FileNotFoundException();
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
        public bool ConfirmArchiveCopy(MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux.FileCopyPlan copyPlan) => false;
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
