using System.IO;
using System.Net.Http;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Emby;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

[Collection("PortableStorage")]
public sealed class EmbySyncViewModelTests
{
    private readonly PortableStorageFixture _storageFixture;

    public EmbySyncViewModelTests(PortableStorageFixture storageFixture)
    {
        _storageFixture = storageFixture;
        _storageFixture.Reset();
    }

    private static EmbySyncViewModel CreateViewModel(
        IUserDialogService? dialogService = null,
        IEmbyClient? embyClient = null,
        AppEmbySettings? configuredEmbySettings = null,
        IEmbyProviderReviewDialogService? providerReviewDialogs = null,
        IModuleLogService? moduleLogs = null)
    {
        var settingsStore = new AppSettingsStore();
        settingsStore.Save(new CombinedAppSettings
        {
            Archive = new AppArchiveSettings(),
            Metadata = new AppMetadataSettings(),
            Emby = configuredEmbySettings?.Clone() ?? new AppEmbySettings
            {
                ServerUrl = AppEmbySettings.DefaultServerUrl,
                ApiKey = string.Empty,
                ScanWaitTimeoutSeconds = 60
            }
        });
        var embySettingsStore = new AppEmbySettingsStore(settingsStore);
        var archiveSettings = new AppArchiveSettingsStore(settingsStore);
        var metadataStore = new AppMetadataStore(settingsStore);
        var episodeMetadata = new EpisodeMetadataLookupService(metadataStore, new ThrowingTvdbClient());
        var syncService = new EmbyMetadataSyncService(embyClient ?? new ThrowingEmbyClient(), new EmbyNfoProviderIdService());
        var services = new EmbyModuleServices(
            embySettingsStore,
            archiveSettings,
            syncService,
            episodeMetadata,
            new ImdbLookupService(new HttpClient(new StubHttpMessageHandler())),
            new NullSettingsDialogService());
        return new EmbySyncViewModel(services, dialogService ?? new NullDialogService(), providerReviewDialogs, moduleLogs);
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
        var embyOnlyItem = new EmbySyncItemViewModel(@"C:\Videos\Serie\trailers\Serie - S00E01 - Trailer.mkv", EmbyProviderIds.Empty);
        embyOnlyItem.ApplyAnalysis(new EmbyFileAnalysis(
            embyOnlyItem.MediaFilePath,
            @"C:\Videos\Serie\trailers\Serie - S00E01 - Trailer.nfo",
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
        Assert.Contains("global", vm.RunScanTooltip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nicht bibliotheksscharf", vm.RunScanTooltip, StringComparison.OrdinalIgnoreCase);
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
    public async Task ReviewPendingProviderIdsCommand_ProcessesTvdbMismatchAndRequiredImdbReviews()
    {
        var reviewDialogs = new QueueingProviderReviewDialogs(
            tvdbResults: [EmbyTvdbReviewResult.Apply(new TvdbEpisodeSelection(1, "Serie", 100, "Pilot", "01", "01"))],
            imdbResults:
            [
                EmbyImdbReviewResult.Apply("tt1234567"),
                EmbyImdbReviewResult.NoImdbId
            ]);
        var moduleLogs = new RecordingModuleLogService();
        var vm = CreateViewModel(providerReviewDialogs: reviewDialogs, moduleLogs: moduleLogs);
        var tvdbMismatchItem = new EmbySyncItemViewModel(@"C:\Videos\Serie - S01E01 - Pilot.mkv", new EmbyProviderIds("100", null));
        tvdbMismatchItem.ApplyAnalysis(new EmbyFileAnalysis(
            tvdbMismatchItem.MediaFilePath,
            Path.ChangeExtension(tvdbMismatchItem.MediaFilePath, ".nfo"),
            MediaFileExists: true,
            NfoExists: true,
            NfoProviderIds: new EmbyProviderIds("200", null),
            EmbyItem: new EmbyItem(
                "emby-1",
                "Pilot",
                tvdbMismatchItem.MediaFilePath,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Tvdb"] = "300"
                }),
            WarningMessage: null));
        var imdbOnlyItem = new EmbySyncItemViewModel(@"C:\Videos\Serie - S01E02 - Finale.mkv", new EmbyProviderIds("101", null));
        imdbOnlyItem.ApplyAnalysis(new EmbyFileAnalysis(
            imdbOnlyItem.MediaFilePath,
            Path.ChangeExtension(imdbOnlyItem.MediaFilePath, ".nfo"),
            MediaFileExists: true,
            NfoExists: true,
            NfoProviderIds: new EmbyProviderIds("101", null),
            EmbyItem: null,
            WarningMessage: null));
        vm.Items.Add(tvdbMismatchItem);
        vm.Items.Add(imdbOnlyItem);

        Assert.True(tvdbMismatchItem.RequiresTvdbReview);
        Assert.True(tvdbMismatchItem.RequiresImdbReview);
        Assert.True(imdbOnlyItem.RequiresImdbReview);

        await vm.ReviewPendingProviderIdsCommand.ExecuteAsync();

        Assert.Contains("abgeschlossen", vm.StatusText, StringComparison.Ordinal);
        Assert.Equal(1, reviewDialogs.TvdbReviewCallCount);
        Assert.Equal(2, reviewDialogs.ImdbReviewCallCount);
        Assert.False(tvdbMismatchItem.HasPendingProviderReview);
        Assert.Equal("100", tvdbMismatchItem.TvdbId);
        Assert.Equal("tt1234567", tvdbMismatchItem.ImdbId);
        Assert.False(imdbOnlyItem.HasPendingProviderReview);
        Assert.True(imdbOnlyItem.IsImdbUnavailable);
        Assert.True(imdbOnlyItem.HasCompleteProviderIds);
        var log = Assert.Single(moduleLogs.Logs);
        Assert.Equal("Emby-Abgleich", log.ModuleLabel);
        Assert.Equal("Pflichtchecks", log.OperationLabel);
        Assert.Contains("Provider-ID-Pflichtprüfungen abgeschlossen", log.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewSelectedMetadataCommand_AllowsKeepingCurrentTvdbId_WhenFileNameCannotSeedSearch()
    {
        var reviewDialogs = new QueueingProviderReviewDialogs(
            tvdbResults: [EmbyTvdbReviewResult.KeepCurrent],
            imdbResults: []);
        var vm = CreateViewModel(providerReviewDialogs: reviewDialogs);
        var item = new EmbySyncItemViewModel(@"C:\Videos\Nicht standardisiert.mkv", new EmbyProviderIds("100", null));
        item.ApplyAnalysis(new EmbyFileAnalysis(
            item.MediaFilePath,
            Path.ChangeExtension(item.MediaFilePath, ".nfo"),
            MediaFileExists: true,
            NfoExists: true,
            NfoProviderIds: new EmbyProviderIds("200", null),
            EmbyItem: null,
            WarningMessage: null));
        vm.Items.Add(item);
        vm.SelectedItem = item;

        Assert.True(item.RequiresTvdbReview);
        Assert.True(vm.ReviewSelectedMetadataCommand.CanExecute(null));

        await vm.ReviewSelectedMetadataCommand.ExecuteAsync();

        Assert.Contains("TVDB-Zuordnung", vm.StatusText, StringComparison.Ordinal);
        Assert.Equal(1, reviewDialogs.TvdbReviewCallCount);
        Assert.False(item.RequiresTvdbReview);
        Assert.Equal("100", item.TvdbId);
    }

    [Fact]
    public async Task RunSyncCommand_BlocksWhenProviderReviewsAreOpen()
    {
        var dialogService = new CapturingDialogService();
        var vm = CreateViewModel(dialogService);
        var item = new EmbySyncItemViewModel(@"C:\Videos\Serie - S01E01 - Pilot.mkv", new EmbyProviderIds("12345", "tt1234567"));
        vm.Items.Add(item);

        await vm.RunSyncCommand.ExecuteAsync();

        Assert.Contains("Provider-ID-Pflichtprüfung", dialogService.LastWarningMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectReportCommand_LoadsMultipleStructuredReports()
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

            await vm.SelectReportCommand.ExecuteAsync();

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

    [Fact]
    public async Task SelectReportCommand_MergesDuplicateEntriesAcrossReports_ByRecencyAndComplementsMissingIds()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-viewmodel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var mediaPath = Path.Combine(tempDirectory, "Serie - S01E01 - Pilot.mkv");
            File.WriteAllText(mediaPath, string.Empty);

            var olderReportPath = WriteMetadataReport(
                tempDirectory,
                "older.metadata.json",
                mediaPath,
                "101");
            var newerReportPath = WriteMetadataReport(
                tempDirectory,
                "newer.metadata.json",
                mediaPath,
                "202",
                imdbId: "tt1234567");
            File.SetLastWriteTimeUtc(olderReportPath, new DateTime(2026, 4, 20, 10, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(newerReportPath, new DateTime(2026, 4, 20, 11, 0, 0, DateTimeKind.Utc));

            var dialogService = new SelectingDialogService([olderReportPath, newerReportPath]);
            var vm = CreateViewModel(dialogService);

            await vm.SelectReportCommand.ExecuteAsync();

            Assert.Contains("Prüfung abgeschlossen", vm.StatusText, StringComparison.Ordinal);
            var item = Assert.Single(vm.Items);
            Assert.Equal("202", item.TvdbId);
            Assert.Equal("tt1234567", item.ImdbId);
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
    public async Task SelectReportCommand_ContinuesWithLocalNfo_WhenEmbyLookupFails()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-viewmodel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var mediaPath = Path.Combine(tempDirectory, "Serie - S01E01 - Pilot.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(mediaPath, string.Empty);
            File.WriteAllText(
                nfoPath,
                """
                <episodedetails>
                  <title>Pilot</title>
                  <tvdbid>12345</tvdbid>
                  <imdbid>tt1234567</imdbid>
                </episodedetails>
                """);
            var reportPath = WriteMetadataReport(tempDirectory, "run.metadata.json", mediaPath, string.Empty);
            var dialogService = new SelectingDialogService([reportPath]);
            var vm = CreateViewModel(
                dialogService,
                configuredEmbySettings: new AppEmbySettings
                {
                    ServerUrl = "http://t-emby:8096",
                    ApiKey = "token",
                    ScanWaitTimeoutSeconds = 60
                });

            await vm.SelectReportCommand.ExecuteAsync();

            var item = Assert.Single(vm.Items);
            Assert.Equal("12345", item.TvdbId);
            Assert.Equal("tt1234567", item.ImdbId);
            Assert.Contains("Prüfung abgeschlossen", vm.StatusText, StringComparison.Ordinal);
            Assert.Contains("Emby-Abfrage nicht möglich", vm.LogText, StringComparison.Ordinal);
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
    public async Task RunSyncCommand_WithoutEmbyCredentials_UpdatesLocalNfoAndSkipsRefresh()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-sync-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var mediaPath = Path.Combine(tempDirectory, "Serie - S01E01 - Pilot.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(mediaPath, string.Empty);
            File.WriteAllText(nfoPath, "<episodedetails><title>Pilot</title></episodedetails>");

            var vm = CreateViewModel();
            var item = new EmbySyncItemViewModel(mediaPath, new EmbyProviderIds("12345", "tt1234567"));
            item.ApplyEmbyItem(new EmbyItem(
                "emby-1",
                "Pilot",
                mediaPath,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
            item.ApplyImdbSelection("tt1234567");
            vm.Items.Add(item);
            vm.SelectedItem = item;

            Assert.True(vm.RunSyncCommand.CanExecute(null));

            await vm.RunSyncCommand.ExecuteAsync();

            Assert.Equal("Aktualisiert", item.StatusText);
            Assert.Contains("keine Emby-API-Zugangsdaten", item.Note, StringComparison.Ordinal);
            Assert.Contains("Emby-Refresh wurde wegen fehlender API-Zugangsdaten", vm.StatusText, StringComparison.Ordinal);
            var updatedText = File.ReadAllText(nfoPath);
            Assert.Contains("<tvdbid>12345</tvdbid>", updatedText);
            Assert.Contains("<imdbid>tt1234567</imdbid>", updatedText);
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
    public async Task RunSyncCommand_WritesNoImdbDecision_WhenNoOtherProviderIdIsSet()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-sync-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var mediaPath = Path.Combine(tempDirectory, "Serie - S00E01 - Bonus.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(mediaPath, string.Empty);
            File.WriteAllText(
                nfoPath,
                """
                <episodedetails>
                  <title>Bonus</title>
                  <uniqueid type="imdb">tt1234567</uniqueid>
                  <imdbid>tt1234567</imdbid>
                </episodedetails>
                """);

            var vm = CreateViewModel();
            var item = new EmbySyncItemViewModel(mediaPath, EmbyProviderIds.Empty);
            item.ApplyAnalysis(new EmbyFileAnalysis(
                item.MediaFilePath,
                nfoPath,
                MediaFileExists: true,
                NfoExists: true,
                NfoProviderIds: new EmbyProviderIds(null, "tt1234567"),
                EmbyItem: null,
                WarningMessage: null));
            item.MarkImdbUnavailable();
            vm.Items.Add(item);

            await vm.RunSyncCommand.ExecuteAsync();

            Assert.Equal("IDs fehlen", item.StatusText);
            var updatedText = File.ReadAllText(nfoPath);
            Assert.DoesNotContain("tt1234567", updatedText, StringComparison.Ordinal);
            Assert.DoesNotContain("<imdbid>", updatedText, StringComparison.OrdinalIgnoreCase);
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
    public async Task RunSyncCommand_RefreshesEmby_WhenNfoIsCurrentButServerIdsDiffer()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-sync-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var mediaPath = Path.Combine(tempDirectory, "Serie - S01E01 - Pilot.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(mediaPath, string.Empty);
            File.WriteAllText(
                nfoPath,
                """
                <episodedetails>
                  <title>Pilot</title>
                  <uniqueid type="tvdb" default="true">12345</uniqueid>
                  <uniqueid type="imdb">tt1234567</uniqueid>
                  <tvdbid>12345</tvdbid>
                  <imdbid>tt1234567</imdbid>
                </episodedetails>
                """);

            var embyClient = new RecordingRefreshEmbyClient();
            var vm = CreateViewModel(
                embyClient: embyClient,
                configuredEmbySettings: new AppEmbySettings
                {
                    ServerUrl = "http://t-emby:8096",
                    ApiKey = "token",
                    ScanWaitTimeoutSeconds = 60
                });
            var item = new EmbySyncItemViewModel(mediaPath, new EmbyProviderIds("12345", "tt1234567"));
            item.ApplyAnalysis(new EmbyFileAnalysis(
                item.MediaFilePath,
                nfoPath,
                MediaFileExists: true,
                NfoExists: true,
                NfoProviderIds: new EmbyProviderIds("12345", "tt1234567"),
                EmbyItem: new EmbyItem(
                    "emby-1",
                    "Pilot",
                    mediaPath,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Tvdb"] = "99999",
                        ["Imdb"] = "tt9999999"
                    }),
                WarningMessage: null));
            item.ApproveCurrentTvdbId();
            item.ApplyImdbSelection("tt1234567");
            vm.Items.Add(item);

            await vm.RunSyncCommand.ExecuteAsync();

            Assert.Equal("Aktualisiert", item.StatusText);
            Assert.Equal(1, embyClient.RefreshCallCount);
            Assert.Equal("emby-1", embyClient.LastRefreshItemId);
            Assert.Contains("NFO war bereits aktuell", item.Note, StringComparison.Ordinal);
            Assert.Contains("Emby-Refresh ohne NFO-Änderung", vm.StatusText, StringComparison.Ordinal);
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
    public async Task RunSyncCommand_DoesNotMarkDone_WhenRefreshOnlyUpdateFails()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-sync-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var mediaPath = Path.Combine(tempDirectory, "Serie - S01E01 - Pilot.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(mediaPath, string.Empty);
            File.WriteAllText(
                nfoPath,
                """
                <episodedetails>
                  <uniqueid type="tvdb" default="true">12345</uniqueid>
                  <uniqueid type="imdb">tt1234567</uniqueid>
                  <tvdbid>12345</tvdbid>
                  <imdbid>tt1234567</imdbid>
                </episodedetails>
                """);
            var reportPath = WriteMetadataReport(
                tempDirectory,
                "refresh-failure.metadata.json",
                mediaPath,
                "12345",
                imdbId: "tt1234567");
            var dialogService = new SelectingDialogService([reportPath]);
            var embyClient = new RefreshFailingEmbyClient();
            var vm = CreateViewModel(
                dialogService,
                embyClient,
                new AppEmbySettings
                {
                    ServerUrl = "http://t-emby:8096",
                    ApiKey = "token",
                    ScanWaitTimeoutSeconds = 60
                });
            await vm.SelectReportCommand.ExecuteAsync();
            Assert.Equal(1, vm.ItemCount);
            var item = Assert.Single(vm.Items);
            item.ApplyAnalysis(new EmbyFileAnalysis(
                item.MediaFilePath,
                nfoPath,
                MediaFileExists: true,
                NfoExists: true,
                NfoProviderIds: new EmbyProviderIds("12345", "tt1234567"),
                EmbyItem: new EmbyItem(
                    "emby-1",
                    "Pilot",
                    mediaPath,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Tvdb"] = "99999",
                        ["Imdb"] = "tt9999999"
                    }),
                WarningMessage: null));
            item.ApproveCurrentTvdbId();
            item.ApplyImdbSelection("tt1234567");

            await vm.RunSyncCommand.ExecuteAsync();

            Assert.Equal("Refresh prüfen", item.StatusText);
            var report = BatchOutputMetadataReportJson.Deserialize(File.ReadAllText(reportPath))!;
            Assert.Null(Assert.Single(report.Items).EmbySyncDone);
            Assert.Contains("Emby-Refresh-Fehler", vm.StatusText, StringComparison.Ordinal);
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
    public async Task RunSyncCommand_MarksCompletedReportAndMovesItToDoneDirectory()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-sync-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var mediaPath = Path.Combine(tempDirectory, "Serie - S01E01 - Pilot.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(mediaPath, string.Empty);
            File.WriteAllText(nfoPath, "<episodedetails><title>Pilot</title></episodedetails>");
            var reportPath = WriteMetadataReport(
                tempDirectory,
                "run.metadata.json",
                mediaPath,
                "12345",
                imdbId: "tt1234567");
            var dialogService = new SelectingDialogService([reportPath]);
            var vm = CreateViewModel(dialogService);

            await vm.SelectReportCommand.ExecuteAsync();

            Assert.Equal(1, vm.ItemCount);
            var item = Assert.Single(vm.Items);
            item.ApplyImdbSelection("tt1234567");

            await vm.RunSyncCommand.ExecuteAsync();

            var doneReportPath = Path.Combine(tempDirectory, "done", "run.metadata.json");
            Assert.True(File.Exists(doneReportPath));
            Assert.False(File.Exists(reportPath));
            Assert.Contains(doneReportPath, vm.ReportPath, StringComparison.OrdinalIgnoreCase);
            var report = BatchOutputMetadataReportJson.Deserialize(File.ReadAllText(doneReportPath))!;
            var reportItem = Assert.Single(report.Items);
            Assert.True(reportItem.EmbySyncDone);
            Assert.NotNull(reportItem.EmbySyncDoneAt);
            Assert.NotNull(report.EmbySyncCompletedAt);
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
    public async Task RunSyncCommand_MarksReportDone_WhenNfoIsAlreadyCurrentWithoutEmbyRefresh()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-sync-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var mediaPath = Path.Combine(tempDirectory, "Serie - S01E01 - Pilot.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(mediaPath, string.Empty);
            File.WriteAllText(
                nfoPath,
                """
                <episodedetails>
                  <uniqueid type="tvdb" default="true">12345</uniqueid>
                  <uniqueid type="imdb">tt1234567</uniqueid>
                  <tvdbid>12345</tvdbid>
                  <imdbid>tt1234567</imdbid>
                </episodedetails>
                """);
            var reportPath = WriteMetadataReport(
                tempDirectory,
                "current.metadata.json",
                mediaPath,
                "12345",
                imdbId: "tt1234567");
            var dialogService = new SelectingDialogService([reportPath]);
            var vm = CreateViewModel(dialogService);

            await vm.SelectReportCommand.ExecuteAsync();
            var item = Assert.Single(vm.Items);
            item.ApplyImdbSelection("tt1234567");

            await vm.RunSyncCommand.ExecuteAsync();

            var doneReportPath = Path.Combine(tempDirectory, "done", "current.metadata.json");
            Assert.Equal("NFO aktuell", item.StatusText);
            Assert.True(File.Exists(doneReportPath));
            var report = BatchOutputMetadataReportJson.Deserialize(File.ReadAllText(doneReportPath))!;
            Assert.True(Assert.Single(report.Items).EmbySyncDone);
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
    public async Task RunSyncCommand_SkipsInvalidProviderIds_BeforeWritingNfo()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-sync-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var mediaPath = Path.Combine(tempDirectory, "Serie - S01E01 - Pilot.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            const string originalNfo = "<episodedetails><title>Pilot</title></episodedetails>";
            File.WriteAllText(mediaPath, string.Empty);
            File.WriteAllText(nfoPath, originalNfo);

            var vm = CreateViewModel();
            var item = new EmbySyncItemViewModel(mediaPath, new EmbyProviderIds("12345", "ttbad"));
            item.ImdbId = "ttbad";
            vm.Items.Add(item);

            await vm.RunSyncCommand.ExecuteAsync();

            Assert.Equal("IDs prüfen", item.StatusText);
            Assert.Equal(originalNfo, File.ReadAllText(nfoPath));
            Assert.Contains("IMDB-ID muss im Format", item.Note, StringComparison.Ordinal);
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
    public async Task RunSyncCommand_ContinuesAfterRefreshFailure_AndMarksAffectedRow()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-sync-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var mediaPath = Path.Combine(tempDirectory, "Serie - S01E01 - Pilot.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(mediaPath, string.Empty);
            File.WriteAllText(nfoPath, "<episodedetails><title>Pilot</title></episodedetails>");

            var embyClient = new RefreshFailingEmbyClient();
            var vm = CreateViewModel(
                embyClient: embyClient,
                configuredEmbySettings: new AppEmbySettings
                {
                    ServerUrl = "http://t-emby:8096",
                    ApiKey = "token",
                    ScanWaitTimeoutSeconds = 60
                });
            var item = new EmbySyncItemViewModel(mediaPath, new EmbyProviderIds("12345", "tt1234567"));
            item.ApplyEmbyItem(new EmbyItem(
                "emby-1",
                "Pilot",
                mediaPath,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
            item.ApplyImdbSelection("tt1234567");
            vm.Items.Add(item);

            await vm.RunSyncCommand.ExecuteAsync();

            Assert.Equal("Refresh prüfen", item.StatusText);
            Assert.Equal(1, embyClient.RefreshCallCount);
            Assert.Contains("fehlgeschlagen", item.Note, StringComparison.Ordinal);
            Assert.Contains("Emby-Refresh-Fehler", vm.StatusText, StringComparison.Ordinal);
            Assert.Contains("<tvdbid>12345</tvdbid>", File.ReadAllText(nfoPath));
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

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
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
        public virtual void ShowWarning(string title, string message) { }
        public void ShowError(string message) { }
    }

    private sealed class SelectingDialogService(IReadOnlyList<string> selectedFiles) : NullDialogService
    {
        public override string[]? SelectFiles(string title, string filter, string initialDirectory) => selectedFiles.ToArray();
    }

    private sealed class CapturingDialogService : NullDialogService
    {
        public string LastWarningMessage { get; private set; } = string.Empty;

        public override void ShowWarning(string title, string message)
        {
            LastWarningMessage = message;
        }
    }

    private sealed class QueueingProviderReviewDialogs(
        IReadOnlyList<EmbyTvdbReviewResult> tvdbResults,
        IReadOnlyList<EmbyImdbReviewResult> imdbResults) : IEmbyProviderReviewDialogService
    {
        private readonly Queue<EmbyTvdbReviewResult> _tvdbResults = new(tvdbResults);
        private readonly Queue<EmbyImdbReviewResult> _imdbResults = new(imdbResults);

        public int TvdbReviewCallCount { get; private set; }

        public int ImdbReviewCallCount { get; private set; }

        public EmbyTvdbReviewResult ReviewTvdb(
            EmbySyncItemViewModel item,
            EpisodeMetadataLookupService episodeMetadata,
            IAppSettingsDialogService settingsDialog)
        {
            TvdbReviewCallCount++;
            return _tvdbResults.Count > 0 ? _tvdbResults.Dequeue() : EmbyTvdbReviewResult.Cancelled;
        }

        public EmbyImdbReviewResult ReviewImdb(
            EmbySyncItemViewModel item,
            ImdbLookupService imdbLookup,
            ImdbLookupMode lookupMode)
        {
            ImdbReviewCallCount++;
            return _imdbResults.Count > 0 ? _imdbResults.Dequeue() : EmbyImdbReviewResult.Cancelled;
        }
    }

    private static string WriteMetadataReport(
        string directory,
        string fileName,
        string outputPath,
        string tvdbEpisodeId,
        string? imdbId = null)
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
                            Tvdb = tvdbEpisodeId,
                            Imdb = imdbId
                        }
                    }
                ]
            }));
        return reportPath;
    }

    private sealed class RecordingRefreshEmbyClient : IEmbyClient
    {
        public int RefreshCallCount { get; private set; }

        public string? LastRefreshItemId { get; private set; }

        public Task<IReadOnlyList<EmbyLibraryFolder>> GetLibrariesAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EmbyLibraryFolder>>([]);

        public Task<EmbyServerInfo> GetSystemInfoAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task TriggerLibraryScanAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task TriggerItemFileScanAsync(AppEmbySettings settings, string itemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EmbyItem?> FindItemByPathAsync(AppEmbySettings settings, string mediaFilePath, CancellationToken cancellationToken = default)
            => Task.FromResult<EmbyItem?>(null);

        public Task RefreshItemMetadataAsync(AppEmbySettings settings, string itemId, CancellationToken cancellationToken = default)
        {
            RefreshCallCount++;
            LastRefreshItemId = itemId;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    private sealed class RefreshFailingEmbyClient : IEmbyClient
    {
        public int RefreshCallCount { get; private set; }

        public Task<IReadOnlyList<EmbyLibraryFolder>> GetLibrariesAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EmbyLibraryFolder>>([]);

        public Task<EmbyServerInfo> GetSystemInfoAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task TriggerLibraryScanAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task TriggerItemFileScanAsync(AppEmbySettings settings, string itemId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EmbyItem?> FindItemByPathAsync(AppEmbySettings settings, string mediaFilePath, CancellationToken cancellationToken = default)
            => Task.FromResult<EmbyItem?>(null);

        public Task RefreshItemMetadataAsync(AppEmbySettings settings, string itemId, CancellationToken cancellationToken = default)
        {
            RefreshCallCount++;
            throw new InvalidOperationException("Refresh kaputt");
        }

        public void Dispose()
        {
        }
    }
}
