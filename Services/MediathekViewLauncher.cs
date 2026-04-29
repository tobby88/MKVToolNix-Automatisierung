using System.Diagnostics;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Löst installierte oder portable MediathekView-Executables auf.
/// </summary>
internal interface IMediathekViewLocator
{
    /// <summary>
    /// Sucht die aktuell startbare <c>MediathekView.exe</c>.
    /// </summary>
    /// <returns>Aufgelöster Pfad inklusive Fundquelle oder <see langword="null"/>, wenn nichts startbares gefunden wurde.</returns>
    ResolvedToolPath? TryFindMediathekView();
}

/// <summary>
/// Kapselt den Start der externen MediathekView-Anwendung.
/// </summary>
internal interface IMediathekViewLauncher
{
    /// <summary>
    /// Liefert den aktuell erkannten MediathekView-Pfad ohne die Anwendung zu starten.
    /// </summary>
    ResolvedToolPath? TryResolve();

    /// <summary>
    /// Startet MediathekView, falls eine startbare Executable gefunden wird.
    /// </summary>
    MediathekViewLaunchResult Launch();
}

/// <summary>
/// Produktiver MediathekView-Locator mit manueller Override-, Portable-, Installations- und PATH-Suche.
/// </summary>
internal sealed class MediathekViewLocator : IMediathekViewLocator
{
    private readonly AppToolPathStore _toolPathStore;

    public MediathekViewLocator(AppToolPathStore toolPathStore)
    {
        _toolPathStore = toolPathStore;
    }

    /// <inheritdoc />
    public ResolvedToolPath? TryFindMediathekView()
    {
        return MediathekViewPathResolver.TryResolve(_toolPathStore.Load());
    }
}

/// <summary>
/// Startet MediathekView über die Windows-Shell, damit installierte und portable Varianten identisch funktionieren.
/// </summary>
internal sealed class MediathekViewLauncher : IMediathekViewLauncher
{
    private readonly IMediathekViewLocator _locator;
    private readonly Func<ProcessStartInfo, bool> _processStarter;

    public MediathekViewLauncher(IMediathekViewLocator locator)
        : this(locator, StartProcess)
    {
    }

    internal MediathekViewLauncher(
        IMediathekViewLocator locator,
        Func<ProcessStartInfo, bool> processStarter)
    {
        _locator = locator;
        _processStarter = processStarter;
    }

    /// <inheritdoc />
    public ResolvedToolPath? TryResolve()
    {
        return _locator.TryFindMediathekView();
    }

    /// <inheritdoc />
    public MediathekViewLaunchResult Launch()
    {
        var resolvedPath = TryResolve();
        if (resolvedPath is null)
        {
            return MediathekViewLaunchResult.NotFound();
        }

        try
        {
            var startInfo = new ProcessStartInfo(resolvedPath.Path)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(resolvedPath.Path) ?? Environment.CurrentDirectory
            };
            return _processStarter(startInfo)
                ? MediathekViewLaunchResult.Started(resolvedPath)
                : MediathekViewLaunchResult.Failed(resolvedPath.Path, "Der Prozess konnte nicht gestartet werden.");
        }
        catch (Exception exception)
        {
            return MediathekViewLaunchResult.Failed(resolvedPath.Path, exception.Message);
        }
    }

    private static bool StartProcess(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
        return process is not null;
    }
}

/// <summary>
/// Ergebnis eines MediathekView-Startversuchs.
/// </summary>
/// <param name="IsSuccess">Kennzeichnet, ob der Start erfolgreich an die Windows-Shell übergeben wurde.</param>
/// <param name="ExecutablePath">Verwendeter oder fehlgeschlagener Executable-Pfad.</param>
/// <param name="Source">Fundquelle des Executable-Pfads.</param>
/// <param name="ErrorMessage">Fehlertext bei nicht gefundenem oder fehlgeschlagenem Start.</param>
internal sealed record MediathekViewLaunchResult(
    bool IsSuccess,
    string? ExecutablePath,
    ToolPathResolutionSource Source,
    string? ErrorMessage)
{
    public static MediathekViewLaunchResult Started(ResolvedToolPath resolvedPath)
    {
        return new MediathekViewLaunchResult(true, resolvedPath.Path, resolvedPath.Source, null);
    }

    public static MediathekViewLaunchResult NotFound()
    {
        return new MediathekViewLaunchResult(false, null, ToolPathResolutionSource.None, "MediathekView wurde nicht gefunden.");
    }

    public static MediathekViewLaunchResult Failed(string executablePath, string errorMessage)
    {
        return new MediathekViewLaunchResult(false, executablePath, ToolPathResolutionSource.None, errorMessage);
    }
}

/// <summary>
/// Gemeinsame Suchlogik für Settings-UI, Download-Modul und Launcher.
/// </summary>
internal static class MediathekViewPathResolver
{
    private const string MediathekViewExecutableName = "MediathekView.exe";
    private const string MediathekViewPortableExecutableName = "MediathekView_Portable.exe";
    private const string ManagedMediathekViewDirectoryName = "mediathekview";

