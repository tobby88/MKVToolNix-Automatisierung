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
    string AvailableVersionToken,
    DateTimeOffset? AvailableRevisionUtc,
    long? TotalDownloadBytes,
    string? InstalledVersionToken,
    DateTimeOffset? InstalledRevisionUtc,
    DateTimeOffset? InstalledAtUtc);

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
        if (Application.Current is { } application && !application.Dispatcher.CheckAccess())
        {
            return application.Dispatcher.Invoke(() => ConfirmUpdate(offer));
        }

        var result = MessageBox.Show(
            ResolveOwner(),
            BuildMessage(offer),
            "IMDb-Offlineindex aktualisieren",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }

    internal static string BuildMessage(ImdbDatasetUpdateOffer offer)
    {
        var sizeText = offer.TotalDownloadBytes is > 0
            ? FormatBytes(offer.TotalDownloadBytes.Value)
            : "mehrere hundert MiB";
        var actionText = offer.IsInitialInstall
            ? "Der optionale lokale IMDb-Index ist noch nicht installiert."
            : "Für den lokalen IMDb-Index liegen neuere Quelldaten vor.";
        return $"{actionText}{Environment.NewLine}{Environment.NewLine}"
            + $"Vorhandene Version: {FormatInstalledVersion(offer)}{Environment.NewLine}"
            + $"Verfügbare Version: {FormatVersion(offer.AvailableRevisionUtc, offer.AvailableVersionToken)}{Environment.NewLine}"
            + $"Download: ungefähr {sizeText}{Environment.NewLine}{Environment.NewLine}"
            + "Die drei offiziellen IMDb-Dateien werden nur nach Ihrer Zustimmung geladen. "
            + "Der vorhandene Index bleibt bei Abbruch oder Fehler erhalten. Jetzt herunterladen und neu aufbauen?";
    }

    private static string FormatInstalledVersion(ImdbDatasetUpdateOffer offer)
    {
        if (offer.IsInitialInstall)
        {
            return "nicht installiert";
        }

        if (offer.InstalledRevisionUtc is { } revision)
        {
            return FormatVersion(revision, offer.InstalledVersionToken);
        }

        var identifier = FormatVersionIdentifier(offer.InstalledVersionToken);
        return offer.InstalledAtUtc is { } installedAt
            ? $"aufgebaut am {installedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)}{identifier}"
            : $"installiert{identifier}";
    }

    private static string FormatVersion(DateTimeOffset? revisionUtc, string? versionToken)
    {
        var revisionText = revisionUtc is { } revision
            ? $"Datenstand vom {revision.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)}"
            : "Datenstand unbekannt";
        return revisionText + FormatVersionIdentifier(versionToken);
    }

    private static string FormatVersionIdentifier(string? versionToken)
    {
        if (string.IsNullOrWhiteSpace(versionToken))
        {
            return string.Empty;
        }

        var normalized = versionToken.Trim();
        var shortToken = normalized[..Math.Min(12, normalized.Length)];
        return $" (Kennung {shortToken})";
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
    private readonly SemaphoreSlim _updateSync = new(1, 1);

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
        await _updateSync.WaitAsync(cancellationToken);
        try
        {
            return await EnsureCurrentCoreAsync(progress, cancellationToken);
        }
        finally
        {
            _updateSync.Release();
        }
    }

    private async Task<ImdbDatasetStartupResult> EnsureCurrentCoreAsync(
        IProgress<ManagedToolStartupProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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

        var newIndexActivated = false;
        try
        {
            Report(progress, "IMDb: Vorhandener Index wird geprüft...", "Prüfe Datenbankintegrität und aktivierten Datenstand.", null, true, "Index");
            var installed = await Task.Run(() => ImdbIndexInspection.Read(_databasePath, verifyIntegrity: true, cancellationToken), cancellationToken);
            var databaseExists = installed is not null;
            if (installed is not null && (datasetSettings.InstalledVersion != installed.Version
                || datasetSettings.InstalledSchemaVersion != installed.Schema))
            {
                // Der Index ist die Wahrheit nach einem erfolgreichen Replace mit fehlgeschlagenem
                // Settings-Save. Ein erneutes HEAD reicht; kein erneuter großer Download nötig.
                datasetSettings.InstalledVersion = installed.Version;
                datasetSettings.InstalledSchemaVersion = installed.Schema;
                datasetSettings.LastUpdatedUtc = installed.BuiltUtc;
                datasetSettings.InstalledRevisionUtc = null;
                datasetSettings.LastCheckCompleted = false;
                PersistDatasetSettings(datasetSettings);
            }
            if ((databaseExists || string.IsNullOrWhiteSpace(datasetSettings.InstalledVersion))
                && datasetSettings.LastCheckCompleted
                && datasetSettings.LastCheckedSchemaVersion == ImdbDatasetIndexBuilder.SchemaVersion
                && datasetSettings.LastCheckedUtc is { } lastCheckedUtc
                && lastCheckedUtc <= DateTimeOffset.UtcNow
                && DateTimeOffset.UtcNow - lastCheckedUtc < SuccessfulCheckInterval)
            {
                return new ImdbDatasetStartupResult([]);
            }

            Report(progress, "IMDb-Daten werden geprüft...", "Prüfe offizielle Datensatzrevisionen.", 0d, false);
            var remoteFiles = await LoadRemoteMetadataAsync(cancellationToken);
            var versionToken = BuildVersionToken(remoteFiles);
            cancellationToken.ThrowIfCancellationRequested();

            if (databaseExists
                && datasetSettings.InstalledSchemaVersion == ImdbDatasetIndexBuilder.SchemaVersion
                && string.Equals(datasetSettings.InstalledVersion, versionToken, StringComparison.Ordinal))
            {
                datasetSettings.LastCheckedUtc = DateTimeOffset.UtcNow;
                datasetSettings.LastCheckCompleted = true;
                datasetSettings.LastCheckedSchemaVersion = ImdbDatasetIndexBuilder.SchemaVersion;
                PersistDatasetSettings(datasetSettings);
                Report(progress, "IMDb-Offlineindex aktuell", "Kein Download nötig.", 100d, false);
                return new ImdbDatasetStartupResult([]);
            }

            var offer = new ImdbDatasetUpdateOffer(
                IsInitialInstall: !databaseExists,
                AvailableVersionToken: versionToken,
                AvailableRevisionUtc: remoteFiles.Max(file => file.LastModifiedUtc),
                TotalDownloadBytes: remoteFiles.All(file => file.ContentLength is not null)
                    ? remoteFiles.Sum(file => file.ContentLength!.Value)
                    : null,
                InstalledVersionToken: datasetSettings.InstalledVersion,
                InstalledRevisionUtc: datasetSettings.InstalledRevisionUtc,
                InstalledAtUtc: datasetSettings.LastUpdatedUtc);
            var accepted = _consent.ConfirmUpdate(offer);
            cancellationToken.ThrowIfCancellationRequested();
            if (!accepted)
            {
                // Eine bewusste Ablehnung unterdrückt das identische Angebot für das normale
                // Prüfintervall. Abbruch und Fehler im anschließenden Update dürfen das nicht.
                datasetSettings.LastCheckedUtc = DateTimeOffset.UtcNow;
                datasetSettings.LastCheckCompleted = true;
                datasetSettings.LastCheckedSchemaVersion = ImdbDatasetIndexBuilder.SchemaVersion;
                PersistDatasetSettings(datasetSettings);
                Report(progress, "IMDb-Update übersprungen", "Der vorhandene Stand bleibt aktiv.", 100d, false);
                return new ImdbDatasetStartupResult([]);
            }

            // Vor dem potenziell langen Download wird nur der Abschlussmarker zurückgesetzt.
            // Bei Abbruch oder Prozessende bleibt der letzte installierte Index unangetastet,
            // die Prüfung wird beim nächsten Start aber nicht durch den Tages-Cache blockiert.
            datasetSettings.LastCheckCompleted = false;
            PersistDatasetSettings(datasetSettings);
            cancellationToken.ThrowIfCancellationRequested();
            await DownloadAndBuildAsync(remoteFiles, versionToken, installed, progress, cancellationToken);
            newIndexActivated = true;
            datasetSettings.InstalledVersion = versionToken;
            datasetSettings.InstalledSchemaVersion = ImdbDatasetIndexBuilder.SchemaVersion;
            datasetSettings.InstalledRevisionUtc = remoteFiles.Max(file => file.LastModifiedUtc);
            datasetSettings.LastCheckedUtc = DateTimeOffset.UtcNow;
            datasetSettings.LastCheckCompleted = true;
            datasetSettings.LastCheckedSchemaVersion = ImdbDatasetIndexBuilder.SchemaVersion;
            datasetSettings.LastUpdatedUtc = DateTimeOffset.UtcNow;
            PersistDatasetSettings(datasetSettings);
            Report(progress, "IMDb-Offlineindex bereit", "Download und Indexaufbau abgeschlossen.", 100d, false);
            return new ImdbDatasetStartupResult([]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !newIndexActivated)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (newIndexActivated)
            {
                // Der atomare Dateiaustausch ist bereits abgeschlossen; ein Settings-Fehler
                // darf nicht den falschen Eindruck erwecken, weiterhin sei der alte Index aktiv.
                return new ImdbDatasetStartupResult(
                    [$"Der neue IMDb-Offlineindex ist bereits aktiv. Die abschließenden Statusinformationen konnten nicht vollständig gespeichert oder gemeldet werden. Bitte die Einstellungen prüfen.{Environment.NewLine}{ex.Message}"]);
            }

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
        ImdbIndexInspection.Snapshot? previousIndex,
        IProgress<ManagedToolStartupProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_dataDirectory);
        // File.Replace ist nur innerhalb desselben Volumes atomar, auch bei expliziten Test-/Datenpfaden.
        var databaseDirectory = Path.GetDirectoryName(Path.GetFullPath(_databasePath))!;
        Directory.CreateDirectory(databaseDirectory);
        var stagingDirectory = Path.Combine(databaseDirectory, $".staging-{Guid.NewGuid():N}");
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
                        var percent = totalBytes is > 0 ? Math.Min(100d, downloadedBytes * 100d / totalBytes.Value) : (double?)null;
                        Report(
                            progress,
                            $"IMDb: {remoteFile.Descriptor.Name} wird geladen...",
                            totalBytes is > 0
                                ? $"{FormatBytes(downloadedBytes)} von {FormatBytes(totalBytes.Value)}"
                                : $"{FormatBytes(downloadedBytes)} geladen",
                            percent,
                            percent is null,
                            "Download");
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
                        $"IMDb: SQLite-Suchindex wird fertiggestellt ({value.DatasetNumber}/{value.DatasetCount})...",
                        value.DatasetName,
                        null,
                        true,
                        "Index");
                    return;
                }

                Report(
                    progress,
                    $"IMDb: {value.DatasetName} wird indexiert ({value.DatasetNumber}/{value.DatasetCount})...",
                    $"{value.ProcessedRowCount:N0} gelesen · {value.ImportedRowCount:N0} übernommen · {value.ProcessedRowsPerSecond:N0}/s · Datei {value.DatasetProgressPercent:0.0}% · Import {value.OverallProgressPercent:0.0}%",
                    value.OverallProgressPercent,
                    false,
                    "Import");
            });
            // Microsoft.Data.Sqlite führt lange SQLite-Operationen trotz Task-API synchron aus.
            // Der explizite Worker-Thread hält deshalb WPF-Dispatcher und Startdialog reaktionsfähig.
            await Task.Run(
                () => _indexBuilder.BuildAsync(
                    stagedDatabasePath,
                    archivePaths["title.basics"],
                    archivePaths["title.episode"],
                    archivePaths["title.akas"],
                    versionToken,
                    importProgress,
                    cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            Report(
                progress,
                "IMDb: Neuer Offlineindex wird aktiviert...",
                "Die bisherige Datenbank wird atomar ersetzt.",
                null,
                true,
                "Index");
            var candidate = await Task.Run(() => ImdbIndexInspection.Read(stagedDatabasePath, verifyIntegrity: true, cancellationToken), cancellationToken)
                ?? throw new InvalidDataException("Der neue IMDb-Index hat die Integritätsprüfung nicht bestanden.");
            ImdbIndexInspection.EnsurePlausibleReplacement(candidate, previousIndex);
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
        if (remoteFile.ETag is { } etag)
        {
            // Keine Mischung aus HEAD-Revision und zwischenzeitlich erneuerten Downloads aktivieren.
            request.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));
        }
        else if (remoteFile.LastModifiedUtc is { } lastModified)
        {
            request.Headers.IfUnmodifiedSince = lastModified;
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if ((remoteFile.ETag is not null && response.Headers.ETag is { } responseEtag
                && !string.Equals(remoteFile.ETag, responseEtag.Tag, StringComparison.Ordinal))
            || (remoteFile.LastModifiedUtc is { } expectedModified
                && response.Content.Headers.LastModified is { } actualModified && expectedModified != actualModified))
        {
            throw new InvalidDataException($"IMDb-Datei {remoteFile.Descriptor.FileName} wurde während der Aktualisierung geändert.");
        }
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
        _metadataStore.Update(settings =>
        {
            var updated = datasetSettings.Clone();
            if (settings.ImdbDataset is { ManagementPreferenceConfigured: true } current)
            {
                updated.AutoManageEnabled = current.AutoManageEnabled;
                updated.ManagementPreferenceConfigured = true;
            }

            settings.ImdbDataset = updated;
        });
    }

    private static string BuildVersionToken(IReadOnlyList<ImdbRemoteDatasetFile> files)
    {
        var datasetToken = string.Join(
            "|",
            files.Select(file =>
                $"{file.Descriptor.Name}:{file.ETag ?? string.Empty}:{file.LastModifiedUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty}:{file.ContentLength?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}"));
        var rawToken = $"schema:{ImdbDatasetIndexBuilder.SchemaVersion}|{datasetToken}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
    }

    private static void ReplaceDatabaseAtomically(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(targetPath))!);
        if (!File.Exists(targetPath))
        {
            File.Move(sourcePath, targetPath);
            return;
        }

        // Kein gemeinsamer .bak-Pfad und kein fehleranfälliges Cleanup nach dem Commit:
        // File.Replace erhält bei einem fehlgeschlagenen Austausch bereits das alte Ziel.
        File.Replace(sourcePath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
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
        bool indeterminate,
        string progressLabel = "Gesamt") =>
        progress?.Report(new ManagedToolStartupProgress(status, detail, percent, indeterminate, progressLabel));

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
