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
    public void LanguageOverrides_NormalizeCodesAndExposeEffectiveOriginalLanguage()
    {
        var viewModel = CreateViewModel();
        viewModel.ApplyTvdbSelection(new TvdbEpisodeSelection(42, "Beispielserie", 100, "Pilot", "01", "02", OriginalLanguage: "deu"));

        viewModel.SetVideoLanguageOverride("eng");
        viewModel.SetAudioLanguageOverride("en-US");
        viewModel.SetOriginalLanguageOverride("en");

        Assert.Equal("en", viewModel.VideoLanguageOverride);
        Assert.Equal("en", viewModel.AudioLanguageOverride);
        Assert.Equal("en", viewModel.OriginalLanguageOverride);
        Assert.Equal("en", viewModel.EffectiveOriginalLanguage);

        viewModel.SetOriginalLanguageOverride(null);

        Assert.Equal(string.Empty, viewModel.OriginalLanguageOverride);
        Assert.Equal("de", viewModel.EffectiveOriginalLanguage);
    }

    [Fact]
    public void ApplyTvdbSelection_PreservesForeignOriginalLanguage_ForOriginalFlagComparison()
    {
        var viewModel = CreateViewModel();

        viewModel.ApplyTvdbSelection(new TvdbEpisodeSelection(
            42,
            "Pettersson und Findus",
            100,
            "Findus zieht um",
            "00",
            "03",
            OriginalLanguage: "swe"));

        Assert.Equal("swe", viewModel.EffectiveOriginalLanguage);
    }

    [Fact]
    public void CancelCurrentOperationCommand_IsDisabled_WhenNoSingleOperationRuns()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.CanCancelCurrentOperation);
        Assert.False(viewModel.CancelCurrentOperationCommand.CanExecute(null));
    }

    [Fact]
    public void TestSelectedSourcesCommand_ReraisesCanExecute_WhenAudioDescriptionChangesManualCheckFiles()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "single-manual-check-command", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var audioDescriptionPath = Path.Combine(tempDirectory, "ad.mp4");
            File.WriteAllText(audioDescriptionPath, "ad");
            File.WriteAllText(Path.ChangeExtension(audioDescriptionPath, ".txt"), "Sender: SRF");
            var viewModel = CreateViewModel();
            var canExecuteChangedCount = 0;
            viewModel.TestSelectedSourcesCommand.CanExecuteChanged += (_, _) => canExecuteChangedCount++;

            Assert.False(viewModel.TestSelectedSourcesCommand.CanExecute(null));

            viewModel.SetAudioDescription(audioDescriptionPath);

            Assert.True(viewModel.TestSelectedSourcesCommand.CanExecute(null));
            Assert.True(canExecuteChangedCount > 0);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void SelectOutputCommand_UsesCurrentOutputDirectory_AsDialogStart()
    {
        var dialogService = new CapturingDialogService();
        var viewModel = new SingleEpisodeMuxViewModel(
            ViewModelTestContext.CreateSingleEpisodeServices(),
            dialogService);
        var expectedDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-single-output");
        Directory.CreateDirectory(expectedDirectory);

        viewModel.SetOutputPath(Path.Combine(expectedDirectory, "Folge.mkv"));

        viewModel.SelectOutputCommand.Execute(null);

        Assert.Equal(expectedDirectory, dialogService.LastOutputInitialDirectory);
        Assert.Equal("Folge.mkv", dialogService.LastOutputFileName);
    }

    [Fact]
    public void SelectOutputCommand_UsesNearestExistingDirectory_WhenCurrentOutputDirectoryIsMissing()
    {
        var dialogService = new CapturingDialogService();
        var viewModel = new SingleEpisodeMuxViewModel(
            ViewModelTestContext.CreateSingleEpisodeServices(),
            dialogService);
        var existingParent = Path.Combine(Path.GetTempPath(), "mkv-auto-single-output", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(existingParent);
        var missingDirectory = Path.Combine(existingParent, "fehlt", "noch");

        viewModel.SetOutputPath(Path.Combine(missingDirectory, "Folge.mkv"));

        viewModel.SelectOutputCommand.Execute(null);

        Assert.Equal(existingParent, dialogService.LastOutputInitialDirectory);
        Assert.Equal("Folge.mkv", dialogService.LastOutputFileName);
    }

    [Fact]
    public void EpisodePlanInputSnapshot_KeepsValuesStable_WhenSourceModelChangesLater()
    {
        var viewModel = CreateViewModel();
        SetNonPublicProperty(viewModel, nameof(EpisodeEditModel.HasPrimaryVideoSource), true);
        SetNonPublicProperty(viewModel, nameof(EpisodeEditModel.MainVideoPath), @"C:\Temp\alt.mp4");
        viewModel.SetOutputPath(@"C:\Temp\alt.mkv");
        viewModel.Title = "Alt";

        var snapshot = EpisodePlanInputSnapshot.Create(viewModel);

        SetNonPublicProperty(viewModel, nameof(EpisodeEditModel.MainVideoPath), @"C:\Temp\neu.mp4");
        viewModel.SetOutputPath(@"C:\Temp\neu.mkv");
        viewModel.Title = "Neu";

        Assert.Equal(@"C:\Temp\alt.mp4", snapshot.MainVideoPath);
        Assert.Equal(@"C:\Temp\alt.mkv", snapshot.OutputPath);
        Assert.Equal("Alt", snapshot.TitleForMux);
    }

    [Fact]
    public void OpenFileCommands_OpenTheConfiguredSourceComponents()
    {
        var dialogService = new CapturingDialogService();
        var viewModel = new SingleEpisodeMuxViewModel(
            ViewModelTestContext.CreateSingleEpisodeServices(),
            dialogService);
        var outputPath = Path.Combine(Path.GetTempPath(), "single-open-tests", Guid.NewGuid().ToString("N"), "output.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, "archive");

        SetNonPublicProperty(viewModel, nameof(EpisodeEditModel.MainVideoPath), @"C:\Temp\haupt.mp4");
        InvokeSetAdditionalVideoPaths(viewModel, [@"C:\Temp\weitere-spur.mp4"]);
        viewModel.SetAudioDescription(@"C:\Temp\ad.mp4");
        viewModel.SetSubtitles([@"C:\Temp\untertitel.srt", @"C:\Temp\untertitel.ass"]);
        viewModel.SetAttachments([@"C:\Temp\metadaten.txt"]);
        viewModel.SetOutputPath(outputPath);

        viewModel.OpenMainVideoCommand.Execute(null);
        viewModel.OpenAudioDescriptionCommand.Execute(null);
        viewModel.OpenSubtitlesCommand.Execute(null);
        viewModel.OpenAttachmentsCommand.Execute(null);
        Assert.True(viewModel.OpenOutputCommand.CanExecute(null));
        viewModel.OpenOutputCommand.Execute(null);

        Assert.Equal(
        [
            @"C:\Temp\haupt.mp4",
            @"C:\Temp\weitere-spur.mp4",
            @"C:\Temp\ad.mp4",
            @"C:\Temp\untertitel.ass",
            @"C:\Temp\untertitel.srt",
            @"C:\Temp\metadaten.txt",
            outputPath
        ],
            dialogService.OpenedFilePaths);
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
    public void ApprovePlanReviewCommand_ReraisesCanExecute_WhenChangedPlanNeedsReviewAgain()
    {
        var viewModel = CreateViewModel();
        viewModel.SetPlanNotes([
            "Mehrfachfolge erkannt. Bitte prüfen, ob die aktuelle Quelle zu einer Doppel- oder Mehrfachfolge gehört."
        ]);

        Assert.True(viewModel.ApprovePlanReviewCommand.CanExecute(null));
        viewModel.ApprovePlanReviewCommand.Execute(null);
        Assert.False(viewModel.ApprovePlanReviewCommand.CanExecute(null));

        var canExecuteChangedCount = 0;
        viewModel.ApprovePlanReviewCommand.CanExecuteChanged += (_, _) => canExecuteChangedCount++;

        viewModel.SetPlanNotes([
            "In der Bibliothek existiert zusätzlich ein Archivtreffer mit anderer Laufzeit. Bitte prüfen, ob die aktuelle Quelle dieses Ziel ersetzen soll."
        ]);

        Assert.True(viewModel.HasPendingPlanReview);
        Assert.True(viewModel.ApprovePlanReviewCommand.CanExecute(null));
        Assert.True(canExecuteChangedCount > 0);
    }

    [Fact]
    public async Task BuildFreshPlanAsync_SubtitleOnlyWithoutPrimary_DoesNotRequireAudioDescription()
    {
        var viewModel = CreateViewModel();
        var subtitlePath = Path.Combine(Path.GetTempPath(), "single-subtitle-only", "Pilot.ass");

        SetNonPublicProperty(viewModel, nameof(EpisodeEditModel.HasPrimaryVideoSource), false);
        SetNonPublicProperty(viewModel, nameof(EpisodeEditModel.MainVideoPath), subtitlePath);
        viewModel.SetSubtitles([subtitlePath]);
        SetNonPublicProperty(viewModel, nameof(EpisodeEditModel.OutputPath), Path.Combine(Path.GetTempPath(), "single-subtitle-only", "Pilot.mkv"));
        viewModel.Title = string.Empty;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeBuildFreshPlanAsync(viewModel));

        Assert.Equal("Bitte einen Dateititel eingeben.", exception.Message);
    }

    [Fact]
    public async Task RefreshPlanSummaryImmediatelyAsync_ClearsStalePlanPresentation_WhenRefreshFails()
    {
        var viewModel = CreateViewModel();
        var outputPath = Path.Combine(Path.GetTempPath(), "single-plan-refresh", "Pilot.mkv");

        SetNonPublicProperty(viewModel, nameof(EpisodeEditModel.HasPrimaryVideoSource), true);
        SetNonPublicProperty(viewModel, nameof(EpisodeEditModel.MainVideoPath), @"C:\Temp\episode.mp4");
        SetNonPublicProperty(viewModel, nameof(EpisodeEditModel.OutputPath), outputPath);
        viewModel.Title = string.Empty;
        viewModel.SetPlanSummary("Veraltete Zusammenfassung");
        viewModel.SetUsageSummary(EpisodeUsageSummary.CreatePending("Alt", "Bleibt fälschlich sichtbar"));
        viewModel.SetPlanNotes(["Veralteter Prüfhinweis"]);
        SetNonPublicProperty(viewModel, nameof(SingleEpisodeMuxViewModel.OutputTargetStatusText), "Veralteter Planstatus");

        var updated = await InvokeRefreshPlanSummaryImmediatelyAsync(viewModel);

        Assert.False(updated);
        Assert.Equal(string.Empty, viewModel.PlanSummaryText);
        Assert.Null(viewModel.UsageSummary);
        Assert.False(viewModel.HasNotes);
        Assert.False(viewModel.HasPendingPlanReview);
        Assert.Contains("Plan konnte gerade nicht aktualisiert werden", viewModel.PlanRefreshProblemText, StringComparison.Ordinal);
        Assert.Equal("Die Zieldatei existiert noch nicht.", viewModel.OutputTargetStatusText);
    }

    [Fact]
    public void ApplyPlanPresentation_ShowsProminentUpToDateStatus_ForSkipPlan()
    {
        var viewModel = CreateViewModel();
        var plan = SeriesEpisodeMuxPlan.CreateSkip(
            "mkvmerge.exe",
            Path.Combine(Path.GetTempPath(), "single-skip", "Pilot.mkv"),
            "Pilot",
            "Die Zieldatei ist bereits vollständig.");

        InvokeApplyPlanPresentation(viewModel, plan);

        Assert.Equal(SingleEpisodeExecutionStatusKind.UpToDate, viewModel.ExecutionStatusKind);
        Assert.Equal("Ziel aktuell", viewModel.ExecutionStatusBadgeText);
        Assert.Equal("#EEF6E8", viewModel.ExecutionStatusBadgeBackground);
        Assert.Equal("Die Zieldatei ist bereits vollständig.", viewModel.OutputTargetStatusText);
        Assert.True(viewModel.HasSingleEpisodeStatusBand);
    }

    [Fact]
    public void SingleExecutionStatusBadge_ExplainsOpenComparisonWithoutRunningState()
    {
        Assert.Equal(
            "Vergleich offen",
            EpisodeEditTextBuilder.BuildSingleExecutionStatusText(SingleEpisodeExecutionStatusKind.ComparisonPending));
        Assert.Equal(
            "#E8F3FF",
            EpisodeUiStyleBuilder.BuildSingleExecutionStatusBadgeBackground(SingleEpisodeExecutionStatusKind.ComparisonPending));
    }

    [Fact]
    public void ClearCompletedSingleEpisodeInput_ClearsDialogButKeepsFinalRunStatus()
    {
        var viewModel = CreateViewModel();
        SetNonPublicProperty(viewModel, nameof(EpisodeEditModel.HasPrimaryVideoSource), true);
        SetNonPublicProperty(viewModel, nameof(EpisodeEditModel.MainVideoPath), @"C:\Temp\haupt.mp4");
        InvokeSetAdditionalVideoPaths(viewModel, [@"C:\Temp\zweite-spur.mp4"]);
        viewModel.SetAudioDescription(@"C:\Temp\ad.mp4");
        viewModel.SetSubtitles([@"C:\Temp\untertitel.srt"]);
        viewModel.SetAttachments([@"C:\Temp\info.txt"]);
        viewModel.SetOutputPath(@"C:\Temp\ziel.mkv");
        viewModel.SeriesName = "Beispielserie";
        viewModel.SeasonNumber = "01";
        viewModel.EpisodeNumber = "02";
        viewModel.Title = "Pilot";
        viewModel.SetPlanSummary("Alter Plan");
        viewModel.SetUsageSummary(EpisodeUsageSummary.CreatePending("Alt", "Veraltete Nutzung"));
        viewModel.SetPlanNotes(["Veralteter Hinweis"]);
        SetNonPublicProperty(viewModel, nameof(SingleEpisodeMuxViewModel.PreviewText), "Alter mkvmerge-Output");

        InvokeClearCompletedSingleEpisodeInput(
            viewModel,
            "Muxing erfolgreich abgeschlossen",
            SingleEpisodeExecutionStatusKind.Success);

        Assert.Equal(string.Empty, viewModel.MainVideoPath);
        Assert.Empty(viewModel.AdditionalVideoPaths);
        Assert.Equal(string.Empty, viewModel.AudioDescriptionPath);
        Assert.Empty(viewModel.SubtitlePaths);
        Assert.Empty(viewModel.AttachmentPaths);
        Assert.Equal(string.Empty, viewModel.OutputPath);
        Assert.Equal(string.Empty, viewModel.SeriesName);
        Assert.Equal("xx", viewModel.SeasonNumber);
        Assert.Equal("xx", viewModel.EpisodeNumber);
        Assert.Equal(string.Empty, viewModel.Title);
        Assert.False(viewModel.HasPlanSummary);
        Assert.Null(viewModel.UsageSummary);
        Assert.False(viewModel.HasOutputTargetStatus);
        Assert.Equal(string.Empty, viewModel.PreviewText);
        Assert.Equal("Muxing erfolgreich abgeschlossen", viewModel.StatusText);
        Assert.Equal(100, viewModel.ProgressValue);
        Assert.Equal(SingleEpisodeExecutionStatusKind.Success, viewModel.ExecutionStatusKind);
        Assert.Equal("Erfolgreich", viewModel.ExecutionStatusBadgeText);
        Assert.True(viewModel.HasSingleEpisodeStatusBand);
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

    [Fact]
    public void OpenSingleEpisodeRunArtifactIfAvailable_OpensPreferredNewOutputList()
    {
        var dialogService = new CapturingDialogService();
        var viewModel = new SingleEpisodeMuxViewModel(
            ViewModelTestContext.CreateSingleEpisodeServices(),
            dialogService);
        var listPath = Path.Combine(Path.GetTempPath(), "single-new-files.txt");
        var result = new BatchRunLogSaveResult(
            BatchLogPath: Path.Combine(Path.GetTempPath(), "single.log.txt"),
            NewOutputListPath: listPath,
            NewOutputMetadataReportPath: null,
            NewOutputFiles: [@"C:\Temp\Pilot.mkv"]);

        InvokeOpenSingleEpisodeRunArtifactIfAvailable(viewModel, result);

        Assert.Equal([listPath], dialogService.OpenedPaths);
    }

    private static SingleEpisodeMuxViewModel CreateViewModel()
    {
        return new SingleEpisodeMuxViewModel(
            ViewModelTestContext.CreateSingleEpisodeServices(),
            new UserDialogService());
    }

    private static Task<SeriesEpisodeMuxPlan> InvokeBuildFreshPlanAsync(SingleEpisodeMuxViewModel viewModel)
    {
        var method = typeof(SingleEpisodeMuxViewModel).GetMethod(
            "BuildFreshPlanAsync",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(CancellationToken)],
            modifiers: null);
        Assert.NotNull(method);
        return Assert.IsType<Task<SeriesEpisodeMuxPlan>>(method!.Invoke(viewModel, [CancellationToken.None]));
    }

    private static Task<bool> InvokeRefreshPlanSummaryImmediatelyAsync(SingleEpisodeMuxViewModel viewModel)
    {
        var method = typeof(SingleEpisodeMuxViewModel).GetMethod(
            "RefreshPlanSummaryImmediatelyAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<Task<bool>>(method!.Invoke(viewModel, [CancellationToken.None]));
    }

    private static void InvokeApplyPlanPresentation(SingleEpisodeMuxViewModel viewModel, SeriesEpisodeMuxPlan plan)
    {
        var method = typeof(SingleEpisodeMuxViewModel).GetMethod(
            "ApplyPlanPresentation",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(viewModel, [plan]);
    }

    private static void InvokeClearCompletedSingleEpisodeInput(
        SingleEpisodeMuxViewModel viewModel,
        string finalStatusText,
        SingleEpisodeExecutionStatusKind finalStatusKind)
    {
        var method = typeof(SingleEpisodeMuxViewModel).GetMethod(
            "ClearCompletedSingleEpisodeInput",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(viewModel, [finalStatusText, finalStatusKind]);
    }

    private static void InvokeOpenSingleEpisodeRunArtifactIfAvailable(
        SingleEpisodeMuxViewModel viewModel,
        BatchRunLogSaveResult result)
    {
        var method = typeof(SingleEpisodeMuxViewModel).GetMethod(
            "OpenSingleEpisodeRunArtifactIfAvailable",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(viewModel, [result]);
    }

    private static void SetNonPublicProperty(object target, string propertyName, object? value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        var setter = property!.GetSetMethod(nonPublic: true);
        Assert.NotNull(setter);
        setter!.Invoke(target, [value]);
    }

    private static void InvokeSetAdditionalVideoPaths(EpisodeEditModel viewModel, IEnumerable<string> paths)
    {
        var method = typeof(EpisodeEditModel).GetMethod(
            "SetAdditionalVideoPaths",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(viewModel, [paths]);
    }

    private sealed class CapturingDialogService : IUserDialogService
    {
        public string? LastOutputInitialDirectory { get; private set; }

        public string? LastOutputFileName { get; private set; }

        public List<string> OpenedFilePaths { get; } = [];

        public List<string> OpenedPaths { get; } = [];

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

        public bool TryOpenFilesWithDefaultApp(IEnumerable<string> filePaths)
        {
            OpenedFilePaths.AddRange(filePaths);
            return true;
        }

        public void OpenPathWithDefaultApp(string path) => OpenedPaths.Add(path);

        public MessageBoxResult AskSourceReviewResult(string fileName, bool canTryAlternative) => throw new NotSupportedException();

        public void ShowInfo(string title, string message) => throw new NotSupportedException();

        public void ShowWarning(string title, string message) => throw new NotSupportedException();

        public void ShowError(string message) => throw new NotSupportedException();
    }
}