    public static ResolvedToolPath? TryResolve(AppToolPathSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (TryResolveConfiguredPath(settings.MediathekViewPath) is { } manualPath)
        {
            return new ResolvedToolPath(manualPath, ToolPathResolutionSource.ManualOverride);
        }

        if (TryResolveConfiguredPath(settings.ManagedMediathekView.InstalledPath) is { } managedPath)
        {
            return new ResolvedToolPath(managedPath, ToolPathResolutionSource.ManagedSettings);
        }

        if (TryFindInPortableTools() is { } portableToolsHit)
        {
            return new ResolvedToolPath(portableToolsHit, ToolPathResolutionSource.PortableToolsFallback);
        }

        if (TryFindInDownloads() is { } downloadsHit)
        {
            return new ResolvedToolPath(downloadsHit, ToolPathResolutionSource.DownloadsFallback);
        }

        if (TryFindInKnownInstallLocations() is { } installedHit)
        {
            return new ResolvedToolPath(installedHit, ToolPathResolutionSource.InstalledApplication);
        }

        if (TryFindInPath() is { } pathHit)
        {
            return new ResolvedToolPath(pathHit, ToolPathResolutionSource.SystemPath);
        }

        return null;
    }

    private static string? TryFindInPortableTools()
    {
        var managedRoot = Path.Combine(PortableAppStorage.ToolsDirectory, ManagedMediathekViewDirectoryName);
        foreach (var candidateDirectory in EnumerateCandidateDirectories(managedRoot))
        {
            if (TryFindExecutableUnderRoot(candidateDirectory, preferPortable: true) is { } executablePath)
            {
                return executablePath;
            }
        }

        return null;
    }

    private static string? TryResolveConfiguredPath(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        if (File.Exists(configuredPath))
        {
            return configuredPath;
        }

        if (!Directory.Exists(configuredPath))
        {
            return null;
        }

        foreach (var directCandidate in EnumerateExecutableCandidates(configuredPath, preferPortable: true))
        {
            if (File.Exists(directCandidate))
            {
                return directCandidate;
            }
        }

        return TryFindExecutableUnderRoot(configuredPath, preferPortable: true);
    }

    private static string? TryFindInPath()
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var rawPath in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                foreach (var candidate in EnumerateExecutableCandidates(rawPath, preferPortable: false))
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
                // Defekte PATH-Segmente duerfen die restliche Suche nicht blockieren.
            }
        }

        return null;
    }

    private static string? TryFindInKnownInstallLocations()
    {
        foreach (var root in EnumerateKnownInstallRoots())
        {
            if (TryFindExecutableUnderRoot(root, preferPortable: false) is { } candidate)
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidateDirectories(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return [];
        }

        try
        {
            return Directory
                .EnumerateDirectories(rootDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(path =>
                {
                    var directoryName = Path.GetFileName(path);
                    return !directoryName.StartsWith(".staging-", StringComparison.OrdinalIgnoreCase)
                           && !directoryName.StartsWith(".download-", StringComparison.OrdinalIgnoreCase);
                })
                .Select(path => new DirectoryInfo(path))
                .OrderByDescending(directory => directory.LastWriteTimeUtc)
                .Select(directory => directory.FullName)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> EnumerateKnownInstallRoots()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        foreach (var root in roots.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.Combine(root, "MediathekView");
        }
    }

    private static string? TryFindInDownloads()
    {
        var downloadsDirectory = PreferredDownloadDirectoryHelper.TryGetDownloadsDirectory();
        if (string.IsNullOrWhiteSpace(downloadsDirectory) || !Directory.Exists(downloadsDirectory))
        {
            return null;
        }

        try
        {
            foreach (var directory in Directory
                         .EnumerateDirectories(downloadsDirectory, "*MediathekView*", SearchOption.TopDirectoryOnly)
                         .Select(path => new DirectoryInfo(path))
                         .OrderByDescending(directory => directory.LastWriteTimeUtc))
            {
                if (TryFindExecutableUnderRoot(directory.FullName, preferPortable: true) is { } candidate)
                {
                    return candidate;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string? TryFindExecutableUnderRoot(string rootDirectory, bool preferPortable)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return null;
        }

        try
        {
            foreach (var directCandidate in EnumerateExecutableCandidates(rootDirectory, preferPortable))
            {
                if (File.Exists(directCandidate))
                {
                    return directCandidate;
                }
            }

            foreach (var executableName in EnumerateExecutableNames(preferPortable))
            {
                var candidate = Directory
                    .EnumerateFiles(rootDirectory, executableName, SearchOption.AllDirectories)
                    .OrderBy(path => path.Count(character => character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar))
                    .ThenBy(path => path.Length)
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (candidate is not null)
                {
                    return candidate;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateExecutableCandidates(string directory, bool preferPortable)
    {
        foreach (var executableName in EnumerateExecutableNames(preferPortable))
        {
            yield return Path.Combine(directory, executableName);
        }
    }

    private static IEnumerable<string> EnumerateExecutableNames(bool preferPortable)
    {
        if (preferPortable)
        {
            yield return MediathekViewPortableExecutableName;
            yield return MediathekViewExecutableName;
        }
        else
        {
            yield return MediathekViewExecutableName;
            yield return MediathekViewPortableExecutableName;
        }
    }
}
