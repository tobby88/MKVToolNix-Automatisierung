using System.IO;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Emby;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class EmbySyncViewModelTests
{
    private static EmbySyncViewModel CreateViewModel(IUserDialogService? dialogService = null)
    {
        var settingsStore = new AppSettingsStore();
        var embySettings = new AppEmbySettingsStore(settingsStore);
        var archiveSettings = new AppArchiveSettingsStore(settingsStore);
        var metadataStore = new AppMetadataStore(settingsStore);
        var episodeMetadata = new EpisodeMetadataLookupService(metadataStore, new ThrowingTvdbClient());
        var syncService = new EmbyMetadataSyncService(new ThrowingEmbyClient(), new EmbyNfoProviderIdService());
        var services = new EmbyModuleServices(embySettings, archiveSettings, syncService, episodeMetadata, new NullSettingsDialogService());
        return new EmbySyncViewModel(services, dialogService ?? new NullDialogService());
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
    public void MissingIdCount_ReflectsItemsWithoutProviderIds()
    {
        var vm = CreateViewModel();
        vm.Items.Add(new EmbySyncItemViewModel(@"C:\Videos\S01E01.mkv", EmbyProviderIds.Empty));
        vm.Items.Add(new EmbySyncItemViewModel(@"C:\Videos\S01E02.mkv", new EmbyProviderIds("12345", null)));

        Assert.Equal(1, vm.MissingIdCount);
        Assert.Equal(2, vm.IncompleteIdCount);
    }

    [Fact]
    public void MissingIdCount_IgnoresEntriesWithoutApplicableNfoSync()
    {
        var vm = CreateViewModel();
        var syncableItem = new EmbySyncItemViewModel(@"C:\Videos\Serie - S01E01 - Pilot.mkv", EmbyProviderIds.Empty);
        var embyOnlyItem = new EmbySyncItemViewModel(@"C:\Videos\Serie - S00E01 - Trailer.mkv", EmbyProviderIds.Empty);
        embyOnlyItem.ApplyAnalysis(new EmbyFileAnalysis(
            embyOnlyItem.MediaFilePath,
            @"C:\Videos\Serie - S00E01 - Trailer.nfo",
            MediaFileExists: true,
            NfoExists: false,
            NfoProviderIds: EmbyProviderIds.Empty,
            EmbyItem: new EmbyItem(
                "emby-trailer",
                "Trailer",
                embyOnlyItem.MediaFilePath,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            WarningMessage: null));
        vm.Items.Add(syncableItem);
        vm.Items.Add(embyOnlyItem);

        Assert.Equal(1, vm.IncompleteIdCount);
        Assert.Contains("ohne vollständige TVDB-/IMDB-ID", vm.SummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public void RunScanTooltip_ExplainsSeriesLibraryScanAndBackgroundWork()
    {
        var vm = CreateViewModel();

        Assert.Contains("Serienbibliothek", vm.RunScanTooltip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Serverfortschritt", vm.RunScanTooltip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Emby-Treffer", vm.RunScanTooltip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunSyncTooltip_ExplainsFinalWriteStep()
    {
        var vm = CreateViewModel();

        Assert.Contains("Letzter Schritt", vm.RunSyncTooltip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aktuell ausgewählten", vm.RunSyncTooltip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ohne zusätzlichen Bibliotheksscan", vm.RunSyncTooltip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NFO-Dateien", vm.RunSyncTooltip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToggleSelectedItemSelectionCommand_TogglesSelectedRow()
    {
        var vm = CreateViewModel();
        var item = new EmbySyncItemViewModel(@"C:\Videos\Serie - S01E01 - Pilot.mkv", EmbyProviderIds.Empty);
        vm.Items.Add(item);
        vm.SelectedItem = item;

        vm.ToggleSelectedItemSelectionCommand.Execute(null);

        Assert.False(item.IsSelected);

        vm.ToggleSelectedItemSelectionCommand.Execute(null);

        Assert.True(item.IsSelected);
    }

    [Fact]
    public void SelectReportCommand_LoadsMultipleStructuredReports()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-viewmodel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var firstReportPath = WriteMetadataReport(
                tempDirectory,
                "first.metadata.json",
                Path.Combine(tempDirectory, "Serie - S01E01 - Pilot.mkv"),
                "101");
            var secondReportPath = WriteMetadataReport(
                tempDirectory,
                "second.metadata.json",
                Path.Combine(tempDirectory, "Serie - S01E02 - Finale.mkv"),
                "102");
            var dialogService = new SelectingDialogService([firstReportPath, secondReportPath]);
            var vm = CreateViewModel(dialogService);

            vm.SelectReportCommand.Execute(null);

            Assert.True(SpinWait.SpinUntil(() => vm.ItemCount == 2, TimeSpan.FromSeconds(2)));
            Assert.Equal(2, vm.ItemCount);
            Assert.Contains(firstReportPath, vm.ReportPath, StringComparison.Ordinal);
            Assert.Contains(secondReportPath, vm.ReportPath, StringComparison.Ordinal);
            Assert.Contains("Prüfung abgeschlossen", vm.StatusText, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private sealed class ThrowingEmbyClient : IEmbyClient
    {
        public Task<IReadOnlyList<EmbyLibraryFolder>> GetLibrariesAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EmbyServerInfo> GetSystemInfoAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task TriggerLibraryScanAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task TriggerItemFileScanAsync(AppEmbySettings settings, string itemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EmbyItem?> FindItemByPathAsync(AppEmbySettings settings, string mediaFilePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RefreshItemMetadataAsync(AppEmbySettings settings, string itemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Dispose() { }
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

    private sealed class NullSettingsDialogService : IAppSettingsDialogService
    {
        public bool ShowDialog(System.Windows.Window? owner = null, AppSettingsPage initialPage = AppSettingsPage.Archive)
        {
            return false;
        }
    }

    private class NullDialogService : IUserDialogService
    {
        public string? SelectMainVideo(string initialDirectory) => null;
        public string? SelectAudioDescription(string initialDirectory) => null;
        public string[]? SelectSubtitles(string initialDirectory) => null;
        public string[]? SelectAttachments(string initialDirectory) => null;
        public string? SelectOutput(string initialDirectory, string fileName) => null;
        public string? SelectFolder(string title, string initialDirectory) => null;
        public string? SelectExecutable(string title, string filter, string initialDirectory) => null;
        public string? SelectFile(string title, string filter, string initialDirectory) => null;
        public virtual string[]? SelectFiles(string title, string filter, string initialDirectory) => null;
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
        public void ShowWarning(string title, string message) { }
        public void ShowError(string message) { }
    }

    private sealed class SelectingDialogService(IReadOnlyList<string> selectedFiles) : NullDialogService
    {
        public override string[]? SelectFiles(string title, string filter, string initialDirectory) => selectedFiles.ToArray();
    }

    private static string WriteMetadataReport(string directory, string fileName, string outputPath, string tvdbEpisodeId)
    {
        var reportPath = Path.Combine(directory, fileName);
        File.WriteAllText(
            reportPath,
            BatchOutputMetadataReportJson.Serialize(new BatchOutputMetadataReport
            {
                CreatedAt = DateTimeOffset.Now,
                SourceDirectory = directory,
                OutputDirectory = directory,
                Items =
                [
                    new BatchOutputMetadataEntry
                    {
                        OutputPath = outputPath,
                        TvdbEpisodeId = tvdbEpisodeId,
                        ProviderIds = new BatchOutputProviderIds
                        {
                            Tvdb = tvdbEpisodeId
                        }
                    }
                ]
            }));
        return reportPath;
    }
}
