using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace MkvToolnixAutomatisierung.Services.Metadata;

/// <summary>
/// Beschreibt ein verfügbares IMDb-Datensatzupdate vor dem kostenintensiven Download.
/// </summary>
internal sealed record ImdbDatasetUpdateOffer(
    bool IsInitialInstall,
    string VersionToken,
    DateTimeOffset? LastModifiedUtc,
    long? TotalDownloadBytes);

/// <summary>
/// Kapselt die zwingende Benutzerzustimmung vor dem Download der großen IMDb-Dateien.
/// </summary>
internal interface IImdbDatasetUpdateConsent
{
    bool ConfirmUpdate(ImdbDatasetUpdateOffer offer);
}

/// <summary>
/// WPF-Bestätigung für den optionalen IMDb-Download. Ein Nein lässt einen vorhandenen Index unverändert nutzbar.
/// </summary>
internal sealed class ImdbDatasetUpdateConsent : IImdbDatasetUpdateConsent
{
    public bool ConfirmUpdate(ImdbDatasetUpdateOffer offer)
    {
        var sizeText = offer.TotalDownloadBytes is > 0
            ? FormatBytes(offer.TotalDownloadBytes.Value)
            : "mehrere hundert MiB";
        var revisionText = offer.LastModifiedUtc is { } revision
            ? revision.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)
            : "aktuell";
        var actionText = offer.IsInitialInstall
            ? "Der optionale lokale IMDb-Index ist noch nicht installiert."
            : "Für den lokalen IMDb-Index liegen neuere Quelldaten vor.";
        var result = MessageBox.Show(
            ResolveOwner(),
            $"{actionText}{Environment.NewLine}{Environment.NewLine}"
            + $"Revision: {revisionText}{Environment.NewLine}"
            + $"Download: ungefähr {sizeText}{Environment.NewLine}{Environment.NewLine}"
            + "Die drei offiziellen IMDb-Dateien werden nur nach Ihrer Zustimmung geladen. "
            + "Der vorhandene Index bleibt bei Abbruch oder Fehler erhalten. Jetzt herunterladen und neu aufbauen?",
            "IMDb-Offlineindex aktualisieren",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }

    private static Window? ResolveOwner() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
        ?? Application.Current?.MainWindow;

    private static string FormatBytes(long byteCount)
    {
        var mib = byteCount / 1024d / 1024d;
        return mib >= 1024d
            ? $"{mib / 1024d:F1} GiB"
            : $"{mib:F0} MiB";
    }
}

/// <summary>
/// Ergebnis der optionalen IMDb-Indexprüfung beim Start oder nach dem Speichern der Einstellungen.
/// </summary>
internal sealed record ImdbDatasetStartupResult(IReadOnlyList<string> Warnings)
{
    public bool HasWarning => Warnings.Count > 0;
}

/// <summary>
/// Prüft die offiziellen IMDb-Dateien sparsam per HEAD und baut nach Zustimmung einen neuen Offlineindex.
/// </summary>
internal sealed class ImdbDatasetManager
{
    private static readonly TimeSpan SuccessfulCheckInterval = TimeSpan.FromHours(24);
    private static readonly IReadOnlyList<ImdbDatasetDescriptor> Datasets =
    [
        new("title.basics", new Uri("https://datasets.imdbws.com/title.basics.tsv.gz"), "title.basics.tsv.gz"),
        new("title.episode", new Uri("https://datasets.imdbws.com/title.episode.tsv.gz"), "title.episode.tsv.gz"),
        new("title.akas", new Uri("https://datasets.imdbws.com/title.akas.tsv.gz"), "title.akas.tsv.gz")
    ];

    private readonly IAppMetadataStore _metadataStore;
    private readonly HttpClient _httpClient;
    private readonly ImdbDatasetIndexBuilder _indexBuilder;
    private readonly IImdbDatasetUpdateConsent _consent;
    private readonly string _dataDirectory;
    private readonly string _databasePath;

    public ImdbDatasetManager(
        IAppMetadataStore metadataStore,
        HttpClient httpClient,
        ImdbDatasetIndexBuilder indexBuilder,
        IImdbDatasetUpdateConsent consent,
        string? dataDirectory = null,
        string? databasePath = null)
    {
        _metadataStore = metadataStore;
        _httpClient = httpClient;
        _indexBuilder = indexBuilder;
        _consent = consent;
        _dataDirectory = dataDirectory ?? PortableAppStorage.ImdbDataDirectory;
        _databasePath = databasePath ?? PortableAppStorage.ImdbDatabaseFilePath;
    }

