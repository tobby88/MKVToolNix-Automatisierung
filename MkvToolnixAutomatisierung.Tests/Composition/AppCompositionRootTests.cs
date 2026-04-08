using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using MkvToolnixAutomatisierung.Composition;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.ViewModels;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Composition;

[Collection("PortableStorage")]
public sealed class AppCompositionRootTests
{
    private readonly PortableStorageFixture _storageFixture;

    public AppCompositionRootTests(PortableStorageFixture storageFixture)
    {
        _storageFixture = storageFixture;
        _storageFixture.Reset();
        ViewModelTestContext.EnsureApplication();
    }

    [Fact]
    public void RegisterAll_ResolvesSingletonServicesViaServiceCollection()
    {
        var services = new ServiceCollection();
        AppCompositionModuleCatalog.RegisterAll(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var first = provider.GetRequiredService<MainWindowViewModel>();
        var second = provider.GetRequiredService<MainWindowViewModel>();
        var archive = provider.GetRequiredService<SeriesArchiveService>();

        Assert.Same(first, second);
        Assert.NotNull(archive);
    }

    [Fact]
    public void Create_ReturnsDisposableAppComposition()
    {
        using var composition = new AppCompositionRoot().Create();

        Assert.NotNull(composition.DialogService);
        Assert.NotNull(composition.SettingsLoadResult);
        Assert.NotNull(composition.MainWindowViewModel);
    }

    [Fact]
    public void CreateComposition_DisposesProvider_WhenStartupResolutionFails()
    {
        var services = new ServiceCollection();
        DisposableProbe? probe = null;
        services.AddSingleton<IUserDialogService, StubUserDialogService>();
        services.AddSingleton(new AppSettingsLoadResult(new CombinedAppSettings(), AppSettingsLoadStatus.LoadedDefaultsNoFile));
        services.AddSingleton(_ =>
        {
            probe = new DisposableProbe();
            return probe;
        });
        services.AddSingleton<MainWindowViewModel>(provider =>
        {
            _ = provider.GetRequiredService<DisposableProbe>();
            throw new InvalidOperationException("boom");
        });

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var exception = Assert.Throws<InvalidOperationException>(() => AppCompositionRoot.CreateComposition(provider));

        Assert.Equal("boom", exception.Message);
        Assert.NotNull(probe);
        Assert.True(probe!.IsDisposed);
    }

    private sealed class DisposableProbe : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class StubUserDialogService : IUserDialogService
    {
        public string? SelectMainVideo(string initialDirectory) => throw new NotSupportedException();

        public string? SelectAudioDescription(string initialDirectory) => throw new NotSupportedException();

        public string[]? SelectSubtitles(string initialDirectory) => throw new NotSupportedException();

        public string[]? SelectAttachments(string initialDirectory) => throw new NotSupportedException();

        public string? SelectOutput(string initialDirectory, string fileName) => throw new NotSupportedException();

        public string? SelectFolder(string title, string initialDirectory) => throw new NotSupportedException();

        public string? SelectExecutable(string title, string filter, string initialDirectory) => throw new NotSupportedException();

        public MessageBoxResult AskAudioDescriptionChoice() => throw new NotSupportedException();

        public MessageBoxResult AskSubtitlesChoice() => throw new NotSupportedException();

        public MessageBoxResult AskAttachmentChoice() => throw new NotSupportedException();

        public bool ConfirmMuxStart() => throw new NotSupportedException();

        public bool ConfirmBatchExecution(int itemCount, int archiveFileCount, long archiveTotalBytes) => throw new NotSupportedException();

        public bool ConfirmArchiveCopy(FileCopyPlan copyPlan) => throw new NotSupportedException();

        public bool ConfirmSingleEpisodeCleanup(IReadOnlyList<string> usedFiles, IReadOnlyList<string> unusedFiles) => throw new NotSupportedException();

        public bool ConfirmBatchRecycleDoneFiles(int fileCount, string doneDirectory) => throw new NotSupportedException();

        public bool AskOpenDoneDirectory(string doneDirectory) => throw new NotSupportedException();

        public bool TryOpenFilesWithDefaultApp(IEnumerable<string> filePaths) => throw new NotSupportedException();

        public void OpenPathWithDefaultApp(string path) => throw new NotSupportedException();

        public MessageBoxResult AskSourceReviewResult(string fileName, bool canTryAlternative) => throw new NotSupportedException();

        public void ShowInfo(string title, string message) => throw new NotSupportedException();

        public void ShowWarning(string title, string message) => throw new NotSupportedException();

        public void ShowError(string message) => throw new NotSupportedException();
    }
}
