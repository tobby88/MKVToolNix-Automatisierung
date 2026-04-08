using System.IO;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class SingleEpisodeMuxViewModelTests
{
    public SingleEpisodeMuxViewModelTests()
    {
        ViewModelTestContext.EnsureApplication();
    }

    [Fact]
    public void ShouldPreserveManualTitle_ReturnsTrue_ForSameDetectionSeedPath()
    {
        var result = SingleEpisodeManualTitlePolicy.ShouldPreserve(
            currentTitle: "Manueller Titel",
            lastSuggestedTitle: "Automatischer Titel",
            detectionSeedPath: @"C:\Temp\episode.mp4",
            mainVideoPath: @"C:\Temp\episode-alt.mp4",
            selectedVideoPath: @"C:\Temp\episode.mp4");

        Assert.True(result);
    }

    [Fact]
    public void ShouldPreserveManualTitle_ReturnsFalse_ForNewSelection()
    {
        var result = SingleEpisodeManualTitlePolicy.ShouldPreserve(
            currentTitle: "Manueller Titel",
            lastSuggestedTitle: "Automatischer Titel",
            detectionSeedPath: @"C:\Temp\episode-alt.mp4",
            mainVideoPath: @"C:\Temp\episode-alt.mp4",
            selectedVideoPath: @"C:\Temp\episode-neu.mp4");

        Assert.False(result);
    }

    [Fact]
    public void ShouldPreserveManualTitle_ReturnsFalse_WhenTitleMatchesLastSuggestion()
    {
        var result = SingleEpisodeManualTitlePolicy.ShouldPreserve(
            currentTitle: "Titel aus Erkennung",
            lastSuggestedTitle: "Titel aus Erkennung",
            detectionSeedPath: @"C:\Temp\episode.mp4",
            mainVideoPath: @"C:\Temp\episode.mp4",
            selectedVideoPath: @"C:\Temp\episode.mp4");

        Assert.False(result);
    }

    [Fact]
    public void ResolveMetadataBadgeState_ReturnsOpen_WhenAutomaticLookupWasSkipped()
    {
        var badgeState = EpisodeUiStateResolver.ResolveMetadataBadgeState(
            hasPendingMetadataReview: false,
            isMetadataReviewApproved: false);

        Assert.Equal(MetadataBadgeState.Open, badgeState);
    }

    [Fact]
    public void ResolveMetadataBadgeState_ReturnsApproved_WhenMetadataWasFreigegeben()
    {
        var badgeState = EpisodeUiStateResolver.ResolveMetadataBadgeState(
            hasPendingMetadataReview: false,
            isMetadataReviewApproved: true);

        Assert.Equal(MetadataBadgeState.Approved, badgeState);
    }

    [Fact]
    public void SubtitleDisplayText_ReturnsOnlyFileNames()
    {
        var viewModel = CreateViewModel();
        viewModel.SetSubtitles(
        [
            @"C:\Temp\untertitel-a.srt",
            @"D:\Andere\untertitel-b.ass"
        ]);

        Assert.Equal(
            "untertitel-a.srt" + Environment.NewLine + "untertitel-b.ass",
            viewModel.SubtitleDisplayText);
    }

    [Fact]
    public void AttachmentDisplayText_ReturnsOnlyFileNames()
    {
        var viewModel = CreateViewModel();
        viewModel.SetAttachments(
        [
            @"C:\Temp\infos-a.txt",
            @"D:\Andere\infos-b.txt"
        ]);

        Assert.Equal(
            "infos-a.txt" + Environment.NewLine + "infos-b.txt",
            viewModel.AttachmentDisplayText);
    }

    [Fact]
    public void CancelCurrentOperationCommand_IsDisabled_WhenNoSingleOperationRuns()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.CanCancelCurrentOperation);
        Assert.False(viewModel.CancelCurrentOperationCommand.CanExecute(null));
    }

    [Fact]
    public void SelectOutputCommand_UsesCurrentOutputDirectory_AsDialogStart()
    {
        var dialogService = new CapturingDialogService();
        var viewModel = new SingleEpisodeMuxViewModel(
            ViewModelTestContext.CreateSingleEpisodeServices(),
            dialogService);
        var expectedDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-single-output");

        viewModel.SetOutputPath(Path.Combine(expectedDirectory, "Folge.mkv"));

        viewModel.SelectOutputCommand.Execute(null);

        Assert.Equal(expectedDirectory, dialogService.LastOutputInitialDirectory);
        Assert.Equal("Folge.mkv", dialogService.LastOutputFileName);
    }

    private static SingleEpisodeMuxViewModel CreateViewModel()
    {
        return new SingleEpisodeMuxViewModel(
            ViewModelTestContext.CreateSingleEpisodeServices(),
            new UserDialogService());
    }

    private sealed class CapturingDialogService : IUserDialogService
    {
        public string? LastOutputInitialDirectory { get; private set; }

        public string? LastOutputFileName { get; private set; }

        public string? SelectMainVideo(string initialDirectory) => throw new NotSupportedException();

        public string? SelectAudioDescription(string initialDirectory) => throw new NotSupportedException();

        public string[]? SelectSubtitles(string initialDirectory) => throw new NotSupportedException();

        public string[]? SelectAttachments(string initialDirectory) => throw new NotSupportedException();

        public string? SelectOutput(string initialDirectory, string fileName)
        {
            LastOutputInitialDirectory = initialDirectory;
            LastOutputFileName = fileName;
            return null;
        }

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