    /// <summary>
    /// Prüft höchstens täglich auf Änderungen. Der eigentliche große Download erfolgt immer erst nach Zustimmung.
    /// </summary>
    public async Task<ImdbDatasetStartupResult> EnsureCurrentAsync(
        IProgress<ManagedToolStartupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var settings = _metadataStore.Load();
        var datasetSettings = settings.ImdbDataset ?? new ImdbDatasetSettings();
        if (!datasetSettings.ManagementPreferenceConfigured)
        {
            // Migration für Installationen, deren settings.json vor Einführung des IMDb-Index
            // entstanden ist. Ein später bewusst deaktivierter Index bleibt dagegen deaktiviert.
            datasetSettings.AutoManageEnabled = true;
            datasetSettings.ManagementPreferenceConfigured = true;
            PersistDatasetSettings(datasetSettings);
        }

        if (!datasetSettings.AutoManageEnabled)
        {
            return new ImdbDatasetStartupResult([]);
        }

        var databaseExists = File.Exists(_databasePath);
        if (databaseExists
            && datasetSettings.LastCheckedUtc is { } lastCheckedUtc
            && DateTimeOffset.UtcNow - lastCheckedUtc < SuccessfulCheckInterval)
        {
            return new ImdbDatasetStartupResult([]);
        }

        try
        {
            Report(progress, "IMDb-Daten werden geprüft...", "Prüfe offizielle Datensatzrevisionen.", 0d, false);
            var remoteFiles = await LoadRemoteMetadataAsync(cancellationToken);
            var versionToken = BuildVersionToken(remoteFiles);
            datasetSettings.LastCheckedUtc = DateTimeOffset.UtcNow;
            PersistDatasetSettings(datasetSettings);

            if (databaseExists && string.Equals(datasetSettings.InstalledVersion, versionToken, StringComparison.Ordinal))
            {
                Report(progress, "IMDb-Offlineindex aktuell", "Kein Download nötig.", 100d, false);
                return new ImdbDatasetStartupResult([]);
            }

            var offer = new ImdbDatasetUpdateOffer(
                IsInitialInstall: !databaseExists,
                versionToken,
                remoteFiles.Max(file => file.LastModifiedUtc),
                remoteFiles.All(file => file.ContentLength is not null)
                    ? remoteFiles.Sum(file => file.ContentLength!.Value)
                    : null);
            if (!_consent.ConfirmUpdate(offer))
            {
                Report(progress, "IMDb-Update übersprungen", "Der vorhandene Stand bleibt aktiv.", 100d, false);
                return new ImdbDatasetStartupResult([]);
            }

            await DownloadAndBuildAsync(remoteFiles, versionToken, progress, cancellationToken);
            datasetSettings.InstalledVersion = versionToken;
            datasetSettings.LastCheckedUtc = DateTimeOffset.UtcNow;
            datasetSettings.LastUpdatedUtc = DateTimeOffset.UtcNow;
            PersistDatasetSettings(datasetSettings);
            Report(progress, "IMDb-Offlineindex bereit", "Download und Indexaufbau abgeschlossen.", 100d, false);
            return new ImdbDatasetStartupResult([]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ImdbDatasetStartupResult(
                [$"Der optionale IMDb-Offlineindex konnte nicht aktualisiert werden. Ein vorhandener Index bleibt aktiv.{Environment.NewLine}{ex.Message}"]);
        }
    }

    private async Task<IReadOnlyList<ImdbRemoteDatasetFile>> LoadRemoteMetadataAsync(CancellationToken cancellationToken)
    {
        var results = new List<ImdbRemoteDatasetFile>(Datasets.Count);
        foreach (var dataset in Datasets)
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, dataset.DownloadUri);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            results.Add(new ImdbRemoteDatasetFile(
                dataset,
                response.Content.Headers.ContentLength,
                response.Content.Headers.LastModified,
                response.Headers.ETag?.Tag));
        }

