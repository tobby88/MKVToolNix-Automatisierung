using System.Security.Cryptography;
using System.Net.Http;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Lädt MKVToolNix und ffprobe beim Start selbst nach und hält die verwalteten Toolversionen aktuell.
/// </summary>
internal sealed class ManagedToolInstallerService
{
    private static readonly TimeSpan SuccessfulMetadataRefreshInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan FailedMetadataRefreshBackoffInterval = TimeSpan.FromHours(2);
    private static readonly TimeSpan DownloadReadIdleTimeout = TimeSpan.FromSeconds(30);
    private const double MetadataLookupProgressPercent = 8d;
    private const double DownloadStartProgressPercent = 15d;
    private const double DownloadEndProgressPercent = 70d;
    private const double ChecksumVerificationProgressPercent = 78d;
    private const double ExtractionStartProgressPercent = 82d;
    private const double ExtractionEndProgressPercent = 96d;
    private const double FinishedProgressPercent = 100d;
    private readonly AppToolPathStore _toolPathStore;
    private readonly IReadOnlyDictionary<ManagedToolKind, IManagedToolPackageSource> _packageSources;
    private readonly IManagedToolArchiveExtractor _archiveExtractor;
    private readonly HttpClient _httpClient;

    public ManagedToolInstallerService(
        AppToolPathStore toolPathStore,
        IEnumerable<IManagedToolPackageSource> packageSources,
        IManagedToolArchiveExtractor archiveExtractor,
        HttpClient httpClient)
    {
        _toolPathStore = toolPathStore;
        _packageSources = packageSources.ToDictionary(source => source.Kind);
        _archiveExtractor = archiveExtractor;
        _httpClient = httpClient;
    }

    /// <summary>
    /// Prüft beide verwalteten Werkzeuge beim Start auf fehlende oder neuere Versionen und installiert sie bei Bedarf.
    /// </summary>
    /// <param name="progress">Optionaler Fortschrittskanal für einen sichtbaren Startdialog.</param>
    /// <param name="cancellationToken">Abbruchsignal für Download und Entpacken.</param>
    /// <returns>Warnungen für den Startdialog, falls ein Werkzeug nicht automatisch bereitgestellt werden konnte.</returns>
    public async Task<ManagedToolStartupResult> EnsureManagedToolsAsync(
        IProgress<ManagedToolStartupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Report(progress, "Werkzeuge werden vorbereitet...", "Prüfe automatische Werkzeugverwaltung.", 0d, false);

        var settings = _toolPathStore.Load();
        var warnings = new List<string>();
        var hasChanges = false;
        var mkvProgress = new ToolStartupProgressReporter(progress, 0d, 50d);
        var ffprobeProgress = new ToolStartupProgressReporter(progress, 50d, 100d);

        hasChanges |= await EnsureManagedToolAsync(
            settings,
            settings.ManagedMkvToolNix,
            ManagedToolKind.MkvToolNix,
            warnings,
            mkvProgress,
            cancellationToken);
        hasChanges |= await EnsureManagedToolAsync(
            settings,
            settings.ManagedFfprobe,
            ManagedToolKind.Ffprobe,
            warnings,
            ffprobeProgress,
            cancellationToken);

        if (hasChanges)
        {
            try
            {
                _toolPathStore.Save(settings);
            }
            catch (Exception ex)
            {
                warnings.Add(
                    "Die aktualisierten Werkzeugzustände konnten nicht dauerhaft gespeichert werden."
                    + Environment.NewLine
                    + ex.Message);
            }
        }

        Report(progress, "Werkzeuge bereit", warnings.Count == 0
            ? "Der Start kann fortgesetzt werden."
            : "Einige Werkzeuge konnten nicht automatisch aktualisiert werden.", 100d, false);
        return new ManagedToolStartupResult(warnings);
    }

