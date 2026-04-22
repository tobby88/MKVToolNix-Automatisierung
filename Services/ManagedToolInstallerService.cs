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
    /// <param name="cancellationToken">Abbruchsignal für Download und Entpacken.</param>
    /// <returns>Warnungen für den Startdialog, falls ein Werkzeug nicht automatisch bereitgestellt werden konnte.</returns>
    public async Task<ManagedToolStartupResult> EnsureManagedToolsAsync(CancellationToken cancellationToken = default)
    {
        PortableAppStorage.EnsureToolsDirectoryForSave();

        var settings = _toolPathStore.Load();
        var warnings = new List<string>();
        var hasChanges = false;

        hasChanges |= await EnsureManagedToolAsync(
            settings.ManagedMkvToolNix,
            ManagedToolKind.MkvToolNix,
            warnings,
            cancellationToken);
        hasChanges |= await EnsureManagedToolAsync(
            settings.ManagedFfprobe,
            ManagedToolKind.Ffprobe,
            warnings,
            cancellationToken);

        if (hasChanges)
        {
            _toolPathStore.Save(settings);
        }

        return new ManagedToolStartupResult(warnings);
    }

    private async Task<bool> EnsureManagedToolAsync(
        ManagedToolSettings toolSettings,
        ManagedToolKind toolKind,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (!toolSettings.AutoManageEnabled)
        {
            return false;
        }

        try
        {
            var latestPackage = await _packageSources[toolKind].GetLatestPackageAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var hasValidInstalledVersion = HasValidManagedInstallation(toolKind, toolSettings.InstalledPath);
            if (hasValidInstalledVersion
                && string.Equals(toolSettings.InstalledVersion, latestPackage.VersionToken, StringComparison.OrdinalIgnoreCase))
            {
                if (toolSettings.LastCheckedUtc != now)
                {
                    toolSettings.LastCheckedUtc = now;
                    return true;
                }

                return false;
            }

            var installedPath = await DownloadAndInstallAsync(latestPackage, cancellationToken);
            toolSettings.InstalledPath = installedPath;
            toolSettings.InstalledVersion = latestPackage.VersionToken;
            toolSettings.LastCheckedUtc = now;
            CleanupOlderManagedVersions(toolKind, latestPackage.VersionToken);
            return true;
        }
        catch (Exception ex)
        {
            if (!HasValidManagedInstallation(toolKind, toolSettings.InstalledPath))
            {
                warnings.Add(BuildWarningMessage(toolKind, ex));
            }

            return false;
        }
    }

    private async Task<string> DownloadAndInstallAsync(ManagedToolPackage package, CancellationToken cancellationToken)
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
            await DownloadArchiveAsync(package, archivePath, cancellationToken);
            _archiveExtractor.ExtractArchive(archivePath, stagingDirectory);

            Directory.Move(stagingDirectory, versionDirectory);
            return ResolveInstalledPath(package.Kind, versionDirectory);
        }
        finally
        {
            TryDeleteFile(archivePath);
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private async Task DownloadArchiveAsync(ManagedToolPackage package, string archivePath, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(package.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var fileStream = File.Create(archivePath))
        {
            await contentStream.CopyToAsync(fileStream, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(package.ExpectedSha256))
        {
            return;
        }

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
}
