using System.IO;
using System.Net.Http;
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
        var mediathekViewPath = CreateFile(Path.Combine("mediathekview", "MediathekView.exe"));
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
                },
                ManagedMediathekView = new ManagedToolSettings
                {
                    AutoManageEnabled = true,
                    InstalledPath = @"C:\managed\MediathekView_Portable.exe",
                    InstalledVersion = "14.5.0"
                }
            }
        });
        var viewModel = CreateViewModel(settingsStore: settingsStore);

        viewModel.ArchiveRootDirectory = $"  {archiveRoot}  ";
        viewModel.MkvToolNixDirectoryPath = $"  {mkvToolNixDirectory}  ";
        viewModel.FfprobePath = $"  {ffprobePath}  ";
        viewModel.MediathekViewPath = $"  {mediathekViewPath}  ";
        viewModel.AutoManageMkvToolNix = false;
        viewModel.AutoManageFfprobe = false;
        viewModel.AutoManageMediathekView = false;
        viewModel.TvdbApiKey = "  tvdb-key  ";
        viewModel.TvdbPin = "  1234  ";
        viewModel.SelectedImdbLookupMode = ImdbLookupMode.BrowserOnly;
        viewModel.EmbyServerUrl = "  http://emby-test:8096  ";
        viewModel.EmbyApiKey = "  emby-key  ";
        viewModel.EmbyScanWaitTimeoutSeconds = 300;

        viewModel.SaveSettings();

        var savedSettings = new AppSettingsStore().Load();
        Assert.Equal(archiveRoot, savedSettings.Archive?.DefaultSeriesArchiveRootPath);
        Assert.Equal(mkvToolNixDirectory, savedSettings.ToolPaths?.MkvToolNixDirectoryPath);
        Assert.Equal(ffprobePath, savedSettings.ToolPaths?.FfprobePath);
        Assert.Equal(mediathekViewPath, savedSettings.ToolPaths?.MediathekViewPath);
        Assert.False(savedSettings.ToolPaths?.ManagedMkvToolNix.AutoManageEnabled);
        Assert.False(savedSettings.ToolPaths?.ManagedFfprobe.AutoManageEnabled);
        Assert.False(savedSettings.ToolPaths?.ManagedMediathekView.AutoManageEnabled);
        Assert.Equal(@"C:\managed\mkvtoolnix", savedSettings.ToolPaths?.ManagedMkvToolNix.InstalledPath);
        Assert.Equal("98.0", savedSettings.ToolPaths?.ManagedMkvToolNix.InstalledVersion);
        Assert.Equal(@"C:\managed\ffprobe.exe", savedSettings.ToolPaths?.ManagedFfprobe.InstalledPath);
        Assert.Equal("2026-04-18T13-04-00Z", savedSettings.ToolPaths?.ManagedFfprobe.InstalledVersion);
        Assert.Equal(@"C:\managed\MediathekView_Portable.exe", savedSettings.ToolPaths?.ManagedMediathekView.InstalledPath);
        Assert.Equal("14.5.0", savedSettings.ToolPaths?.ManagedMediathekView.InstalledVersion);
        Assert.Equal("tvdb-key", savedSettings.Metadata?.TvdbApiKey);
        Assert.Equal("1234", savedSettings.Metadata?.TvdbPin);
        Assert.Equal(ImdbLookupMode.BrowserOnly, savedSettings.Metadata?.ImdbLookupMode);
        Assert.Equal("http://emby-test:8096", savedSettings.Emby?.ServerUrl);
        Assert.Equal("emby-key", savedSettings.Emby?.ApiKey);
        Assert.Equal(300, savedSettings.Emby?.ScanWaitTimeoutSeconds);
        Assert.True(viewModel.IsArchiveAvailable);
        Assert.True(viewModel.IsFfprobeAvailable);
        Assert.True(viewModel.IsMkvToolNixAvailable);
        Assert.True(viewModel.IsMediathekViewAvailable);
        Assert.Contains("Einstellungen gespeichert", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("4")]
    [InlineData("601")]
    public void SaveSettings_ShowsValidationAndBlocksSave_WhenEmbyTimeoutIsInvalid(string invalidValue)
    {
        var settingsStore = new AppSettingsStore();
        var viewModel = CreateViewModel(settingsStore: settingsStore);

        viewModel.EmbyScanWaitTimeoutSecondsText = invalidValue;

        Assert.True(viewModel.HasErrors);
        Assert.False(viewModel.CanSave);
        Assert.NotNull(viewModel.EmbyScanWaitTimeoutValidationMessage);
        Assert.Contains("Sekunden", viewModel.EmbyScanWaitTimeoutHelpText, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => viewModel.SaveSettings());
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

    [Fact]
    public async Task TestEmbyConnectionAsync_ResetsBusyStateAndFailureStatusOnError()
    {
        var viewModel = CreateViewModel(new ThrowingEmbyClient());
        viewModel.EmbyApiKey = "emby-key";

        await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.TestEmbyConnectionAsync());

        Assert.True(viewModel.IsInteractive);
        Assert.Equal("Emby-Verbindung fehlgeschlagen", viewModel.StatusText);
    }

    [Fact]
    public async Task SaveAndCloseAsync_SavesSettingsAndEnsuresManagedTools()
    {
        var installer = new RecordingManagedToolInstaller();
        var settingsStore = new AppSettingsStore();
        var viewModel = CreateViewModel(settingsStore: settingsStore, managedToolInstaller: installer);
        var closedWithAcceptedResult = false;
        viewModel.CloseRequested += (_, accepted) => closedWithAcceptedResult = accepted;

        viewModel.AutoManageMkvToolNix = true;
        await viewModel.SaveAndCloseAsync();

        Assert.Equal(1, installer.CallCount);
        Assert.True(closedWithAcceptedResult);
        Assert.True(viewModel.IsInteractive);
        Assert.True(settingsStore.Load().ToolPaths?.ManagedMkvToolNix.AutoManageEnabled);
        Assert.Contains("Werkzeuge bereit", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancel_DuringManagedToolCheck_AbortsAndKeepsDialogOpen()
    {
        var installer = new CancellableManagedToolInstaller();
        var settingsStore = new AppSettingsStore();
        var viewModel = CreateViewModel(settingsStore: settingsStore, managedToolInstaller: installer);
        var closeRequested = false;
        viewModel.CloseRequested += (_, _) => closeRequested = true;

        viewModel.AutoManageMkvToolNix = true;
        var saveTask = viewModel.SaveAndCloseAsync();
        await installer.Started.Task;

        Assert.False(viewModel.IsInteractive);
        Assert.True(viewModel.CanCancel);
        viewModel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => saveTask);
        Assert.True(viewModel.IsInteractive);
        Assert.False(closeRequested);
        Assert.Contains("Werkzeugprüfung abgebrochen", viewModel.StatusText, StringComparison.Ordinal);
        Assert.True(settingsStore.Load().ToolPaths?.ManagedMkvToolNix.AutoManageEnabled);
    }

    [Fact]
    public void ToolStatus_ShowsManagedInstallationEvenWhenAutoManageIsDisabled()
    {
        var mkvToolNixDirectory = CreateDirectory("managed-mkvtoolnix");
        _ = CreateFile(Path.Combine("managed-mkvtoolnix", "mkvmerge.exe"));
        _ = CreateFile(Path.Combine("managed-mkvtoolnix", "mkvpropedit.exe"));
        var ffprobePath = CreateFile(Path.Combine("managed-ffprobe", "ffprobe.exe"));
        var settingsStore = new AppSettingsStore();
        settingsStore.Save(new CombinedAppSettings
        {
            ToolPaths = new AppToolPathSettings
            {
                ManagedMkvToolNix = new ManagedToolSettings
                {
                    AutoManageEnabled = false,
                    InstalledPath = mkvToolNixDirectory,
                    InstalledVersion = "98.0"
                },
                ManagedFfprobe = new ManagedToolSettings
                {
                    AutoManageEnabled = false,
                    InstalledPath = ffprobePath,
                    InstalledVersion = "2026-04-18T13-04-00Z"
                }
            }
        });

        var viewModel = CreateViewModel(settingsStore: settingsStore);

        Assert.True(viewModel.IsMkvToolNixAvailable);
        Assert.True(viewModel.IsFfprobeAvailable);
        Assert.Equal("MKVToolNix bereit (verwaltet)", viewModel.MkvToolNixStatusText);
        Assert.Equal("ffprobe bereit (verwaltet)", viewModel.FfprobeStatusText);
        Assert.Contains("Automatische Updates sind deaktiviert", viewModel.MkvToolNixStatusTooltip, StringComparison.Ordinal);
        Assert.Contains("Automatische Updates sind deaktiviert", viewModel.FfprobeStatusTooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolStatus_ShowsSystemPathFallbackForFfprobe()
    {
        var pathDirectory = CreateDirectory("path-tools");
        var ffprobePath = CreateFile(Path.Combine("path-tools", "ffprobe.exe"));
        var originalPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            Environment.SetEnvironmentVariable("PATH", $"{pathDirectory}{Path.PathSeparator}{originalPath}");
            var viewModel = CreateViewModel();

            Assert.True(viewModel.IsFfprobeAvailable);
            Assert.Equal("ffprobe bereit (PATH)", viewModel.FfprobeStatusText);
            Assert.Contains(ffprobePath, viewModel.FfprobeStatusTooltip, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public void ToolStatus_ShowsDownloadsFallbackForMkvToolNix()
    {
        var userProfileDirectory = CreateDirectory("sandbox-profile");
        var downloadsDirectory = Path.Combine(userProfileDirectory, "Downloads");
        var fallbackRoot = Path.Combine(downloadsDirectory, $"mkvtoolnix-64-bit-999.0-codex-{Guid.NewGuid():N}");
        var toolDirectory = Path.Combine(fallbackRoot, "mkvtoolnix");
        var originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        var originalHome = Environment.GetEnvironmentVariable("HOME");

        try
        {
            Environment.SetEnvironmentVariable("USERPROFILE", userProfileDirectory);
            Environment.SetEnvironmentVariable("HOME", userProfileDirectory);
            Directory.CreateDirectory(toolDirectory);
            var mkvMergePath = Path.Combine(toolDirectory, "mkvmerge.exe");
            var mkvPropEditPath = Path.Combine(toolDirectory, "mkvpropedit.exe");
            File.WriteAllText(mkvMergePath, "tool");
            File.WriteAllText(mkvPropEditPath, "tool");
            Directory.SetLastWriteTimeUtc(fallbackRoot, DateTime.UtcNow.AddMinutes(5));

            var viewModel = CreateViewModel();

            Assert.True(viewModel.IsMkvToolNixAvailable);
            Assert.Equal("MKVToolNix bereit (Fallback)", viewModel.MkvToolNixStatusText);
            Assert.Contains(mkvMergePath, viewModel.MkvToolNixStatusTooltip, StringComparison.Ordinal);
            Assert.Contains(mkvPropEditPath, viewModel.MkvToolNixStatusTooltip, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("USERPROFILE", originalUserProfile);
            Environment.SetEnvironmentVariable("HOME", originalHome);
            if (Directory.Exists(fallbackRoot))
            {
                Directory.Delete(fallbackRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void ToolStatus_ShowsPortableDownloadsFallbackForMediathekView()
    {
        var userProfileDirectory = CreateDirectory("sandbox-profile-mediathekview");
        var downloadsDirectory = Path.Combine(userProfileDirectory, "Downloads");
        var portableDirectory = Path.Combine(downloadsDirectory, "MediathekView");
        var originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        var originalHome = Environment.GetEnvironmentVariable("HOME");

        try
        {
            Environment.SetEnvironmentVariable("USERPROFILE", userProfileDirectory);
            Environment.SetEnvironmentVariable("HOME", userProfileDirectory);
            Directory.CreateDirectory(portableDirectory);
            var mediathekViewPath = Path.Combine(portableDirectory, "MediathekView_Portable.exe");
            File.WriteAllText(mediathekViewPath, "tool");

            var viewModel = CreateViewModel();

            Assert.True(viewModel.IsMediathekViewAvailable);
            Assert.Equal("MediathekView bereit (portable)", viewModel.MediathekViewStatusText);
            Assert.Contains(mediathekViewPath, viewModel.MediathekViewStatusTooltip, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("USERPROFILE", originalUserProfile);
            Environment.SetEnvironmentVariable("HOME", originalHome);
        }
    }

    [Fact]
    public void ToolStatus_PrefersManualOverrideEvenWhenAutoManageRemainsEnabled()
    {
        var manualMkvToolNixDirectory = CreateDirectory("manual-mkvtoolnix");
        _ = CreateFile(Path.Combine("manual-mkvtoolnix", "mkvmerge.exe"));
        _ = CreateFile(Path.Combine("manual-mkvtoolnix", "mkvpropedit.exe"));
        var managedMkvToolNixDirectory = CreateDirectory("managed-mkvtoolnix");
        _ = CreateFile(Path.Combine("managed-mkvtoolnix", "mkvmerge.exe"));
        _ = CreateFile(Path.Combine("managed-mkvtoolnix", "mkvpropedit.exe"));
        var settingsStore = new AppSettingsStore();
        settingsStore.Save(new CombinedAppSettings
        {
            ToolPaths = new AppToolPathSettings
            {
                MkvToolNixDirectoryPath = manualMkvToolNixDirectory,
                ManagedMkvToolNix = new ManagedToolSettings
                {
                    AutoManageEnabled = true,
                    InstalledPath = managedMkvToolNixDirectory,
                    InstalledVersion = "98.0"
                }
            }
        });

        var viewModel = CreateViewModel(settingsStore: settingsStore);

        Assert.True(viewModel.IsMkvToolNixAvailable);
        Assert.Equal("MKVToolNix bereit (Override)", viewModel.MkvToolNixStatusText);
        Assert.Contains("übersteuert", viewModel.MkvToolNixStatusTooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolStatus_TreatsLegacyDownloadOverrideAsFallbackWhenAutoManageIsEnabled()
    {
        var userProfileDirectory = CreateDirectory("sandbox-profile");
        var downloadsDirectory = Path.Combine(userProfileDirectory, "Downloads");
        var legacyMkvRoot = Path.Combine(downloadsDirectory, "mkvtoolnix-64-bit-999.0-legacy");
        var legacyMkvToolDirectory = Path.Combine(legacyMkvRoot, "mkvtoolnix");
        Directory.CreateDirectory(legacyMkvToolDirectory);
        var legacyFfprobeDirectory = Path.Combine(downloadsDirectory, "ffmpeg", "bin");
        Directory.CreateDirectory(legacyFfprobeDirectory);
        var originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        var originalHome = Environment.GetEnvironmentVariable("HOME");

        try
        {
            Environment.SetEnvironmentVariable("USERPROFILE", userProfileDirectory);
            Environment.SetEnvironmentVariable("HOME", userProfileDirectory);

            var mkvMergePath = Path.Combine(legacyMkvToolDirectory, "mkvmerge.exe");
            var mkvPropEditPath = Path.Combine(legacyMkvToolDirectory, "mkvpropedit.exe");
            var ffprobePath = Path.Combine(legacyFfprobeDirectory, "ffprobe.exe");
            File.WriteAllText(mkvMergePath, "tool");
            File.WriteAllText(mkvPropEditPath, "tool");
            File.WriteAllText(ffprobePath, "tool");

            var settingsStore = new AppSettingsStore();
            settingsStore.Save(new CombinedAppSettings
            {
                ToolPaths = new AppToolPathSettings
                {
                    MkvToolNixDirectoryPath = legacyMkvToolDirectory,
                    FfprobePath = ffprobePath,
                    ManagedMkvToolNix = new ManagedToolSettings
                    {
                        AutoManageEnabled = true
                    },
                    ManagedFfprobe = new ManagedToolSettings
                    {
                        AutoManageEnabled = true
                    }
                }
            });

            var viewModel = CreateViewModel(settingsStore: settingsStore);

            Assert.Equal("MKVToolNix bereit (Fallback)", viewModel.MkvToolNixStatusText);
            Assert.Equal("ffprobe bereit (Fallback)", viewModel.FfprobeStatusText);
            Assert.DoesNotContain("übersteuert", viewModel.MkvToolNixStatusTooltip, StringComparison.Ordinal);
            Assert.DoesNotContain("übersteuert", viewModel.FfprobeStatusTooltip, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("USERPROFILE", originalUserProfile);
            Environment.SetEnvironmentVariable("HOME", originalHome);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private AppSettingsWindowViewModel CreateViewModel(
        IEmbyClient? embyClient = null,
        AppSettingsStore? settingsStore = null,
        IManagedToolInstallerService? managedToolInstaller = null)
    {
        settingsStore ??= new AppSettingsStore();
        var services = new AppSettingsModuleServices(
            settingsStore,
            new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(settingsStore)),
            new AppToolPathStore(settingsStore),
            new EpisodeMetadataLookupService(new AppMetadataStore(settingsStore), new ThrowingTvdbClient()),
            new AppEmbySettingsStore(settingsStore),
            new EmbyMetadataSyncService(embyClient ?? new StubEmbyClient(new EmbyServerInfo("Test-Emby", "4.9.0", "emby-1")), new EmbyNfoProviderIdService()),
            managedToolInstaller ?? new RecordingManagedToolInstaller());
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

    private sealed class RecordingManagedToolInstaller : IManagedToolInstallerService
    {
        public int CallCount { get; private set; }

        public Task<ManagedToolStartupResult> EnsureManagedToolsAsync(
            IProgress<ManagedToolStartupProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            progress?.Report(new ManagedToolStartupProgress("Werkzeuge bereit", "Test", 100d, false));
            return Task.FromResult(new ManagedToolStartupResult([]));
        }
    }

    private sealed class CancellableManagedToolInstaller : IManagedToolInstallerService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ManagedToolStartupResult> EnsureManagedToolsAsync(
            IProgress<ManagedToolStartupProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            progress?.Report(new ManagedToolStartupProgress("Werkzeuge werden geprüft", "Test", null, true));
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new ManagedToolStartupResult([]);
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

    private sealed class ThrowingEmbyClient : IEmbyClient
    {
        public Task<IReadOnlyList<EmbyLibraryFolder>> GetLibrariesAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EmbyServerInfo> GetSystemInfoAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("boom");

        public Task TriggerLibraryScanAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task TriggerItemFileScanAsync(AppEmbySettings settings, string itemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EmbyItem?> FindItemByPathAsync(AppEmbySettings settings, string mediaFilePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RefreshItemMetadataAsync(AppEmbySettings settings, string itemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

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