    private async Task<bool> EnsureManagedToolAsync(
        AppToolPathSettings toolPathSettings,
        ManagedToolSettings toolSettings,
        ManagedToolKind toolKind,
        List<string> warnings,
        ToolStartupProgressReporter? progress,
        CancellationToken cancellationToken)
    {
        var resolvedSource = ResolveCurrentSource(toolPathSettings, toolKind);
        if (resolvedSource is ToolPathResolutionSource.ManualOverride)
        {
            progress?.Report(
                $"{GetToolDisplayName(toolKind)} wird übersprungen",
                BuildExternalSourceSkipMessage(toolKind, resolvedSource),
                FinishedProgressPercent,
                false);
            return false;
        }

        if (!toolSettings.AutoManageEnabled)
        {
            progress?.Report(
                $"{GetToolDisplayName(toolKind)} wird übersprungen",
                resolvedSource is ToolPathResolutionSource.ManagedSettings or ToolPathResolutionSource.PortableToolsFallback
                    ? "Automatische Updates sind deaktiviert; die vorhandene verwaltete Installation bleibt in Verwendung."
                    : "Automatische Verwaltung ist deaktiviert.",
                FinishedProgressPercent,
                false);
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (ShouldSkipOnlineCheck(toolSettings, resolvedSource, now))
        {
            progress?.Report(
                $"{GetToolDisplayName(toolKind)} bleibt unverändert",
                BuildDeferredOnlineCheckMessage(toolKind, toolSettings, resolvedSource, now),
                FinishedProgressPercent,
                false);
            return false;
        }

        try
        {
            progress?.Report(
                $"{GetToolDisplayName(toolKind)} wird geprüft...",
                "Suche nach aktueller Version.",
                MetadataLookupProgressPercent,
                false);
            var latestPackage = await _packageSources[toolKind].GetLatestPackageAsync(cancellationToken);
            if (HasValidManagedInstallation(toolKind, toolSettings, latestPackage.VersionToken))
            {
                toolSettings.LastCheckedUtc = now;
                toolSettings.LastFailedCheckUtc = null;
                progress?.Report(
                    $"{GetToolDisplayName(toolKind)} ist aktuell",
                    $"Version {latestPackage.DisplayVersion} ist bereits installiert.",
                    FinishedProgressPercent,
                    false);
                return true;
            }

            var installedPath = await DownloadAndInstallAsync(latestPackage, progress, cancellationToken);
            toolSettings.InstalledPath = installedPath;
            toolSettings.InstalledVersion = latestPackage.VersionToken;
            toolSettings.LastCheckedUtc = now;
            toolSettings.LastFailedCheckUtc = null;
            CleanupOlderManagedVersions(toolKind, latestPackage.VersionToken);
            progress?.Report(
                $"{GetToolDisplayName(toolKind)} wurde aktualisiert",
                $"Installiert: {latestPackage.DisplayVersion}",
                FinishedProgressPercent,
                false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            toolSettings.LastFailedCheckUtc = now;
            if (!IsToolCurrentlyUsable(toolPathSettings, toolKind))
            {
                warnings.Add(BuildWarningMessage(toolKind, ex));
            }

            progress?.Report(
                $"{GetToolDisplayName(toolKind)} konnte nicht vorbereitet werden",
                ex.Message,
                FinishedProgressPercent,
                false);
            return true;
        }
    }

    private async Task<string> DownloadAndInstallAsync(
        ManagedToolPackage package,
        ToolStartupProgressReporter? progress,
        CancellationToken cancellationToken)
    {
        var toolRootDirectory = GetToolRootDirectory(package.Kind);
        var versionDirectory = Path.Combine(toolRootDirectory, SanitizePathSegment(package.VersionToken));
        var stagingDirectory = Path.Combine(toolRootDirectory, $".staging-{Guid.NewGuid():N}");
        var archivePath = Path.Combine(toolRootDirectory, $".download-{Guid.NewGuid():N}-{package.ArchiveFileName}");

        try
        {
            PortableAppStorage.EnsureToolsDirectoryForSave();
            Directory.CreateDirectory(toolRootDirectory);
            ValidatePackageIntegrityMetadata(package);
            await DownloadArchiveAsync(package, archivePath, progress, cancellationToken);
            progress?.Report(
                $"{GetToolDisplayName(package.Kind)} wird entpackt...",
                $"{package.DisplayVersion} – Vorbereitung läuft...",
                ExtractionStartProgressPercent,
                false);
            await Task.Run(() => _archiveExtractor.ExtractArchive(
                archivePath,
                stagingDirectory,
                progress?.CreateExtractionProgressAdapter(package.Kind),
                package.Kind,
                cancellationToken), cancellationToken);

            var installedPathInStaging = ResolveInstalledPath(package.Kind, stagingDirectory);
            var installedPathInVersionDirectory = MapStagingInstalledPathToVersionDirectory(
                stagingDirectory,
                versionDirectory,
                installedPathInStaging);

            ReplaceVersionDirectoryWithStaging(stagingDirectory, versionDirectory);
            return installedPathInVersionDirectory;
        }
        finally
        {
            TryDeleteFile(archivePath);
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private async Task DownloadArchiveAsync(
        ManagedToolPackage package,
        string archivePath,
        ToolStartupProgressReporter? progress,
        CancellationToken cancellationToken)
    {
        ValidatePackageIntegrityMetadata(package);

        using var response = await _httpClient.GetAsync(package.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        progress?.Report(
            $"{GetToolDisplayName(package.Kind)} wird heruntergeladen...",
            package.DisplayVersion,
            DownloadStartProgressPercent,
            !contentLength.HasValue || contentLength.Value <= 0);

        await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var fileStream = File.Create(archivePath))
        {
            var buffer = new byte[81920];
            long totalRead = 0;

            while (true)
            {
                var bytesRead = await ReadWithIdleTimeoutAsync(contentStream, buffer, DownloadReadIdleTimeout, cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;

                if (contentLength.HasValue && contentLength.Value > 0)
                {
                    var percent = Math.Clamp((double)totalRead / contentLength.Value * 100d, 0d, 100d);
                    var stagePercent = DownloadStartProgressPercent
                                       + percent / 100d * (DownloadEndProgressPercent - DownloadStartProgressPercent);
                    progress?.Report(
                        $"{GetToolDisplayName(package.Kind)} wird heruntergeladen...",
                        $"{FormatFileSize(totalRead)} / {FormatFileSize(contentLength.Value)}",
                        stagePercent,
                        false);
                }
                else
                {
                    progress?.Report(
                        $"{GetToolDisplayName(package.Kind)} wird heruntergeladen...",
                        $"{FormatFileSize(totalRead)} übertragen",
                        DownloadStartProgressPercent);
                }
            }
        }

        progress?.Report(
            $"{GetToolDisplayName(package.Kind)} wird überprüft...",
            "Prüfsumme wird berechnet.",
            ChecksumVerificationProgressPercent,
            false);
        var actualHash = await ComputeSha256Async(archivePath, cancellationToken);
        if (!string.Equals(actualHash, package.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{GetToolDisplayName(package.Kind)} wurde heruntergeladen, aber die Prüfsumme stimmt nicht.");
        }
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var fileStream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(fileStream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static async ValueTask<int> ReadWithIdleTimeoutAsync(
        Stream stream,
        byte[] buffer,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            return await stream.ReadAsync(buffer, timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException("Der Werkzeugdownload liefert seit längerer Zeit keine weiteren Daten.");
        }
    }

    private static bool HasValidManagedInstallation(
        ManagedToolKind toolKind,
        ManagedToolSettings toolSettings,
        string expectedVersionToken)
    {
        var installedPath = toolSettings.InstalledPath;
        if (string.IsNullOrWhiteSpace(installedPath))
        {
            return false;
        }

        if (!string.Equals(toolSettings.InstalledVersion, expectedVersionToken, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedVersionDirectory = Path.Combine(GetToolRootDirectory(toolKind), SanitizePathSegment(expectedVersionToken));
        if (!PathComparisonHelper.IsPathWithinRoot(installedPath, expectedVersionDirectory))
        {
            return false;
        }

        return toolKind switch
        {
            ManagedToolKind.MkvToolNix => File.Exists(Path.Combine(installedPath, "mkvmerge.exe"))
                                          && File.Exists(Path.Combine(installedPath, "mkvpropedit.exe")),
            ManagedToolKind.Ffprobe => File.Exists(installedPath),
            _ => false
        };
    }

    private static bool IsToolCurrentlyUsable(AppToolPathSettings settings, ManagedToolKind toolKind)
    {
        return toolKind switch
        {
            ManagedToolKind.MkvToolNix => ManagedToolResolution.TryResolveMkvToolNix(settings) is not null,
            ManagedToolKind.Ffprobe => ManagedToolResolution.TryResolveFfprobe(settings) is not null,
            _ => false
        };
    }

    private static ToolPathResolutionSource ResolveCurrentSource(AppToolPathSettings settings, ManagedToolKind toolKind)
    {
        return toolKind switch
        {
            ManagedToolKind.MkvToolNix => ManagedToolResolution.TryResolveMkvToolNix(settings)?.Source ?? ToolPathResolutionSource.None,
            ManagedToolKind.Ffprobe => ManagedToolResolution.TryResolveFfprobe(settings)?.Source ?? ToolPathResolutionSource.None,
            _ => ToolPathResolutionSource.None
        };
    }

    private static bool ShouldSkipOnlineCheck(
        ManagedToolSettings toolSettings,
        ToolPathResolutionSource resolvedSource,
        DateTimeOffset now)
    {
        if (resolvedSource is not ToolPathResolutionSource.ManagedSettings
            and not ToolPathResolutionSource.PortableToolsFallback)
        {
            return false;
        }

        if (toolSettings.LastCheckedUtc is not null
            && now - toolSettings.LastCheckedUtc.Value < SuccessfulMetadataRefreshInterval)
        {
            return true;
        }

        return toolSettings.LastFailedCheckUtc is not null
               && now - toolSettings.LastFailedCheckUtc.Value < FailedMetadataRefreshBackoffInterval;
    }

    private static string BuildDeferredOnlineCheckMessage(
        ManagedToolKind toolKind,
        ManagedToolSettings toolSettings,
        ToolPathResolutionSource resolvedSource,
        DateTimeOffset now)
    {
        if (toolSettings.LastCheckedUtc is not null
            && now - toolSettings.LastCheckedUtc.Value < SuccessfulMetadataRefreshInterval)
        {
            return $"{GetToolDisplayName(toolKind)} wurde zuletzt erfolgreich geprüft am {toolSettings.LastCheckedUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm:ss}.";
        }

        if (toolSettings.LastFailedCheckUtc is not null)
        {
            return $"{GetToolDisplayName(toolKind)} bleibt vorerst auf der vorhandenen Installation, weil der letzte Online-Check fehlgeschlagen ist.";
        }

        return resolvedSource == ToolPathResolutionSource.PortableToolsFallback
            ? "Eine bereits vorhandene portable Installation wird weiterverwendet."
            : "Die vorhandene Installation wird weiterverwendet.";
    }

    private static string BuildExternalSourceSkipMessage(ManagedToolKind toolKind, ToolPathResolutionSource resolvedSource)
    {
        return resolvedSource switch
        {
            ToolPathResolutionSource.ManualOverride => "Ein manueller Override hat Vorrang vor der automatischen Verwaltung.",
            ToolPathResolutionSource.SystemPath => $"{GetToolDisplayName(toolKind)} wird bereits über den System-PATH gefunden.",
            ToolPathResolutionSource.DownloadsFallback => $"{GetToolDisplayName(toolKind)} wird bereits aus einem vorhandenen Download-Ordner erkannt.",
            _ => $"{GetToolDisplayName(toolKind)} wird bereits aus einer höher priorisierten Quelle verwendet."
        };
    }

    private static void ValidatePackageIntegrityMetadata(ManagedToolPackage package)
    {
        if (string.IsNullOrWhiteSpace(package.ExpectedSha256))
        {
            throw new InvalidOperationException(
                $"{GetToolDisplayName(package.Kind)} kann nicht automatisch installiert werden, weil keine SHA-256-Prüfsumme verfügbar ist.");
        }

        if (!ManagedToolParsing.IsValidSha256(package.ExpectedSha256))
        {
            throw new InvalidOperationException(
                $"{GetToolDisplayName(package.Kind)} kann nicht automatisch installiert werden, weil die gelieferte SHA-256-Prüfsumme ungültig ist.");
        }
    }

    private static string ResolveInstalledPath(ManagedToolKind toolKind, string extractedDirectory)
    {
        return toolKind switch
        {
            ManagedToolKind.MkvToolNix => ResolveMkvToolNixDirectory(extractedDirectory),
            ManagedToolKind.Ffprobe => ResolveFfprobePath(extractedDirectory),
            _ => throw new ArgumentOutOfRangeException(nameof(toolKind), toolKind, null)
        };
    }

    private static string ResolveMkvToolNixDirectory(string extractedDirectory)
    {
        var mkvMergePath = Directory
            .EnumerateFiles(extractedDirectory, "mkvmerge.exe", SearchOption.AllDirectories)
            .OrderBy(path => path.Length)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        var mkvPropEditPath = Directory
            .EnumerateFiles(extractedDirectory, "mkvpropedit.exe", SearchOption.AllDirectories)
            .OrderBy(path => path.Length)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(mkvMergePath) || string.IsNullOrWhiteSpace(mkvPropEditPath))
        {
            throw new InvalidOperationException("Die entpackte MKVToolNix-Version enthält nicht sowohl mkvmerge.exe als auch mkvpropedit.exe.");
        }

        var mkvMergeDirectory = Path.GetDirectoryName(mkvMergePath);
        var mkvPropEditDirectory = Path.GetDirectoryName(mkvPropEditPath);
        if (!string.Equals(mkvMergeDirectory, mkvPropEditDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Die entpackten MKVToolNix-Dateien liegen nicht in einem gemeinsamen Werkzeugordner.");
        }

        return mkvMergeDirectory!;
    }

    private static string ResolveFfprobePath(string extractedDirectory)
    {
        var ffprobePath = Directory
            .EnumerateFiles(extractedDirectory, "ffprobe.exe", SearchOption.AllDirectories)
            .OrderBy(path => path.Length)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(ffprobePath))
        {
            throw new InvalidOperationException("Die entpackte ffprobe-Version enthält keine ffprobe.exe.");
        }

        return ffprobePath;
    }

    private static string GetToolRootDirectory(ManagedToolKind toolKind)
    {
        return Path.Combine(
            PortableAppStorage.ToolsDirectory,
            toolKind == ManagedToolKind.MkvToolNix ? "mkvtoolnix" : "ffprobe");
    }

    private static string MapStagingInstalledPathToVersionDirectory(
        string stagingDirectory,
        string versionDirectory,
        string installedPathInStaging)
    {
        var installedRelativePath = Path.GetRelativePath(stagingDirectory, installedPathInStaging);
        return string.Equals(installedRelativePath, ".", StringComparison.Ordinal)
            ? versionDirectory
            : Path.Combine(versionDirectory, installedRelativePath);
    }

    private static void ReplaceVersionDirectoryWithStaging(string stagingDirectory, string versionDirectory)
    {
        var replacedDirectory = BuildReplacedVersionDirectoryPath(versionDirectory);
        var movedExistingVersion = false;

        if (Directory.Exists(versionDirectory))
        {
            Directory.Move(versionDirectory, replacedDirectory);
            movedExistingVersion = true;
        }

        try
        {
            Directory.Move(stagingDirectory, versionDirectory);
            TryDeleteDirectory(replacedDirectory);
        }
        catch
        {
            if (movedExistingVersion
                && !Directory.Exists(versionDirectory)
                && Directory.Exists(replacedDirectory))
            {
                Directory.Move(replacedDirectory, versionDirectory);
            }

            throw;
        }
    }

    private static string BuildReplacedVersionDirectoryPath(string versionDirectory)
    {
        var parentDirectory = Path.GetDirectoryName(versionDirectory)
                              ?? throw new InvalidOperationException("Der Zielordner für die Werkzeugversion ist ungültig.");
        var directoryName = Path.GetFileName(versionDirectory);
        var candidate = Path.Combine(parentDirectory, $".replaced-{directoryName}-{Guid.NewGuid():N}");
        while (Directory.Exists(candidate))
        {
            candidate = Path.Combine(parentDirectory, $".replaced-{directoryName}-{Guid.NewGuid():N}");
        }

        return candidate;
    }

    private static string BuildWarningMessage(ManagedToolKind toolKind, Exception ex)
    {
        return $"{GetToolDisplayName(toolKind)} konnte nicht automatisch bereitgestellt werden.{Environment.NewLine}{ex.Message}";
    }

    private static string GetToolDisplayName(ManagedToolKind toolKind)
    {
        return toolKind == ManagedToolKind.MkvToolNix ? "MKVToolNix" : "ffprobe";
    }

    private static void Report(
        IProgress<ManagedToolStartupProgress>? progress,
        string statusText,
        string? detailText = null,
        double? progressPercent = null,
        bool isIndeterminate = true)
    {
        progress?.Report(new ManagedToolStartupProgress(statusText, detailText, progressPercent, isIndeterminate));
    }

    private static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "current";
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        return new string(value
            .Trim()
            .Select(character => invalidCharacters.Contains(character) ? '-' : character)
            .ToArray());
    }

    private static void CleanupOlderManagedVersions(ManagedToolKind toolKind, string currentVersionToken)
    {
        var toolRootDirectory = GetToolRootDirectory(toolKind);
        if (!Directory.Exists(toolRootDirectory))
        {
            return;
        }

        var currentDirectoryName = SanitizePathSegment(currentVersionToken);
        foreach (var directory in Directory.EnumerateDirectories(toolRootDirectory))
        {
            var directoryName = Path.GetFileName(directory);
            if (string.Equals(directoryName, currentDirectoryName, StringComparison.OrdinalIgnoreCase)
                || directoryName.StartsWith(".staging-", StringComparison.OrdinalIgnoreCase)
                || directoryName.StartsWith(".download-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TryDeleteDirectory(directory);
        }
    }

    private static void TryDeleteDirectory(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        try
        {
            Directory.Delete(directoryPath, recursive: true);
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        try
        {
            File.Delete(filePath);
        }
        catch
        {
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["Bytes", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    /// <summary>
    /// Übersetzt werkzeugspezifische Fortschritte in einen monotonen Gesamtfortschritt des Startfensters.
    /// </summary>
    /// <remarks>
    /// Netzwerkdownload, Prüfsummenprüfung und Extraktion melden jeweils eigene Teilfortschritte.
    /// Dieser Adapter sorgt dafür, dass der Gesamtbalken im vorgeschalteten Startfenster trotz
    /// wechselnder Teilphasen niemals rückwärts springt.
    /// </remarks>
    private sealed class ToolStartupProgressReporter(
        IProgress<ManagedToolStartupProgress>? startupProgress,
        double phaseStartPercent,
        double phaseEndPercent)
    {
        private readonly double _phaseStartPercent = phaseStartPercent;
        private readonly double _phaseRange = Math.Max(0d, phaseEndPercent - phaseStartPercent);
        private double _lastMappedProgressPercent = phaseStartPercent;

        public void Report(
            string statusText,
            string? detailText = null,
            double? toolProgressPercent = null,
            bool isIndeterminate = true)
        {
            if (startupProgress is null)
            {
                return;
            }

            double? mappedPercent = toolProgressPercent is null
                ? null
                : Math.Clamp(
                    _phaseStartPercent + toolProgressPercent.Value / 100d * _phaseRange,
                    _phaseStartPercent,
                    _phaseStartPercent + _phaseRange);
            if (mappedPercent is not null)
            {
                _lastMappedProgressPercent = Math.Max(_lastMappedProgressPercent, mappedPercent.Value);
                mappedPercent = _lastMappedProgressPercent;
            }

            startupProgress.Report(new ManagedToolStartupProgress(statusText, detailText, mappedPercent, isIndeterminate));
        }

        public IProgress<ManagedToolExtractionProgress> CreateExtractionProgressAdapter(ManagedToolKind toolKind)
        {
            return new ExtractionProgressAdapter(toolKind, this);
        }
    }

    private sealed class ExtractionProgressAdapter(
        ManagedToolKind toolKind,
        ToolStartupProgressReporter toolProgress) : IProgress<ManagedToolExtractionProgress>
    {
        public void Report(ManagedToolExtractionProgress value)
        {
            var hasByteProgress = value.TotalByteCount is > 0 && value.ExtractedByteCount is not null;
            var percent = hasByteProgress
                ? Math.Clamp((double)value.ExtractedByteCount!.Value / value.TotalByteCount!.Value * 100d, 0d, 100d)
                : Math.Clamp((double)value.ExtractedEntryCount / Math.Max(1, value.TotalEntryCount) * 100d, 0d, 100d);
            var stagePercent = ExtractionStartProgressPercent
                               + percent / 100d * (ExtractionEndProgressPercent - ExtractionStartProgressPercent);
            var currentEntry = string.IsNullOrWhiteSpace(value.CurrentEntryPath)
                ? null
                : Path.GetFileName(value.CurrentEntryPath);
            var detailPrefix = hasByteProgress
                ? $"{value.ExtractedEntryCount} / {value.TotalEntryCount} Dateien – {FormatFileSize(value.ExtractedByteCount!.Value)} / {FormatFileSize(value.TotalByteCount!.Value)}"
                : $"{value.ExtractedEntryCount} / {value.TotalEntryCount} Dateien";
            var detail = currentEntry is null
                ? detailPrefix
                : $"{detailPrefix} – {currentEntry}";

            toolProgress.Report(
                $"{GetToolDisplayName(toolKind)} wird entpackt...",
                detail,
                stagePercent,
                false);
        }
    }
}
