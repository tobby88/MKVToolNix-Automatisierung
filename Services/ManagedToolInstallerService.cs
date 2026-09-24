using System.Security.Cryptography;
using System.Net.Http;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Lädt verwaltete externe Werkzeuge beim Start selbst nach und hält deren portable Versionen aktuell.
/// </summary>
internal interface IManagedToolInstallerService
{
    /// <summary>
    /// Prüft automatisch verwaltete Werkzeuge auf fehlende oder neuere Versionen und installiert sie bei Bedarf.
    /// </summary>
    /// <param name="progress">Optionaler Fortschrittskanal für Start- oder Einstellungsdialog.</param>
    /// <param name="cancellationToken">Abbruchsignal für Download und Entpacken.</param>
    /// <returns>Benutzerrelevante Warnungen, falls einzelne Werkzeuge nicht bereitgestellt werden konnten.</returns>
    Task<ManagedToolStartupResult> EnsureManagedToolsAsync(
        IProgress<ManagedToolStartupProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Lädt verwaltete externe Werkzeuge beim Start selbst nach und hält deren portable Versionen aktuell.
/// </summary>
internal sealed class ManagedToolInstallerService : IManagedToolInstallerService
{
    private static readonly SemaphoreSlim InstallationGate = new(1, 1);
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
    private readonly Action _ensureMediathekViewStopped;

    public ManagedToolInstallerService(
        AppToolPathStore toolPathStore,
        IEnumerable<IManagedToolPackageSource> packageSources,
        IManagedToolArchiveExtractor archiveExtractor,
        HttpClient httpClient,
        Action? ensureMediathekViewStopped = null)
    {
        _toolPathStore = toolPathStore;
        _packageSources = packageSources.ToDictionary(source => source.Kind);
        _archiveExtractor = archiveExtractor;
        _httpClient = httpClient;
        _ensureMediathekViewStopped = ensureMediathekViewStopped ?? MediathekViewStateGuard.EnsureStopped;
    }

    /// <summary>
    /// Prüft die verwalteten Werkzeuge beim Start auf fehlende oder neuere Versionen und installiert sie bei Bedarf.
    /// </summary>
    /// <param name="progress">Optionaler Fortschrittskanal für einen sichtbaren Startdialog.</param>
    /// <param name="cancellationToken">Abbruchsignal für Download und Entpacken.</param>
    /// <returns>Warnungen für den Startdialog, falls ein Werkzeug nicht automatisch bereitgestellt werden konnte.</returns>
    public async Task<ManagedToolStartupResult> EnsureManagedToolsAsync(
        IProgress<ManagedToolStartupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await InstallationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Archiv-I/O und portable Settings-Migration dürfen den WPF-Dispatcher nicht blockieren.
            return await Task.Run(() => EnsureManagedToolsCoreAsync(progress, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            InstallationGate.Release();
        }
    }

    private async Task<ManagedToolStartupResult> EnsureManagedToolsCoreAsync(
        IProgress<ManagedToolStartupProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Report(progress, "Werkzeuge werden vorbereitet...", "Prüfe automatische Werkzeugverwaltung.", 0d, false);

        var settings = _toolPathStore.Load();
        var warnings = new List<string>();
        (ManagedToolKind Kind, ManagedToolSettings Settings, double Start, double End)[] tools =
        [
            (ManagedToolKind.MkvToolNix, settings.ManagedMkvToolNix, 0d, 40d),
            (ManagedToolKind.Ffprobe, settings.ManagedFfprobe, 40d, 70d),
            (ManagedToolKind.MediathekView, settings.ManagedMediathekView, 70d, 100d)
        ];
        foreach (var tool in tools)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previousInstalledPath = tool.Settings.InstalledPath;
            if (!await EnsureManagedToolAsync(settings, tool.Settings, tool.Kind, warnings,
                    new ToolStartupProgressReporter(progress, tool.Start, tool.End), cancellationToken))
            {
                continue;
            }

            try
            {
                // Commit each completed tool before another tool can cancel or fail.
                _toolPathStore.Save(settings);
                CleanupPreviousManagedVersion(tool.Kind, previousInstalledPath, tool.Settings.InstalledPath);
            }
            catch (Exception ex)
            {
                warnings.Add(
                    "Die aktualisierten Werkzeugzustände konnten nicht dauerhaft gespeichert werden."
                    + Environment.NewLine
                    + ex.Message);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
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
            if (latestPackage.Kind != toolKind)
            {
                throw new InvalidOperationException("Die Paketquelle hat ein Paket fuer ein anderes Werkzeug geliefert.");
            }

            ValidatePackagePaths(latestPackage);
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

            var installedPath = await DownloadAndInstallAsync(latestPackage, toolPathSettings, toolSettings, progress, cancellationToken);
            toolSettings.InstalledPath = installedPath;
            toolSettings.InstalledVersion = latestPackage.VersionToken;
            toolSettings.LastCheckedUtc = now;
            toolSettings.LastFailedCheckUtc = null;
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
            toolSettings.LastFailedCheckUtc = ex is ToolStateInUseException ? null : now;
            if (ex is ToolStateInUseException || !IsToolCurrentlyUsable(toolPathSettings, toolKind))
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
        AppToolPathSettings toolPathSettings,
        ManagedToolSettings toolSettings,
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
            await _archiveExtractor.ExtractArchiveAsync(
                archivePath,
                stagingDirectory,
                progress?.CreateExtractionProgressAdapter(package.Kind),
                package.Kind,
                cancellationToken);

            var installedPathInStaging = ResolveInstalledPath(package.Kind, stagingDirectory);
            await PreserveToolStateBeforeReplacementAsync(
                package.Kind,
                toolPathSettings,
                toolSettings,
                installedPathInStaging,
                cancellationToken);
            var installedPathInVersionDirectory = MapStagingInstalledPathToVersionDirectory(
                stagingDirectory,
                versionDirectory,
                installedPathInStaging);

            cancellationToken.ThrowIfCancellationRequested();
            ReplaceVersionDirectoryWithStaging(stagingDirectory, versionDirectory,
                preservePrevious: package.Kind == ManagedToolKind.MediathekView);
            return installedPathInVersionDirectory;
        }
        finally
        {
            TryDeleteFile(archivePath);
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private async Task PreserveToolStateBeforeReplacementAsync(
        ManagedToolKind toolKind,
        AppToolPathSettings toolPathSettings,
        ManagedToolSettings toolSettings,
        string installedPathInStaging,
        CancellationToken cancellationToken)
    {
        if (toolKind != ManagedToolKind.MediathekView)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var targetStateDirectory = Path.Combine(ResolveMediathekViewStateBaseDirectory(installedPathInStaging), "Einstellungen");
        foreach (var sourceSettingsDirectory in EnumerateMediathekViewSettingsDirectories(
                     toolPathSettings,
                     toolSettings,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (PathComparisonHelper.AreSamePath(sourceSettingsDirectory, targetStateDirectory))
            {
                return;
            }

            _ensureMediathekViewStopped();
            await CopyDirectoryAsync(sourceSettingsDirectory, targetStateDirectory, cancellationToken);
            _ensureMediathekViewStopped();
            return;
        }
    }

    private static IEnumerable<string> EnumerateMediathekViewSettingsDirectories(
        AppToolPathSettings toolPathSettings,
        ManagedToolSettings toolSettings,
        CancellationToken cancellationToken)
    {
        foreach (var baseDirectory in EnumerateMediathekViewStateBaseDirectories(toolPathSettings, toolSettings, cancellationToken)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directSettingsDirectory = Path.Combine(baseDirectory, "Einstellungen");
            if (Directory.Exists(directSettingsDirectory))
            {
                yield return directSettingsDirectory;
            }

            IEnumerable<string> nestedSettingsDirectories;
            try
            {
                nestedSettingsDirectories = Directory
                    .EnumerateDirectories(baseDirectory, "Einstellungen", SearchOption.AllDirectories)
                    .OrderBy(path => path.Length)
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var nestedSettingsDirectory in nestedSettingsDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!PathComparisonHelper.AreSamePath(nestedSettingsDirectory, directSettingsDirectory))
                {
                    yield return nestedSettingsDirectory;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateMediathekViewStateBaseDirectories(
        AppToolPathSettings toolPathSettings,
        ManagedToolSettings toolSettings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TryGetExistingMediathekViewExecutablePath(toolSettings.InstalledPath) is { } managedExecutablePath)
        {
            yield return ResolveMediathekViewStateBaseDirectory(managedExecutablePath);
        }

        if (TryGetManagedVersionDirectory(ManagedToolKind.MediathekView, toolSettings.InstalledPath) is { } managedVersionDirectory)
        {
            yield return managedVersionDirectory;
        }

        if (ManagedToolResolution.TryResolveMediathekView(toolPathSettings)?.Path is { } resolvedPath
            && TryGetExistingMediathekViewExecutablePath(resolvedPath) is { } resolvedExecutablePath)
        {
            yield return ResolveMediathekViewStateBaseDirectory(resolvedExecutablePath);
        }

        foreach (var existingVersionDirectory in EnumerateExistingManagedToolVersionDirectories(ManagedToolKind.MediathekView))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return existingVersionDirectory;
        }

        foreach (var downloadsDirectory in EnumerateMediathekViewDownloadBaseDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return downloadsDirectory;
        }
    }

    private static IEnumerable<string> EnumerateExistingManagedToolVersionDirectories(ManagedToolKind toolKind)
    {
        var toolRootDirectory = GetToolRootDirectory(toolKind);
        if (!Directory.Exists(toolRootDirectory))
        {
            yield break;
        }

        IEnumerable<DirectoryInfo> versionDirectories;
        try
        {
            versionDirectories = Directory
                .EnumerateDirectories(toolRootDirectory)
                .Select(directory => new DirectoryInfo(directory))
                .Where(directory => !directory.Name.StartsWith(".", StringComparison.Ordinal))
                .OrderByToolVersion()
                .ToArray();
        }
        catch
        {
            yield break;
        }

        foreach (var versionDirectory in versionDirectories)
        {
            yield return versionDirectory.FullName;
        }
    }

    private static IEnumerable<string> EnumerateMediathekViewDownloadBaseDirectories()
    {
        var downloadsDirectory = PreferredDownloadDirectoryHelper.TryGetDownloadsDirectory();
        if (string.IsNullOrWhiteSpace(downloadsDirectory) || !Directory.Exists(downloadsDirectory))
        {
            yield break;
        }

        IEnumerable<DirectoryInfo> candidates;
        try
        {
            candidates = Directory
                .EnumerateDirectories(downloadsDirectory, "*MediathekView*", SearchOption.TopDirectoryOnly)
                .Select(directory => new DirectoryInfo(directory))
                .OrderByToolVersion()
                .ToArray();
        }
        catch
        {
            yield break;
        }

        foreach (var candidate in candidates)
        {
            yield return candidate.FullName;
        }
    }

    private static string? TryGetExistingMediathekViewExecutablePath(string? executablePath)
    {
        return !string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath)
            ? executablePath
            : null;
    }

    private static string ResolveMediathekViewStateBaseDirectory(string executablePath)
    {
        var executableDirectory = Path.GetDirectoryName(executablePath) ?? executablePath;
        return string.Equals(Path.GetFileName(executableDirectory), "Portable", StringComparison.OrdinalIgnoreCase)
            ? Directory.GetParent(executableDirectory)?.FullName ?? executableDirectory
            : executableDirectory;
    }

    private static string? TryGetManagedVersionDirectory(ManagedToolKind toolKind, string? installedPath)
    {
        if (string.IsNullOrWhiteSpace(installedPath))
        {
            return null;
        }

        var toolRootDirectory = GetToolRootDirectory(toolKind);
        var relativePath = PathComparisonHelper.TryGetRelativePathWithinRoot(installedPath, toolRootDirectory);
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath == ".")
        {
            return null;
        }

        var versionDirectoryName = relativePath
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(versionDirectoryName)
            ? null
            : Path.Combine(toolRootDirectory, versionDirectoryName);
    }

    private static async Task CopyDirectoryAsync(
        string sourceDirectory,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<(string Source, string Target)>();
        pending.Push((sourceDirectory, targetDirectory));
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(directory.Source) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Verknuepfte MediathekView-Einstellungen koennen nicht sicher migriert werden.");
            }

            Directory.CreateDirectory(directory.Target);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory.Source))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Verknuepfte MediathekView-Einstellungen koennen nicht sicher migriert werden.");
                }

                var target = Path.Combine(directory.Target, Path.GetFileName(entry));
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push((entry, target));
                    continue;
                }

                await using var input = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await input.CopyToAsync(output, cancellationToken);
            }
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
        var actualHash = !string.IsNullOrWhiteSpace(package.ExpectedSha256)
            ? await ComputeSha256Async(archivePath, cancellationToken)
            : await ComputeSha512Async(archivePath, cancellationToken);
        var expectedHash = !string.IsNullOrWhiteSpace(package.ExpectedSha256)
            ? package.ExpectedSha256
            : package.ExpectedSha512;
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
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

