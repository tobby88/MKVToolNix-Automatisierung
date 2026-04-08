using System.IO;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.ViewModels;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

[Collection("PortableStorage")]
public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly PortableStorageFixture _storageFixture;
    private readonly string _tempDirectory;

    public MainWindowViewModelTests(PortableStorageFixture storageFixture)
    {
        _storageFixture = storageFixture;
        _storageFixture.Reset();
        ViewModelTestContext.EnsureApplication();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-main-window-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void Constructor_PreservesConfiguredToolPaths_WhenToolsTemporarilyUnavailable()
    {
        var configuredFfprobePath = Path.Combine(_tempDirectory, "ffmpeg", "ffprobe.exe");
        var configuredMkvToolDirectory = Path.Combine(_tempDirectory, "mkvtoolnix");
        var toolPathStore = CreateToolPathStore();
        toolPathStore.Save(new AppToolPathSettings
        {
            FfprobePath = configuredFfprobePath,
            MkvToolNixDirectoryPath = configuredMkvToolDirectory
        });

        var viewModel = CreateViewModel(
            toolPathStore,
            new StubFfprobeLocator(null),
            new StubMkvToolNixLocator(new FileNotFoundException("Probe fehlgeschlagen.")));

        var savedSettings = toolPathStore.Load();

        Assert.Equal(configuredFfprobePath, savedSettings.FfprobePath);
        Assert.Equal(configuredMkvToolDirectory, savedSettings.MkvToolNixDirectoryPath);
        Assert.False(viewModel.IsFfprobeAvailable);
        Assert.False(viewModel.IsMkvToolNixAvailable);
        Assert.Equal(configuredFfprobePath, viewModel.FfprobePath);
        Assert.Equal(Path.Combine(configuredMkvToolDirectory, "mkvmerge.exe"), viewModel.MkvMergePath);
        Assert.Contains(configuredFfprobePath, viewModel.MediaProbeStatusTooltip, StringComparison.Ordinal);
        Assert.Contains("Der gespeicherte Pfad bleibt erhalten", viewModel.MediaProbeStatusTooltip, StringComparison.Ordinal);
        Assert.Contains("Probe fehlgeschlagen.", viewModel.MkvToolNixStatusTooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_PersistsResolvedToolPaths_WhenLocatorsFindWorkingExecutables()
    {
        var ffprobePath = CreateFile(Path.Combine("ffmpeg", "ffprobe.exe"));
        var mkvMergePath = CreateFile(Path.Combine("mkvtoolnix", "mkvmerge.exe"));
        var toolPathStore = CreateToolPathStore();
        toolPathStore.Save(new AppToolPathSettings
        {
            FfprobePath = Path.Combine(_tempDirectory, "stale", "ffprobe.exe"),
            MkvToolNixDirectoryPath = Path.Combine(_tempDirectory, "stale-mkvtoolnix")
        });

        var viewModel = CreateViewModel(
            toolPathStore,
            new StubFfprobeLocator(ffprobePath),
            new StubMkvToolNixLocator(mkvMergePath));

        var savedSettings = toolPathStore.Load();

        Assert.True(viewModel.IsFfprobeAvailable);
        Assert.True(viewModel.IsMkvToolNixAvailable);
        Assert.Equal(ffprobePath, viewModel.FfprobePath);
        Assert.Equal(mkvMergePath, viewModel.MkvMergePath);
        Assert.Equal(ffprobePath, savedSettings.FfprobePath);
        Assert.Equal(Path.GetDirectoryName(mkvMergePath), savedSettings.MkvToolNixDirectoryPath);
    }

    [Fact]
    public void UpdateArchiveRootDirectory_RefreshesArchiveStatus_AndNotifiesArchiveAwareModules()
    {
        var archiveAwareModule = new StubArchiveConfigurationAwareModule();
        var archiveRoot = Path.Combine(_tempDirectory, "archive-ready");
        Directory.CreateDirectory(archiveRoot);
        var viewModel = CreateViewModel(
            CreateToolPathStore(),
            new StubFfprobeLocator(null),
            new StubMkvToolNixLocator(new FileNotFoundException("Probe fehlgeschlagen.")),
            [new ModuleNavigationItem("Einzelepisode", "Erkennen", archiveAwareModule)]);

        viewModel.UpdateArchiveRootDirectory(archiveRoot);

        Assert.Equal(archiveRoot, viewModel.ArchiveRootDirectory);
        Assert.True(viewModel.IsArchiveAvailable);
        Assert.Equal(1, archiveAwareModule.CallCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private AppToolPathStore CreateToolPathStore()
    {
        return new AppToolPathStore(new AppSettingsStore());
    }

    private MainWindowViewModel CreateViewModel(
        AppToolPathStore toolPathStore,
        IFfprobeLocator ffprobeLocator,
        IMkvToolNixLocator mkvToolNixLocator,
        IReadOnlyList<ModuleNavigationItem>? modules = null)
    {
        var services = ViewModelTestContext.CreateMainWindowServices(
            toolPathStore,
            ffprobeLocator: ffprobeLocator,
            mkvToolNixLocator: mkvToolNixLocator);
        services.Archive.ConfigureArchiveRootDirectory(_tempDirectory);

        return new MainWindowViewModel(
            modules ?? [new ModuleNavigationItem("Einzelepisode", "Erkennen", new object())],
            services,
            new UserDialogService());
    }

    private string CreateFile(string relativePath, string content = "tool")
    {
        var path = Path.Combine(_tempDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class StubFfprobeLocator(string? resolvedPath) : IFfprobeLocator
    {
        public string? TryFindFfprobePath()
        {
            return resolvedPath;
        }
    }

    private sealed class StubMkvToolNixLocator(string? resolvedPath = null, Exception? exception = null) : IMkvToolNixLocator
    {
        public StubMkvToolNixLocator(Exception exception)
            : this(null, exception)
        {
        }

        public string FindMkvMergePath()
        {
            if (exception is not null)
            {
                throw exception;
            }

            return resolvedPath ?? throw new InvalidOperationException("Kein mkvmerge-Pfad gesetzt.");
        }
    }

    private sealed class StubArchiveConfigurationAwareModule : IArchiveConfigurationAwareModule
    {
        public int CallCount { get; private set; }

        public void HandleArchiveConfigurationChanged()
        {
            CallCount++;
        }
    }
}
