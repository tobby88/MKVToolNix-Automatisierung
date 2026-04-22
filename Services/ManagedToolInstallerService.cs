using System.Security.Cryptography;
using System.Net.Http;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Lädt MKVToolNix und ffprobe beim Start selbst nach und hält die verwalteten Toolversionen aktuell.
/// </summary>
internal sealed class ManagedToolInstallerService
{
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
        PortableAppStorage.EnsureToolsDirectoryForSave();
        Report(progress, "Werkzeuge werden vorbereitet...", "Prüfe automatische Werkzeugverwaltung.");

        var settings = _toolPathStore.Load();
        var warnings = new List<string>();
        var hasChanges = false;

        hasChanges |= await EnsureManagedToolAsync(
            settings.ManagedMkvToolNix,
            ManagedToolKind.MkvToolNix,
            warnings,
            progress,
            cancellationToken);
        hasChanges |= await EnsureManagedToolAsync(
            settings.ManagedFfprobe,
            ManagedToolKind.Ffprobe,
            warnings,
            progress,
            cancellationToken);

        if (hasChanges)
        {
            _toolPathStore.Save(settings);
        }

        Report(progress, "Werkzeuge bereit", warnings.Count == 0
            ? "Der Start kann fortgesetzt werden."
            : "Einige Werkzeuge konnten nicht automatisch aktualisiert werden.", 100d, false);
        return new ManagedToolStartupResult(warnings);
    }

    private async Task<bool> EnsureManagedToolAsync(
        ManagedToolSettings toolSettings,
        ManagedToolKind toolKind,
        List<string> warnings,
        IProgress<ManagedToolStartupProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!toolSettings.AutoManageEnabled)
        {
            Report(progress, $"{GetToolDisplayName(toolKind)} wird übersprungen", "Automatische Verwaltung ist deaktiviert.", 100d, false);
            return false;
        }

        try
        {
            Report(progress, $"{GetToolDisplayName(toolKind)} wird geprüft...", "Suche nach aktueller Version.");
            var latestPackage = await _packageSources[toolKind].GetLatestPackageAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var hasValidInstalledVersion = HasValidManagedInstallation(toolKind, toolSettings.InstalledPath);
            if (hasValidInstalledVersion
                && string.Equals(toolSettings.InstalledVersion, latestPackage.VersionToken, StringComparison.OrdinalIgnoreCase))
            {
                Report(progress,
                    $"{GetToolDisplayName(toolKind)} ist aktuell",
                    $"Version {latestPackage.DisplayVersion} ist bereits installiert.",
                    100d,
                    false);
                if (toolSettings.LastCheckedUtc != now)
                {
                    toolSettings.LastCheckedUtc = now;
                    return true;
                }

                return false;
            }

            var installedPath = await DownloadAndInstallAsync(latestPackage, progress, cancellationToken);
            toolSettings.InstalledPath = installedPath;
            toolSettings.InstalledVersion = latestPackage.VersionToken;
            toolSettings.LastCheckedUtc = now;
            CleanupOlderManagedVersions(toolKind, latestPackage.VersionToken);
            Report(progress,
                $"{GetToolDisplayName(toolKind)} wurde aktualisiert",
                $"Installiert: {latestPackage.DisplayVersion}",
                100d,
                false);
            return true;
        }
        catch (Exception ex)
        {
            if (!HasValidManagedInstallation(toolKind, toolSettings.InstalledPath))
            {
                warnings.Add(BuildWarningMessage(toolKind, ex));
            }

            Report(progress,
                $"{GetToolDisplayName(toolKind)} konnte nicht vorbereitet werden",
                ex.Message,
                100d,
                false);
            return false;
        }
    }

    private async Task<string> DownloadAndInstallAsync(
        ManagedToolPackage package,
        IProgress<ManagedToolStartupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var toolRootDirectory = GetToolRootDirectory(package.Kind);
        Directory.CreateDirectory(toolRootDirectory);

        var versionDirectory = Path.Combine(toolRootDirectory, SanitizePathSegment(package.VersionToken));
        if (Directory.Exists(versionDirectory))
        {
            Directory.Delete(versionDirectory, recursive: true);
        }

        var stagingDirectory = Path.Combine(toolRootDirectory, $".staging-{Guid.NewGuid():N}");
        var archivePath = Path.Combine(toolRootDirectory, $".download-{Guid.NewGuid():N}-{package.ArchiveFileName}");

        try
        {
            await DownloadArchiveAsync(package, archivePath, progress, cancellationToken);
            Report(progress,
                $"{GetToolDisplayName(package.Kind)} wird entpackt...",
                $"{package.DisplayVersion} – Vorbereitung läuft...",
                0d,
                false);
            await Task.Run(() => _archiveExtractor.ExtractArchive(
                archivePath,
                stagingDirectory,
                new ExtractionProgressAdapter(package.Kind, progress)), cancellationToken);

            Directory.Move(stagingDirectory, versionDirectory);
            return ResolveInstalledPath(package.Kind, versionDirectory);
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
        IProgress<ManagedToolStartupProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(package.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        Report(progress,
            $"{GetToolDisplayName(package.Kind)} wird heruntergeladen...",
            package.DisplayVersion,
            progressPercent: contentLength.HasValue && contentLength.Value > 0 ? 0d : null,
            isIndeterminate: !contentLength.HasValue || contentLength.Value <= 0);

        await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var fileStream = File.Create(archivePath))
        {
            var buffer = new byte[81920];
            long totalRead = 0;

            while (true)
            {
                var bytesRead = await contentStream.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;

                if (contentLength.HasValue && contentLength.Value > 0)
                {
                    var percent = Math.Clamp((double)totalRead / contentLength.Value * 100d, 0d, 100d);
                    Report(progress,
                        $"{GetToolDisplayName(package.Kind)} wird heruntergeladen...",
                        $"{FormatFileSize(totalRead)} / {FormatFileSize(contentLength.Value)}",
                        percent,
                        false);
                }
                else
                {
                    Report(progress,
                        $"{GetToolDisplayName(package.Kind)} wird heruntergeladen...",
                        $"{FormatFileSize(totalRead)} übertragen");
                }
            }
        }

        if (string.IsNullOrWhiteSpace(package.ExpectedSha256))
        {
            return;
        }

        Report(progress,
            $"{GetToolDisplayName(package.Kind)} wird überprüft...",
            "Prüfsumme wird berechnet.");
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

    private static bool HasValidManagedInstallation(ManagedToolKind toolKind, string? installedPath)
    {
        if (string.IsNullOrWhiteSpace(installedPath))
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

    private sealed class ExtractionProgressAdapter(
        ManagedToolKind toolKind,
        IProgress<ManagedToolStartupProgress>? startupProgress) : IProgress<ManagedToolExtractionProgress>
    {
        public void Report(ManagedToolExtractionProgress value)
        {
            var total = Math.Max(1, value.TotalEntryCount);
            var percent = Math.Clamp((double)value.ExtractedEntryCount / total * 100d, 0d, 100d);
            var currentEntry = string.IsNullOrWhiteSpace(value.CurrentEntryPath)
                ? null
                : Path.GetFileName(value.CurrentEntryPath);
            var detail = currentEntry is null
                ? $"{value.ExtractedEntryCount} / {value.TotalEntryCount} Dateien"
                : $"{value.ExtractedEntryCount} / {value.TotalEntryCount} Dateien – {currentEntry}";

            ManagedToolInstallerService.Report(
                startupProgress,
                $"{GetToolDisplayName(toolKind)} wird entpackt...",
                detail,
                percent,
                false);
        }
    }
}
