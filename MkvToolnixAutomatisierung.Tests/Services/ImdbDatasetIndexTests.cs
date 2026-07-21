using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Data.Sqlite;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class ImdbDatasetIndexTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-imdb-tests", Guid.NewGuid().ToString("N"));

    public ImdbDatasetIndexTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task BuildAsync_CreatesSearchableGermanEpisodeIndex()
    {
        var files = WriteSmallDatasetArchives();
        var databasePath = Path.Combine(_tempDirectory, "index.sqlite");
        var progress = new RecordingProgress<ImdbDatasetImportProgress>();
        await new ImdbDatasetIndexBuilder().BuildAsync(
            databasePath,
            files.Basics,
            files.Episodes,
            files.Aliases,
            "revision-1",
            progress);

        Assert.Equal(1, ReadScalar(databasePath, "SELECT COUNT(*) FROM titles WHERE kind = 1;"));
        Assert.Equal(1, ReadScalar(databasePath, "SELECT COUNT(*) FROM titles WHERE kind = 2;"));
        Assert.Equal(2, ReadScalar(databasePath, "SELECT COUNT(*) FROM aliases;"));
        Assert.Equal("tt1000001", ReadText(databasePath, "SELECT parent_id FROM titles WHERE kind = 2;"));
        Assert.Equal("der alte", ReadText(databasePath, "SELECT normalized_primary FROM titles WHERE kind = 1;"));
        Assert.Equal("die wahrheit im dunkeln", ReadText(databasePath, "SELECT normalized_title FROM aliases WHERE title_id = 'tt2000001';"));
        Assert.Equal(
            1,
            ReadScalar(
                databasePath,
                "SELECT COUNT(*) FROM titles t WHERE t.kind = 1 AND t.normalized_primary = 'der alte';"));
        Assert.Equal(
            1,
            ReadScalar(
                databasePath,
                "SELECT COUNT(*) FROM titles t JOIN aliases a ON a.title_id = t.id WHERE t.parent_id = 'tt1000001' AND a.normalized_title = 'die wahrheit im dunkeln';"));
        Assert.Equal(30, EpisodeMetadataMatchingHeuristics.CalculateTitleSimilarity("Die Wahrheit im Dunkeln", "Die Wahrheit im Dunkeln"));

        var candidates = new ImdbDatasetSearchService(databasePath).SearchEpisodeCandidates(
            new EpisodeMetadataGuess("Der Alte", "Die Wahrheit im Dunkeln", "55", "02"));

        var candidate = Assert.Single(candidates);
        Assert.Equal("tt2000001", candidate.ImdbId);
        Assert.Equal("Der Alte", candidate.SeriesTitle);
        Assert.Equal(55, candidate.SeasonNumber);
        Assert.Equal(2, candidate.EpisodeNumber);
        Assert.True(candidate.IsStrongAutomaticMatch);

        var seriesAliasCandidate = Assert.Single(new ImdbDatasetSearchService(databasePath).SearchEpisodeCandidates(
            new EpisodeMetadataGuess("Der alte Kommissar", "Die Wahrheit im Dunkeln", "55", "02")));
        Assert.Equal("tt2000001", seriesAliasCandidate.ImdbId);
        Assert.True(seriesAliasCandidate.SeriesTitleMatchedExactly);

        var lookupViewModel = new ImdbLookupWindowViewModel(
            new EpisodeMetadataGuess("Der Alte", "Die Wahrheit im Dunkeln", "55", "02"),
            currentImdbId: null,
            new ImdbDatasetSearchService(databasePath));
        Assert.Empty(lookupViewModel.LocalCandidates);
        await lookupViewModel.RefreshLocalCandidatesAsync();
        Assert.Single(lookupViewModel.LocalCandidates);
        Assert.True(lookupViewModel.ApplySelectedLocalCandidate());
        Assert.Equal("tt2000001", lookupViewModel.ImdbInput);

        lookupViewModel.EpisodeSearchText = "Anderer Titel";
        Assert.Empty(lookupViewModel.LocalCandidates);
        Assert.Contains("Lokal neu suchen", lookupViewModel.LocalDatasetStatusText, StringComparison.Ordinal);
        lookupViewModel.EpisodeSearchText = "Die Wahrheit im Dunkeln";
        await lookupViewModel.RefreshLocalCandidatesAsync();
        Assert.Single(lookupViewModel.LocalCandidates);

        var manuallyEnteredViewModel = new ImdbLookupWindowViewModel(
            guess: null,
            currentImdbId: null,
            new ImdbDatasetSearchService(databasePath))
        {
            SeriesSearchText = "Der Alte",
            EpisodeSearchText = "Die Wahrheit im Dunkeln"
        };
        await manuallyEnteredViewModel.RefreshLocalCandidatesAsync();
        Assert.Single(manuallyEnteredViewModel.LocalCandidates);

        var importReports = progress.Values.Where(value => !value.IsFinalizing).ToArray();
        Assert.Equal([1, 2, 3], importReports.Select(value => value.DatasetNumber).Distinct().ToArray());
        Assert.All(
            importReports.GroupBy(value => value.DatasetNumber),
            group => Assert.Equal(100d, group.Last().DatasetProgressPercent, precision: 5));
        Assert.True(importReports.Zip(importReports.Skip(1), (left, right) => left.OverallProgressPercent <= right.OverallProgressPercent).All(value => value));
        Assert.Equal(100d, Assert.Single(progress.Values, value => value.IsFinalizing).OverallProgressPercent);
    }

    [Fact]
    public void SelectAutomaticCandidate_RejectsAmbiguousOrInexactMatches()
    {
        var exact = new ImdbEpisodeCandidate("tt1000001", "Serie", "Folge", 1, 1, 80, 30, true);
        var closeSecond = new ImdbEpisodeCandidate("tt1000002", "Serie", "Folge", 2, 1, 75, 30, true);
        var inexact = new ImdbEpisodeCandidate("tt1000003", "Serie", "Folge ähnlich", 1, 1, 90, 22, true);

        Assert.Same(exact, ImdbDatasetSearchService.SelectAutomaticCandidate([exact]));
        Assert.Null(ImdbDatasetSearchService.SelectAutomaticCandidate([exact, closeSecond]));
        Assert.Null(ImdbDatasetSearchService.SelectAutomaticCandidate([inexact]));
    }

    [Fact]
    public async Task EnsureCurrentAsync_DoesNotDownload_WhenUserDeclinesOffer()
    {
        var datasetBytes = BuildSmallDatasetByteMap();
        var handler = new DatasetHttpHandler(datasetBytes);
        using var httpClient = new HttpClient(handler);
        var store = new FakeMetadataStore(new AppMetadataSettings());
        var consent = new FixedConsent(false);
        var manager = new ImdbDatasetManager(
            store,
            httpClient,
            new ImdbDatasetIndexBuilder(),
            consent,
            _tempDirectory,
            Path.Combine(_tempDirectory, "index.sqlite"));

        var result = await manager.EnsureCurrentAsync();

        Assert.False(result.HasWarning);
        Assert.Equal(1, consent.CallCount);
        Assert.Equal(3, handler.HeadRequestCount);
        Assert.Equal(0, handler.GetRequestCount);
        Assert.NotNull(store.CurrentSettings.ImdbDataset.LastCheckedUtc);
        Assert.True(store.CurrentSettings.ImdbDataset.AutoManageEnabled);
        Assert.True(store.CurrentSettings.ImdbDataset.ManagementPreferenceConfigured);
    }

    [Fact]
    public async Task EnsureCurrentAsync_DoesNotCheck_WhenUserExplicitlyDisabledManagement()
    {
        var handler = new DatasetHttpHandler(BuildSmallDatasetByteMap());
        using var httpClient = new HttpClient(handler);
        var store = new FakeMetadataStore(new AppMetadataSettings
        {
            ImdbDataset = new ImdbDatasetSettings
            {
                AutoManageEnabled = false,
                ManagementPreferenceConfigured = true
            }
        });
        var consent = new FixedConsent(true);
        var manager = new ImdbDatasetManager(
            store,
            httpClient,
            new ImdbDatasetIndexBuilder(),
            consent,
            _tempDirectory,
            Path.Combine(_tempDirectory, "disabled.sqlite"));

        var result = await manager.EnsureCurrentAsync();

        Assert.False(result.HasWarning);
        Assert.Equal(0, consent.CallCount);
        Assert.Equal(0, handler.HeadRequestCount);
        Assert.Equal(0, handler.GetRequestCount);
    }

    [Fact]
    public async Task EnsureCurrentAsync_BuildsIndexAfterConsent_AndSkipsUnchangedRevision()
    {
        var datasetBytes = BuildSmallDatasetByteMap();
        var handler = new DatasetHttpHandler(datasetBytes);
        using var httpClient = new HttpClient(handler);
        var store = new FakeMetadataStore(new AppMetadataSettings
        {
            ImdbDataset = new ImdbDatasetSettings
            {
                AutoManageEnabled = true,
                ManagementPreferenceConfigured = true
            }
        });
        var consent = new FixedConsent(true);
        var databasePath = Path.Combine(_tempDirectory, "index.sqlite");
        var manager = new ImdbDatasetManager(
            store,
            httpClient,
            new ImdbDatasetIndexBuilder(),
            consent,
            _tempDirectory,
            databasePath);
        var progress = new RecordingProgress<ManagedToolStartupProgress>();

        var first = await manager.EnsureCurrentAsync(progress);
        var second = await manager.EnsureCurrentAsync();

        Assert.False(first.HasWarning);
        Assert.False(second.HasWarning);
        Assert.True(File.Exists(databasePath));
        Assert.Equal(1, consent.CallCount);
        Assert.Equal(3, handler.GetRequestCount);
        Assert.False(string.IsNullOrWhiteSpace(store.CurrentSettings.ImdbDataset.InstalledVersion));
        Assert.NotNull(store.CurrentSettings.ImdbDataset.LastUpdatedUtc);
        Assert.Single(new ImdbDatasetSearchService(databasePath).SearchEpisodeCandidates(
            new EpisodeMetadataGuess("Der Alte", "Die Wahrheit im Dunkeln", "55", "02")));
        Assert.Contains(progress.Values, value =>
            value.StatusText.Contains("(1/3)", StringComparison.Ordinal)
            && value.DetailText?.Contains("Datei ", StringComparison.Ordinal) == true
            && value.DetailText.Contains("Import ", StringComparison.Ordinal)
            && !value.DetailText.Contains("Gesamt ", StringComparison.Ordinal)
            && value.IsIndeterminate == false);
        Assert.Contains(progress.Values, value =>
            value.StatusText.Contains("Suchindex", StringComparison.Ordinal)
            && value.ProgressPercent == 98d);
    }

    private DatasetFiles WriteSmallDatasetArchives()
    {
        var bytes = BuildSmallDatasetByteMap();
        string Write(string fileName)
        {
            var path = Path.Combine(_tempDirectory, fileName);
            File.WriteAllBytes(path, bytes[fileName]);
            return path;
        }

        return new DatasetFiles(
            Write("title.basics.tsv.gz"),
            Write("title.episode.tsv.gz"),
            Write("title.akas.tsv.gz"));
    }

    private static Dictionary<string, byte[]> BuildSmallDatasetByteMap() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["title.basics.tsv.gz"] = Gzip(
            "tconst\ttitleType\tprimaryTitle\toriginalTitle\tisAdult\tstartYear\tendYear\truntimeMinutes\tgenres\n"
            + "tt1000001\ttvSeries\tDer Alte\tDer Alte\t0\t1977\t\\N\t60\tCrime\n"
            + "tt2000001\ttvEpisode\tTruth in the Dark\tTruth in the Dark\t0\t2026\t\\N\t60\tCrime\n"
            + "tt3000001\tmovie\tIgnored Movie\tIgnored Movie\t0\t2026\t\\N\t90\tDrama\n"),
        ["title.episode.tsv.gz"] = Gzip(
            "tconst\tparentTconst\tseasonNumber\tepisodeNumber\n"
            + "tt2000001\t" + "tt1000001\t55\t2\n"),
        ["title.akas.tsv.gz"] = Gzip(
            "titleId\tordering\ttitle\tregion\tlanguage\ttypes\tattributes\tisOriginalTitle\n"
            + "tt1000001\t1\tDer alte Kommissar\tDE\tde\timdbDisplay\t\\N\t0\n"
            + "tt2000001\t1\tDie Wahrheit im Dunkeln\tDE\tde\timdbDisplay\t\\N\t0\n"
            + "tt2000001\t2\tLa vérité\tFR\tfr\timdbDisplay\t\\N\t0\n")
    };

    private static byte[] Gzip(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            gzip.Write(bytes);
        }

        return output.ToArray();
    }

    private static long ReadScalar(string databasePath, string commandText)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string ReadText(string databasePath, string commandText)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return Convert.ToString(command.ExecuteScalar())!;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private sealed record DatasetFiles(string Basics, string Episodes, string Aliases);

    private sealed class FixedConsent(bool response) : IImdbDatasetUpdateConsent
    {
        public int CallCount { get; private set; }

        public bool ConfirmUpdate(ImdbDatasetUpdateOffer offer)
        {
            CallCount++;
            return response;
        }
    }

    private sealed class DatasetHttpHandler(IReadOnlyDictionary<string, byte[]> files) : HttpMessageHandler
    {
        public int HeadRequestCount { get; private set; }

        public int GetRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var fileName = Path.GetFileName(request.RequestUri!.AbsolutePath);
            if (!files.TryGetValue(fileName, out var bytes))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (request.Method == HttpMethod.Head)
            {
                HeadRequestCount++;
            }
            else
            {
                GetRequestCount++;
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(request.Method == HttpMethod.Head ? [] : bytes)
            };
            response.Content.Headers.ContentLength = bytes.Length;
            response.Content.Headers.LastModified = new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero);
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue($"\"{fileName}-v1\"");
            return Task.FromResult(response);
        }
    }

    private sealed class FakeMetadataStore(AppMetadataSettings settings) : IAppMetadataStore
    {
        public AppMetadataSettings CurrentSettings { get; private set; } = settings.Clone();

        public string SettingsFilePath => Path.Combine(Path.GetTempPath(), "settings.json");

        public AppMetadataSettings Load() => CurrentSettings.Clone();

        public void Save(AppMetadataSettings value) => CurrentSettings = value.Clone();

        public void Update(Action<AppMetadataSettings> updateAction)
        {
            var updated = CurrentSettings.Clone();
            updateAction(updated);
            CurrentSettings = updated.Clone();
        }
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value) => Values.Add(value);
    }
}
