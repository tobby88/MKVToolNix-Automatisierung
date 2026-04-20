using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

[Collection("PortableStorage")]
public sealed class SingleEpisodeMuxViewModelTests
{
    private readonly PortableStorageFixture _storageFixture;

    public SingleEpisodeMuxViewModelTests(PortableStorageFixture storageFixture)
    {
        _storageFixture = storageFixture;
        _storageFixture.Reset();
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

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 40)]
    [InlineData(100, 80)]
    public void ScaleDetectionProgressForOverallProgress_UsesOnlyDetectionStageSlice(int rawProgress, int expectedProgress)
    {
        Assert.Equal(
            expectedProgress,
            SingleEpisodeMuxViewModel.ScaleDetectionProgressForOverallProgress(rawProgress));
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

    [Fact]
    public void ApplyTvdbSelection_ClearsStalePlanReviewHints()
    {
        var viewModel = CreateViewModel();
        viewModel.SetPlanNotes([
            "In der Bibliothek existiert zusätzlich eine Mehrfachfolge mit demselben Titel (S2014E05-E06). Bitte prüfen, ob die aktuelle Quelle zu einer Doppel- oder Mehrfachfolge gehört."
        ]);

        viewModel.ApplyTvdbSelection(new TvdbEpisodeSelection(42, "Beispielserie", 100, "Mit Pippi Langstrumpf auf der Walz", "01", "04"));

        Assert.False(viewModel.HasPendingPlanReview);
        Assert.False(viewModel.HasActionablePlanNotes);
        Assert.DoesNotContain("Archiv prüfen", viewModel.ReviewHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PersistSingleEpisodeArtifactsIfNeeded_WritesEinzelLogAndMetadataReport_ForNewOutput()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "single-artifact-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        try
        {
            var outputPath = Path.Combine(outputDirectory, "Beispielserie - S01E02 - Pilot.mkv");
            File.WriteAllText(outputPath, "video");
            var viewModel = new SingleEpisodeMuxViewModel(
                ViewModelTestContext.CreateSingleEpisodeServices(batchLogs: new BatchRunLogService()),
                new UserDialogService());
            viewModel.ApplyTvdbSelection(new TvdbEpisodeSelection(42, "Beispielserie", 100, "Pilot", "01", "02"));

            var persistMethod = typeof(SingleEpisodeMuxViewModel).GetMethod(
                "PersistSingleEpisodeArtifactsIfNeeded",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(persistMethod);

            persistMethod!.Invoke(
                viewModel,
                [
                    SeriesEpisodeMuxPlan.CreateSkip("mkvmerge.exe", outputPath, "Pilot", "bereits vorhanden"),
                    false,
                    false
                ]);

            var einzelLogPath = Directory.EnumerateFiles(PortableAppStorage.LogsDirectory, "Einzel - *.log.txt").Single();
            var metadataReportPath = Directory.EnumerateFiles(PortableAppStorage.LogsDirectory, "*.metadata.json").Single();

            Assert.True(File.Exists(einzelLogPath));
            Assert.True(File.Exists(metadataReportPath));
            Assert.Contains("Einzel-Log", File.ReadAllText(einzelLogPath), StringComparison.Ordinal);

            using var metadataDocument = JsonDocument.Parse(File.ReadAllText(metadataReportPath));
            var item = Assert.Single(metadataDocument.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(outputPath, item.GetProperty("outputPath").GetString());
            Assert.Equal("100", item.GetProperty("providerIds").GetProperty("tvdb").GetString());
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
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

        public void ShowError(string message) => throw new NotSupportedException();
    }
}
