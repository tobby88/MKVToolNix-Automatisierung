using System.IO;
using System.Windows;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Emby;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.ViewModels;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

[Collection("PortableStorage")]
public sealed class AppSettingsWindowViewModelTests : IDisposable
{
    private readonly PortableStorageFixture _storageFixture;
    private readonly string _tempDirectory;

    public AppSettingsWindowViewModelTests(PortableStorageFixture storageFixture)
    {
        _storageFixture = storageFixture;
        _storageFixture.Reset();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-app-settings-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void SaveSettings_PersistsAllManagedSettings()
    {
        var archiveRoot = CreateDirectory("archive");
        var mkvToolNixDirectory = CreateDirectory("mkvtoolnix");
        _ = CreateFile(Path.Combine("mkvtoolnix", "mkvmerge.exe"));
        _ = CreateFile(Path.Combine("mkvtoolnix", "mkvpropedit.exe"));
        var ffprobePath = CreateFile(Path.Combine("ffmpeg", "ffprobe.exe"));
        var settingsStore = new AppSettingsStore();
        settingsStore.Save(new CombinedAppSettings
        {
            ToolPaths = new AppToolPathSettings
            {
                ManagedMkvToolNix = new ManagedToolSettings
                {
                    AutoManageEnabled = true,
                    InstalledPath = @"C:\managed\mkvtoolnix",
                    InstalledVersion = "98.0"
                },
                ManagedFfprobe = new ManagedToolSettings
                {
                    AutoManageEnabled = true,
                    InstalledPath = @"C:\managed\ffprobe.exe",
                    InstalledVersion = "2026-04-18T13-04-00Z"
                }
            }
        });
        var viewModel = CreateViewModel(settingsStore: settingsStore);

        viewModel.ArchiveRootDirectory = $"  {archiveRoot}  ";
        viewModel.MkvToolNixDirectoryPath = $"  {mkvToolNixDirectory}  ";
        viewModel.FfprobePath = $"  {ffprobePath}  ";
        viewModel.AutoManageMkvToolNix = false;
        viewModel.AutoManageFfprobe = false;
        viewModel.TvdbApiKey = "  tvdb-key  ";
        viewModel.TvdbPin = "  1234  ";
        viewModel.EmbyServerUrl = "  http://emby-test:8096  ";
        viewModel.EmbyApiKey = "  emby-key  ";
        viewModel.EmbyScanWaitTimeoutSeconds = 999;

        viewModel.SaveSettings();

        var savedSettings = new AppSettingsStore().Load();
        Assert.Equal(archiveRoot, savedSettings.Archive?.DefaultSeriesArchiveRootPath);
        Assert.Equal(mkvToolNixDirectory, savedSettings.ToolPaths?.MkvToolNixDirectoryPath);
        Assert.Equal(ffprobePath, savedSettings.ToolPaths?.FfprobePath);
        Assert.False(savedSettings.ToolPaths?.ManagedMkvToolNix.AutoManageEnabled);
        Assert.False(savedSettings.ToolPaths?.ManagedFfprobe.AutoManageEnabled);
        Assert.Equal(@"C:\managed\mkvtoolnix", savedSettings.ToolPaths?.ManagedMkvToolNix.InstalledPath);
        Assert.Equal("98.0", savedSettings.ToolPaths?.ManagedMkvToolNix.InstalledVersion);
        Assert.Equal(@"C:\managed\ffprobe.exe", savedSettings.ToolPaths?.ManagedFfprobe.InstalledPath);
        Assert.Equal("2026-04-18T13-04-00Z", savedSettings.ToolPaths?.ManagedFfprobe.InstalledVersion);
        Assert.Equal("tvdb-key", savedSettings.Metadata?.TvdbApiKey);
        Assert.Equal("1234", savedSettings.Metadata?.TvdbPin);
        Assert.Equal("http://emby-test:8096", savedSettings.Emby?.ServerUrl);
        Assert.Equal("emby-key", savedSettings.Emby?.ApiKey);
        Assert.Equal(600, savedSettings.Emby?.ScanWaitTimeoutSeconds);
        Assert.True(viewModel.IsArchiveAvailable);
        Assert.True(viewModel.IsFfprobeAvailable);
        Assert.True(viewModel.IsMkvToolNixAvailable);
        Assert.Contains("Einstellungen gespeichert", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestEmbyConnectionAsync_UpdatesStatusTextFromServerInfo()
    {
        var viewModel = CreateViewModel(new StubEmbyClient(new EmbyServerInfo("Test-Emby", "4.9.0", "emby-1")));
        viewModel.EmbyApiKey = "emby-key";

        await viewModel.TestEmbyConnectionAsync();

        Assert.True(viewModel.IsInteractive);
        Assert.Contains("Test-Emby", viewModel.StatusText, StringComparison.Ordinal);
        Assert.Contains("4.9.0", viewModel.StatusText, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private AppSettingsWindowViewModel CreateViewModel(IEmbyClient? embyClient = null, AppSettingsStore? settingsStore = null)
    {
        settingsStore ??= new AppSettingsStore();
        var services = new AppSettingsModuleServices(
            new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(settingsStore)),
            new AppToolPathStore(settingsStore),
            new StubFfprobeLocator(),
            new StubMkvToolNixLocator(),
            new EpisodeMetadataLookupService(new AppMetadataStore(settingsStore), new ThrowingTvdbClient()),
            new AppEmbySettingsStore(settingsStore),
            new EmbyMetadataSyncService(embyClient ?? new StubEmbyClient(new EmbyServerInfo("Test-Emby", "4.9.0", "emby-1")), new EmbyNfoProviderIdService()));
        return new AppSettingsWindowViewModel(services, new NullDialogService(), AppSettingsPage.Archive);
    }

    private string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(_tempDirectory, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    private string CreateFile(string relativePath, string content = "tool")
    {
        var path = Path.Combine(_tempDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class StubFfprobeLocator : IFfprobeLocator
    {
        public string? TryFindFfprobePath()
        {
            return null;
        }
    }

    private sealed class StubMkvToolNixLocator : IMkvToolNixLocator
    {
        public string FindMkvMergePath()
        {
            throw new NotSupportedException();
        }

        public string FindMkvPropEditPath()
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubEmbyClient(EmbyServerInfo serverInfo) : IEmbyClient
    {
        public Task<IReadOnlyList<EmbyLibraryFolder>> GetLibrariesAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EmbyLibraryFolder>>([]);

        public Task<EmbyServerInfo> GetSystemInfoAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => Task.FromResult(serverInfo);

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