    private static async Task<string> ComputeSha512Async(string filePath, CancellationToken cancellationToken)
    {
        await using var fileStream = File.OpenRead(filePath);
        var hash = await SHA512.HashDataAsync(fileStream, cancellationToken);
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
            ManagedToolKind.MediathekView => File.Exists(installedPath),
            _ => false
        };
    }

    private static bool IsToolCurrentlyUsable(AppToolPathSettings settings, ManagedToolKind toolKind)
    {
        return toolKind switch
        {
            ManagedToolKind.MkvToolNix => ManagedToolResolution.TryResolveMkvToolNix(settings) is not null,
            ManagedToolKind.Ffprobe => ManagedToolResolution.TryResolveFfprobe(settings) is not null,
            ManagedToolKind.MediathekView => ManagedToolResolution.TryResolveMediathekView(settings) is not null,
            _ => false
        };
    }

    private static ToolPathResolutionSource ResolveCurrentSource(AppToolPathSettings settings, ManagedToolKind toolKind)
    {
        return toolKind switch
        {
            ManagedToolKind.MkvToolNix => ManagedToolResolution.TryResolveMkvToolNix(settings)?.Source ?? ToolPathResolutionSource.None,
            ManagedToolKind.Ffprobe => ManagedToolResolution.TryResolveFfprobe(settings)?.Source ?? ToolPathResolutionSource.None,
            ManagedToolKind.MediathekView => ManagedToolResolution.TryResolveMediathekView(settings)?.Source ?? ToolPathResolutionSource.None,
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
        if (string.IsNullOrWhiteSpace(package.ExpectedSha256)
            && string.IsNullOrWhiteSpace(package.ExpectedSha512))
        {
            throw new InvalidOperationException(
                $"{GetToolDisplayName(package.Kind)} kann nicht automatisch installiert werden, weil keine Prüfsumme verfügbar ist.");
        }

        if (!string.IsNullOrWhiteSpace(package.ExpectedSha256)
            && !ManagedToolParsing.IsValidSha256(package.ExpectedSha256))
        {
            throw new InvalidOperationException(
                $"{GetToolDisplayName(package.Kind)} kann nicht automatisch installiert werden, weil die gelieferte SHA-256-Prüfsumme ungültig ist.");
        }

        if (!string.IsNullOrWhiteSpace(package.ExpectedSha512)
            && !ManagedToolParsing.IsValidSha512(package.ExpectedSha512))
        {
            throw new InvalidOperationException(
                $"{GetToolDisplayName(package.Kind)} kann nicht automatisch installiert werden, weil die gelieferte SHA-512-Prüfsumme ungültig ist.");
        }
    }

    private static string ResolveInstalledPath(ManagedToolKind toolKind, string extractedDirectory)
    {
        return toolKind switch
        {
            ManagedToolKind.MkvToolNix => ResolveMkvToolNixDirectory(extractedDirectory),
            ManagedToolKind.Ffprobe => ResolveFfprobePath(extractedDirectory),
            ManagedToolKind.MediathekView => ResolveMediathekViewPath(extractedDirectory),
            _ => throw new ArgumentOutOfRangeException(nameof(toolKind), toolKind, null)
        };
    }

    private static string ResolveMkvToolNixDirectory(string extractedDirectory)
    {
        var mkvMergePath = Directory
            .EnumerateFiles(extractedDirectory, "mkvmerge.exe", SearchOption.AllDirectories)
            .Where(path => File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "mkvpropedit.exe")))
            .OrderBy(path => path.Length)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(mkvMergePath))
        {
            throw new InvalidOperationException("Die entpackte MKVToolNix-Version enthält nicht sowohl mkvmerge.exe als auch mkvpropedit.exe.");
        }

        return Path.GetDirectoryName(mkvMergePath)!;
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

    private static string ResolveMediathekViewPath(string extractedDirectory)
    {
        var portablePath = Directory
            .EnumerateFiles(extractedDirectory, "MediathekView_Portable.exe", SearchOption.AllDirectories)
            .OrderBy(path => path.Count(character => character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar))
            .ThenBy(path => path.Length)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(portablePath))
        {
            return portablePath;
        }

        var standardPath = Directory
            .EnumerateFiles(extractedDirectory, "MediathekView.exe", SearchOption.AllDirectories)
            .OrderBy(path => path.Count(character => character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar))
            .ThenBy(path => path.Length)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(standardPath))
        {
            throw new InvalidOperationException("Die entpackte MediathekView-Version enthält keine startbare Windows-Executable.");
        }

        return standardPath;
    }

    private static string GetToolRootDirectory(ManagedToolKind toolKind)
    {
        return Path.Combine(
            PortableAppStorage.ToolsDirectory,
            toolKind switch
            {
                ManagedToolKind.MkvToolNix => "mkvtoolnix",
                ManagedToolKind.Ffprobe => "ffprobe",
                ManagedToolKind.MediathekView => "mediathekview",
                _ => throw new ArgumentOutOfRangeException(nameof(toolKind), toolKind, null)
            });
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

    private static void ReplaceVersionDirectoryWithStaging(
        string stagingDirectory,
        string versionDirectory,
        bool preservePrevious)
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
            if (!preservePrevious)
            {
                TryDeleteDirectory(replacedDirectory);
            }
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
        return toolKind switch
        {
            ManagedToolKind.MkvToolNix => "MKVToolNix",
            ManagedToolKind.Ffprobe => "ffprobe",
            ManagedToolKind.MediathekView => "MediathekView",
            _ => toolKind.ToString()
        };
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

    private static void ValidatePackagePaths(ManagedToolPackage package)
    {
        var versionDirectoryName = SanitizePathSegment(package.VersionToken);
        if (string.IsNullOrWhiteSpace(package.VersionToken)
            || versionDirectoryName.StartsWith(".", StringComparison.Ordinal)
            || versionDirectoryName.EndsWith(".", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(package.ArchiveFileName)
            || package.ArchiveFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || package.ArchiveFileName is "." or ".."
            || package.ArchiveFileName.EndsWith(".", StringComparison.Ordinal)
            || package.ArchiveFileName.EndsWith(" ", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Das Werkzeugpaket enthaelt einen unsicheren Versions- oder Archivnamen.");
        }
    }

    private static void CleanupPreviousManagedVersion(
        ManagedToolKind toolKind,
        string previousInstalledPath,
        string currentInstalledPath)
    {
        // Portable MediathekView can store arbitrary user data, not only a folder named Downloads.
        // Keep its old installation (including same-version replacements) as a recovery copy.
        if (toolKind == ManagedToolKind.MediathekView
            || PathComparisonHelper.AreSamePath(previousInstalledPath, currentInstalledPath))
        {
            return;
        }

        var previousDirectory = TryGetManagedVersionDirectory(toolKind, previousInstalledPath);
        var currentDirectory = TryGetManagedVersionDirectory(toolKind, currentInstalledPath);
        if (previousDirectory is not null && currentDirectory is not null
            && !PathComparisonHelper.AreSamePath(previousDirectory, currentDirectory)
            && !Path.GetFileName(previousDirectory).StartsWith(".", StringComparison.Ordinal))
        {
            TryDeleteDirectory(previousDirectory);
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
        private long _lastReportTimestamp;
        private string? _lastStatusText;

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

            var timestamp = Environment.TickCount64;
            if (statusText == _lastStatusText && toolProgressPercent is < FinishedProgressPercent
                && timestamp - _lastReportTimestamp < 100)
            {
                return;
            }

            _lastStatusText = statusText;
            _lastReportTimestamp = timestamp;

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
