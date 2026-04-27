using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class DownloadViewModelTests
{
    [Fact]
    public void StartMediathekViewCommand_StartsResolvedToolAndUpdatesStatus()
    {
        var resolvedPath = new ResolvedToolPath(@"C:\Tools\MediathekView\MediathekView.exe", ToolPathResolutionSource.ManualOverride);
        var launcher = new FakeMediathekViewLauncher
        {
            ResolvedPath = resolvedPath,
            LaunchResult = MediathekViewLaunchResult.Started(resolvedPath)
        };
        var viewModel = CreateViewModel(launcher);

        viewModel.StartMediathekViewCommand.Execute(null);

        Assert.Equal(1, launcher.LaunchCount);
        Assert.True(viewModel.IsMediathekViewAvailable);
        Assert.Contains(resolvedPath.Path, viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartMediathekViewCommand_ShowsWarningWhenToolIsMissing()
    {
        var launcher = new FakeMediathekViewLauncher
        {
            LaunchResult = MediathekViewLaunchResult.NotFound()
        };
        var dialogService = new CapturingDialogService();
        var viewModel = CreateViewModel(launcher, dialogService);

        viewModel.StartMediathekViewCommand.Execute(null);

        Assert.Equal(1, dialogService.WarningCount);
        Assert.False(viewModel.IsMediathekViewAvailable);
        Assert.Contains("nicht gefunden", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpenToolSettingsCommand_OpensToolsPageAndRefreshesStatusAfterAcceptedSettings()
    {
        var launcher = new FakeMediathekViewLauncher();
        var resolvedPath = new ResolvedToolPath(@"C:\Portable\MediathekView.exe", ToolPathResolutionSource.DownloadsFallback);
        var settingsDialog = new FakeSettingsDialog(() => launcher.ResolvedPath = resolvedPath);
        var viewModel = CreateViewModel(launcher, settingsDialog: settingsDialog);

        viewModel.OpenToolSettingsCommand.Execute(null);

        Assert.Equal(AppSettingsPage.Tools, settingsDialog.LastInitialPage);
        Assert.True(viewModel.IsMediathekViewAvailable);
        Assert.Equal(resolvedPath.Path, viewModel.MediathekViewPathText);
    }

    private static DownloadViewModel CreateViewModel(
        FakeMediathekViewLauncher launcher,
        IUserDialogService? dialogService = null,
        IAppSettingsDialogService? settingsDialog = null)
    {
        return new DownloadViewModel(
            new DownloadModuleServices(
                launcher,
                settingsDialog ?? new FakeSettingsDialog()),
            dialogService ?? new CapturingDialogService());
    }

    private sealed class FakeMediathekViewLauncher : IMediathekViewLauncher
    {
        public ResolvedToolPath? ResolvedPath { get; set; }

        public MediathekViewLaunchResult? LaunchResult { get; set; }

        public int LaunchCount { get; private set; }

        public ResolvedToolPath? TryResolve()
        {
            return ResolvedPath;
        }

        public MediathekViewLaunchResult Launch()
        {
            LaunchCount++;
            return LaunchResult ?? MediathekViewLaunchResult.NotFound();
        }
    }

    private sealed class FakeSettingsDialog(Action? onAccept = null) : IAppSettingsDialogService
    {
        public AppSettingsPage? LastInitialPage { get; private set; }

        public bool ShowDialog(Window? owner = null, AppSettingsPage initialPage = AppSettingsPage.Archive)
        {
            LastInitialPage = initialPage;
            onAccept?.Invoke();
            return true;
        }
    }

    private sealed class CapturingDialogService : IUserDialogService
    {
        public int WarningCount { get; private set; }

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

        public bool ConfirmArchiveCopy(FileCopyPlan copyPlan) => false;

        public bool ConfirmSingleEpisodeCleanup(IReadOnlyList<string> usedFiles, IReadOnlyList<string> unusedFiles) => false;

        public bool ConfirmBatchRecycleDoneFiles(int fileCount, string doneDirectory) => false;

        public bool AskOpenDoneDirectory(string doneDirectory) => false;

        public bool ConfirmPlanReview(string episodeTitle, string reviewText) => false;

        public bool TryOpenFilesWithDefaultApp(IEnumerable<string> filePaths) => false;

        public void OpenPathWithDefaultApp(string path) { }

        public MessageBoxResult AskSourceReviewResult(string fileName, bool canTryAlternative) => MessageBoxResult.Cancel;

        public void ShowInfo(string title, string message) { }

        public void ShowWarning(string title, string message)
        {
            WarningCount++;
        }

        public void ShowError(string message) { }
    }
}