        return results;
    }

    private async Task DownloadAndBuildAsync(
        IReadOnlyList<ImdbRemoteDatasetFile> remoteFiles,
        string versionToken,
        IProgress<ManagedToolStartupProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_dataDirectory);
        var stagingDirectory = Path.Combine(_dataDirectory, $".staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            var downloadedBytes = 0L;
            var totalBytes = remoteFiles.All(file => file.ContentLength is not null)
                ? remoteFiles.Sum(file => file.ContentLength!.Value)
                : (long?)null;
            var archivePaths = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var remoteFile in remoteFiles)
            {
                var targetPath = Path.Combine(stagingDirectory, remoteFile.Descriptor.FileName);
                archivePaths[remoteFile.Descriptor.Name] = targetPath;
                await DownloadFileAsync(
                    remoteFile,
                    targetPath,
                    bytes =>
                    {
                        downloadedBytes += bytes;
                        var percent = totalBytes is > 0 ? Math.Min(55d, downloadedBytes * 55d / totalBytes.Value) : (double?)null;
                        Report(
                            progress,
                            $"IMDb: {remoteFile.Descriptor.Name} wird geladen...",
                            totalBytes is > 0
                                ? $"{FormatBytes(downloadedBytes)} von {FormatBytes(totalBytes.Value)}"
                                : $"{FormatBytes(downloadedBytes)} geladen",
                            percent,
                            percent is null);
                    },
                    cancellationToken);
            }

            var stagedDatabasePath = Path.Combine(stagingDirectory, "imdb-episodes.sqlite");
            var importProgress = new CallbackProgress<ImdbDatasetImportProgress>(value =>
            {
                if (value.IsFinalizing)
                {
                    Report(
                        progress,
                        "IMDb: SQLite-Suchindex wird fertiggestellt...",
                        "Die drei Datendateien sind eingelesen; Suchindizes werden optimiert.",
                        98d,
                        false);
                    return;
                }

                var importPercent = 55d + (value.OverallProgressPercent * 42d / 100d);
                Report(
                    progress,
                    $"IMDb: {value.DatasetName} wird indexiert ({value.DatasetNumber}/{value.DatasetCount})...",
                    $"{value.ProcessedRowCount:N0} Datensätze verarbeitet · Datei {value.DatasetProgressPercent:0.0}% · Import {value.OverallProgressPercent:0.0}%",
                    importPercent,
                    false);
            });
            await _indexBuilder.BuildAsync(
                stagedDatabasePath,
                archivePaths["title.basics"],
                archivePaths["title.episode"],
                archivePaths["title.akas"],
                versionToken,
                importProgress,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            ReplaceDatabaseAtomically(stagedDatabasePath, _databasePath);
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private async Task DownloadFileAsync(
        ImdbRemoteDatasetFile remoteFile,
        string targetPath,
        Action<int> reportBytes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, remoteFile.Descriptor.DownloadUri);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);
        var buffer = new byte[1024 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            reportBytes(read);
        }

        await target.FlushAsync(cancellationToken);
        if (remoteFile.ContentLength is { } expectedLength && target.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"IMDb-Datei {remoteFile.Descriptor.FileName} ist unvollständig ({target.Length} statt {expectedLength} Bytes).");
        }
    }

    private void PersistDatasetSettings(ImdbDatasetSettings datasetSettings)
    {
        var settings = _metadataStore.Load();
        settings.ImdbDataset = datasetSettings.Clone();
        _metadataStore.Save(settings);
    }

    private static string BuildVersionToken(IReadOnlyList<ImdbRemoteDatasetFile> files)
    {
        var rawToken = string.Join(
            "|",
            files.Select(file =>
                $"{file.Descriptor.Name}:{file.ETag ?? string.Empty}:{file.LastModifiedUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty}:{file.ContentLength?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
    }

    private static void ReplaceDatabaseAtomically(string sourcePath, string targetPath)
    {
        if (!File.Exists(targetPath))
        {
            File.Move(sourcePath, targetPath);
            return;
        }

        var backupPath = targetPath + ".bak";
        File.Replace(sourcePath, targetPath, backupPath, ignoreMetadataErrors: true);
        File.Delete(backupPath);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Ein Staging-Rest darf weder den alten Index noch den nächsten App-Start blockieren.
        }
    }

    private static void Report(
        IProgress<ManagedToolStartupProgress>? progress,
        string status,
        string detail,
        double? percent,
        bool indeterminate) =>
        progress?.Report(new ManagedToolStartupProgress(status, detail, percent, indeterminate));

    private static string FormatBytes(long byteCount)
    {
        var mib = byteCount / 1024d / 1024d;
        return mib >= 1024d ? $"{mib / 1024d:F1} GiB" : $"{mib:F0} MiB";
    }

    private sealed record ImdbDatasetDescriptor(string Name, Uri DownloadUri, string FileName);

    private sealed record ImdbRemoteDatasetFile(
        ImdbDatasetDescriptor Descriptor,
        long? ContentLength,
        DateTimeOffset? LastModifiedUtc,
        string? ETag);

    /// <summary>
    /// Leitet Builder-Updates synchron an den äußeren Fortschrittskanal weiter. Der äußere
    /// <see cref="Progress{T}"/> übernimmt bereits das Marshalling zum WPF-Dispatcher; ein zweiter
    /// Dispatcher-Hop würde nur veraltete Zwischenstände aufstauen.
    /// </summary>
    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
