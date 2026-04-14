using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Emby;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class EmbySyncViewModelTests
{
    private static EmbySyncViewModel CreateViewModel(IUserDialogService? dialogService = null)
    {
        var settingsStore = new AppSettingsStore();
        var embySettings = new AppEmbySettingsStore(settingsStore);
        var syncService = new EmbyMetadataSyncService(new ThrowingEmbyClient(), new EmbyNfoProviderIdService());
        var services = new EmbyModuleServices(embySettings, syncService);
        return new EmbySyncViewModel(services, dialogService ?? new NullDialogService());
    }

    [Fact]
    public void ServerUrl_SetWithLeadingAndTrailingWhitespace_GetsNormalized()
    {
        var vm = CreateViewModel();

        vm.ServerUrl = "  http://emby:8096  ";

        Assert.Equal("http://emby:8096", vm.ServerUrl);
    }

    [Fact]
    public void ApiKey_SetWithWhitespace_GetsNormalized()
    {
        var vm = CreateViewModel();

        vm.ApiKey = "  abc-def-key  ";

        Assert.Equal("abc-def-key", vm.ApiKey);
    }

    [Fact]
    public void SummaryText_WhenNoItemsLoaded_ShowsEmptyPlaceholder()
    {
        var vm = CreateViewModel();

        Assert.Equal("Noch kein Metadatenreport geladen.", vm.SummaryText);
    }

    [Fact]
    public void SummaryText_AfterItemsAdded_ReflectsCount()
    {
        var vm = CreateViewModel();
        vm.Items.Add(new EmbySyncItemViewModel(@"C:\Videos\Serie S01E01.mkv", EmbyProviderIds.Empty));
        vm.Items.Add(new EmbySyncItemViewModel(@"C:\Videos\Serie S01E02.mkv", EmbyProviderIds.Empty));

        Assert.Contains("2 Datei(en)", vm.SummaryText);
    }

    [Fact]
    public void IsInteractive_Initially_IsTrue()
    {
        var vm = CreateViewModel();

        Assert.True(vm.IsInteractive);
    }

    [Fact]
    public void TestConnectionCommand_CannotExecute_WhenServerUrlEmpty()
    {
        var vm = CreateViewModel();
        vm.ServerUrl = string.Empty;
        vm.ApiKey = "some-key";

        Assert.False(vm.TestConnectionCommand.CanExecute(null));
    }

    [Fact]
    public void TestConnectionCommand_CannotExecute_WhenApiKeyEmpty()
    {
        var vm = CreateViewModel();
        vm.ServerUrl = "http://emby:8096";
        vm.ApiKey = string.Empty;

        Assert.False(vm.TestConnectionCommand.CanExecute(null));
    }

    [Fact]
    public void TestConnectionCommand_CanExecute_WhenBothServerUrlAndApiKeySet()
    {
        var vm = CreateViewModel();
        vm.ServerUrl = "http://emby:8096";
        vm.ApiKey = "some-key";

        Assert.True(vm.TestConnectionCommand.CanExecute(null));
    }

    [Fact]
    public void MissingIdCount_ReflectsItemsWithoutProviderIds()
    {
        var vm = CreateViewModel();
        vm.Items.Add(new EmbySyncItemViewModel(@"C:\Videos\S01E01.mkv", EmbyProviderIds.Empty));
        vm.Items.Add(new EmbySyncItemViewModel(@"C:\Videos\S01E02.mkv", new EmbyProviderIds("12345", null)));

        Assert.Equal(1, vm.MissingIdCount);
    }

    private sealed class ThrowingEmbyClient : IEmbyClient
    {
        public Task<EmbyServerInfo> GetSystemInfoAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task TriggerLibraryScanAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EmbyItem?> FindItemByPathAsync(AppEmbySettings settings, string mediaFilePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RefreshItemMetadataAsync(AppEmbySettings settings, string itemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Dispose() { }
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
        public MessageBoxResult AskAudioDescriptionChoice() => MessageBoxResult.Cancel;
        public MessageBoxResult AskSubtitlesChoice() => MessageBoxResult.Cancel;
        public MessageBoxResult AskAttachmentChoice() => MessageBoxResult.Cancel;
        public bool ConfirmMuxStart() => false;
        public bool ConfirmBatchExecution(int itemCount, int archiveFileCount, long archiveTotalBytes) => false;
        public bool ConfirmArchiveCopy(FileCopyPlan copyPlan) => false;
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
