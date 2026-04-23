namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Kennzeichnet die beiden von der App automatisch verwaltbaren Werkzeuge.
/// </summary>
internal enum ManagedToolKind
{
    /// <summary>
    /// MKVToolNix inklusive <c>mkvmerge.exe</c> und <c>mkvpropedit.exe</c>.
    /// </summary>
    MkvToolNix,

    /// <summary>
    /// Optionales <c>ffprobe.exe</c> für zuverlässigere Laufzeitmessungen.
    /// </summary>
    Ffprobe
}

/// <summary>
/// Beschreibt ein von einer offiziellen Downloadquelle aufgelöstes Toolpaket.
/// </summary>
/// <param name="Kind">Werkzeug, zu dem das Paket gehört.</param>
/// <param name="VersionToken">Vergleichbarer Schlüssel für Install-/Updateentscheidungen.</param>
/// <param name="DisplayVersion">Benutzerlesbare Versionsdarstellung für Status und Tooltips.</param>
/// <param name="DownloadUri">Direkter Downloadlink zum Archiv.</param>
/// <param name="ArchiveFileName">Dateiname des herunterzuladenden Archivs.</param>
/// <param name="ExpectedSha256">Optional erwartete SHA-256-Prüfsumme.</param>
internal sealed record ManagedToolPackage(
    ManagedToolKind Kind,
    string VersionToken,
    string DisplayVersion,
    Uri DownloadUri,
    string ArchiveFileName,
    string? ExpectedSha256 = null);

/// <summary>
/// Ergebnis des Start-Upgrades für automatisch verwaltete Werkzeuge.
/// </summary>
/// <param name="Warnings">Benutzerrelevante Warnungen, falls ein Werkzeug nicht automatisch bereitgestellt werden konnte.</param>
internal sealed record ManagedToolStartupResult(IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// Kennzeichnet, ob eine Warnung an die UI weitergereicht werden sollte.
    /// </summary>
    public bool HasWarning => Warnings.Count > 0;

    /// <summary>
    /// Verdichtete Mehrzeilenmeldung für den Startdialog.
    /// </summary>
    public string? WarningMessage => HasWarning
        ? string.Join(Environment.NewLine + Environment.NewLine, Warnings)
        : null;
}

/// <summary>
/// Laufender Status der Werkzeugprüfung beim App-Start.
/// </summary>
/// <param name="StatusText">Kurzer Hauptstatus für den sichtbaren Startdialog.</param>
/// <param name="DetailText">Optionaler Detailtext, etwa Werkzeugname oder Bytefortschritt.</param>
/// <param name="ProgressPercent">Optionaler Prozentwert für determinate Schritte.</param>
/// <param name="IsIndeterminate">Kennzeichnet Schritte ohne belastbaren Prozentfortschritt.</param>
internal sealed record ManagedToolStartupProgress(
    string StatusText,
    string? DetailText = null,
    double? ProgressPercent = null,
    bool IsIndeterminate = true);

/// <summary>
/// Fortschritt einer laufenden Archiv-Extraktion.
/// </summary>
/// <param name="ExtractedEntryCount">Bereits vollständig extrahierte Datei-Einträge.</param>
/// <param name="TotalEntryCount">Gesamtanzahl der zu extrahierenden Datei-Einträge.</param>
/// <param name="CurrentEntryPath">Optionaler Name oder Pfad des gerade bearbeiteten Eintrags.</param>
internal sealed record ManagedToolExtractionProgress(
    int ExtractedEntryCount,
    int TotalEntryCount,
    string? CurrentEntryPath = null);

/// <summary>
/// Liefert die aktuelle Download-Metadaten eines automatisch verwalteten Werkzeugs.
/// </summary>
internal interface IManagedToolPackageSource
{
    /// <summary>
    /// Kennzeichnet das Werkzeug, für das diese Quelle Metadaten auflöst.
    /// </summary>
    ManagedToolKind Kind { get; }

    /// <summary>
    /// Ermittelt das aktuell bereitgestellte Paket aus der jeweiligen Primärquelle.
    /// </summary>
    /// <param name="cancellationToken">Abbruchsignal für Netzwerkzugriffe.</param>
    /// <returns>Aufgelöste Paketmetadaten inklusive Download-URL und Prüfsumme.</returns>
    Task<ManagedToolPackage> GetLatestPackageAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Beschreibt, aus welcher Quelle ein Werkzeugpfad aktuell aufgelöst wurde.
/// </summary>
internal enum ToolPathResolutionSource
{
    None,
    ManualOverride,
    ManagedSettings,
    PortableToolsFallback,
    SystemPath,
    DownloadsFallback
}

/// <summary>
/// Ergebnis einer erfolgreichen Auflösung eines einzelnen Werkzeugpfads.
/// </summary>
/// <param name="Path">Benutzbarer Dateipfad zur Executable.</param>
/// <param name="Source">Quelle, aus der der Pfad stammt.</param>
internal sealed record ResolvedToolPath(string Path, ToolPathResolutionSource Source);

/// <summary>
/// Ergebnis einer erfolgreichen Auflösung beider benötigten MKVToolNix-Executables.
/// </summary>
/// <param name="MkvMergePath">Benutzbarer Pfad zu <c>mkvmerge.exe</c>.</param>
/// <param name="MkvPropEditPath">Benutzbarer Pfad zu <c>mkvpropedit.exe</c>.</param>
/// <param name="Source">Quelle, aus der das Paar stammt.</param>
internal sealed record ResolvedMkvToolNixPaths(
    string MkvMergePath,
    string MkvPropEditPath,
    ToolPathResolutionSource Source);

/// <summary>
/// Kapselt die Pfadauflösung für manuelle Overrides, verwaltete Installationen und Fallback-Quellen.
/// </summary>
/// <remarks>
/// Die Logik lebt bewusst an einer Stelle, damit Installer, echte Locators und Settings-UI dieselbe
/// Priorisierung verwenden. Dadurch bleiben Statusanzeige, Startverhalten und tatsächliche Toolwahl konsistent.
/// </remarks>
internal static class ManagedToolResolution
{
    private const string DownloadsFolderName = "Downloads";
    private const string FfprobeExecutableName = "ffprobe.exe";
    private const string MkvMergeExecutableName = "mkvmerge.exe";
    private const string MkvPropEditExecutableName = "mkvpropedit.exe";
    private const string MkvToolNixDownloadsPrefix = "mkvtoolnix-64-bit-";
    private const string ManagedMkvToolNixDirectoryName = "mkvtoolnix";
    private const string ManagedFfprobeDirectoryName = "ffprobe";

    /// <summary>
    /// Versucht, die aktuell nutzbare <c>ffprobe.exe</c> anhand der bekannten Prioritätsreihenfolge zu finden.
    /// </summary>
    internal static ResolvedToolPath? TryResolveFfprobe(AppToolPathSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (TryResolveExistingExecutable(settings.FfprobePath) is { } manualOverride)
        {
            return new ResolvedToolPath(manualOverride, ToolPathResolutionSource.ManualOverride);
        }

        if (TryResolveExistingExecutable(settings.ManagedFfprobe.InstalledPath) is { } configuredManagedPath)
        {
            return new ResolvedToolPath(configuredManagedPath, ToolPathResolutionSource.ManagedSettings);
        }

        if (TryFindNewestExecutableUnderRoot(
                Path.Combine(PortableAppStorage.ToolsDirectory, ManagedFfprobeDirectoryName),
                FfprobeExecutableName) is { } portableManagedPath)
        {
            return new ResolvedToolPath(portableManagedPath, ToolPathResolutionSource.PortableToolsFallback);
        }

        if (TryFindInPath(FfprobeExecutableName) is { } pathHit)
        {
            return new ResolvedToolPath(pathHit, ToolPathResolutionSource.SystemPath);
        }

        if (TryFindFfprobeInDownloads() is { } downloadsHit)
        {
            return new ResolvedToolPath(downloadsHit, ToolPathResolutionSource.DownloadsFallback);
        }

        return null;
    }

    /// <summary>
    /// Versucht, beide benötigten MKVToolNix-Executables konsistent als Paar aufzulösen.
    /// </summary>
    internal static ResolvedMkvToolNixPaths? TryResolveMkvToolNix(AppToolPathSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (TryResolveMkvToolNixPairFromConfiguredPath(settings.MkvToolNixDirectoryPath) is { } manualOverride)
        {
            return manualOverride with { Source = ToolPathResolutionSource.ManualOverride };
        }

        if (TryResolveMkvToolNixPairFromConfiguredPath(settings.ManagedMkvToolNix.InstalledPath) is { } configuredManagedPair)
        {
            return configuredManagedPair with { Source = ToolPathResolutionSource.ManagedSettings };
        }

        if (TryFindNewestMkvToolNixPairUnderRoot(
                Path.Combine(PortableAppStorage.ToolsDirectory, ManagedMkvToolNixDirectoryName)) is { } portableManagedPair)
        {
            return portableManagedPair with { Source = ToolPathResolutionSource.PortableToolsFallback };
        }

        if (TryFindNewestMkvToolNixPairInDownloads() is { } downloadsFallback)
        {
            return downloadsFallback with { Source = ToolPathResolutionSource.DownloadsFallback };
        }

        return null;
    }

    private static string? TryResolveExistingExecutable(string? configuredPath)
    {
        return !string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath)
            ? configuredPath
            : null;
    }

    private static ResolvedMkvToolNixPaths? TryResolveMkvToolNixPairFromConfiguredPath(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        var candidateDirectory = configuredPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(configuredPath)
            : configuredPath;
        if (string.IsNullOrWhiteSpace(candidateDirectory))
        {
            return null;
        }

        var directExecutableName = configuredPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(configuredPath)
            : null;
        var mkvMergePath = string.Equals(directExecutableName, MkvMergeExecutableName, StringComparison.OrdinalIgnoreCase)
            ? configuredPath
            : Path.Combine(candidateDirectory, MkvMergeExecutableName);
        var mkvPropEditPath = string.Equals(directExecutableName, MkvPropEditExecutableName, StringComparison.OrdinalIgnoreCase)
            ? configuredPath
            : Path.Combine(candidateDirectory, MkvPropEditExecutableName);

        return File.Exists(mkvMergePath) && File.Exists(mkvPropEditPath)
            ? new ResolvedMkvToolNixPaths(mkvMergePath, mkvPropEditPath, ToolPathResolutionSource.None)
            : null;
    }

    private static ResolvedMkvToolNixPaths? TryFindNewestMkvToolNixPairUnderRoot(string rootDirectory)
    {
        foreach (var directory in EnumerateCandidateDirectories(rootDirectory))
        {
            try
            {
                var mkvMergePath = Directory
                    .EnumerateFiles(directory, MkvMergeExecutableName, SearchOption.AllDirectories)
                    .OrderBy(path => path.Length)
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                var mkvPropEditPath = Directory
                    .EnumerateFiles(directory, MkvPropEditExecutableName, SearchOption.AllDirectories)
                    .OrderBy(path => path.Length)
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(mkvMergePath)
                    && !string.IsNullOrWhiteSpace(mkvPropEditPath)
                    && string.Equals(
                        Path.GetDirectoryName(mkvMergePath),
                        Path.GetDirectoryName(mkvPropEditPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return new ResolvedMkvToolNixPaths(
                        mkvMergePath,
                        mkvPropEditPath,
                        ToolPathResolutionSource.None);
                }
            }
            catch
            {
                // Ein defekter Restordner darf die restliche Suche nicht blockieren.
            }
        }

        return null;
    }

    private static string? TryFindNewestExecutableUnderRoot(string rootDirectory, string executableName)
    {
        foreach (var directory in EnumerateCandidateDirectories(rootDirectory))
        {
            try
            {
                var directCandidate = Path.Combine(directory, executableName);
                if (File.Exists(directCandidate))
                {
                    return directCandidate;
                }

                var nestedCandidate = Directory
                    .EnumerateFiles(directory, executableName, SearchOption.AllDirectories)
                    .OrderBy(path => path.Count(character => character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar))
                    .ThenBy(path => path.Length)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(nestedCandidate))
                {
                    return nestedCandidate;
                }
            }
            catch
            {
                // Ein defekter Restordner darf die restliche Suche nicht blockieren.
            }
        }

        return null;
    }

    private static string? TryFindFfprobeInDownloads()
    {
        var downloadsDirectory = GetDownloadsDirectory();
        if (downloadsDirectory is null || !Directory.Exists(downloadsDirectory))
        {
            return null;
        }

        try
        {
            return Directory
                .EnumerateDirectories(downloadsDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => Path.GetFileName(path).Contains("ffmpeg", StringComparison.OrdinalIgnoreCase))
                .Select(path => new DirectoryInfo(path))
                .OrderByDescending(directory => directory.LastWriteTimeUtc)
                .SelectMany(FindFfprobeCandidatesInDirectory)
                .FirstOrDefault(File.Exists);
        }
        catch
        {
            return null;
        }
    }

    private static ResolvedMkvToolNixPaths? TryFindNewestMkvToolNixPairInDownloads()
    {
        var downloadsDirectory = GetDownloadsDirectory();
        if (downloadsDirectory is null || !Directory.Exists(downloadsDirectory))
        {
            return null;
        }

        try
        {
            foreach (var directory in Directory
                         .EnumerateDirectories(downloadsDirectory, $"{MkvToolNixDownloadsPrefix}*", SearchOption.TopDirectoryOnly)
                         .Select(path => new DirectoryInfo(path))
                         .OrderByDescending(directory => directory.LastWriteTimeUtc))
            {
                var directToolDirectory = Path.Combine(directory.FullName, ManagedMkvToolNixDirectoryName);
                var resolved = TryResolveMkvToolNixPairFromConfiguredPath(directToolDirectory)
                               ?? TryFindNewestMkvToolNixPairUnderRoot(directory.FullName);
                if (resolved is not null)
                {
                    return resolved;
                }
            }
        }
        catch
        {
            return null;
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

    private static IEnumerable<string> FindFfprobeCandidatesInDirectory(DirectoryInfo directory)
    {
        var directCandidates = new[]
        {
            Path.Combine(directory.FullName, "bin", FfprobeExecutableName),
            Path.Combine(directory.FullName, FfprobeExecutableName)
        };

        foreach (var candidate in directCandidates)
        {
            yield return candidate;
        }

        IEnumerable<string> recursiveCandidates;
        try
        {
            recursiveCandidates = Directory
                .EnumerateFiles(directory.FullName, FfprobeExecutableName, SearchOption.AllDirectories)
                .OrderBy(path => path.Count(character => character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar))
                .ThenBy(path => path.Length);
        }
        catch
        {
            yield break;
        }

        foreach (var candidate in recursiveCandidates)
        {
            yield return candidate;
        }
    }

    private static string? TryFindInPath(string executableName)
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
                var candidate = Path.Combine(rawPath, executableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Defekte PATH-Segmente werden bewusst ignoriert.
            }
        }

        return null;
    }

    private static string? GetDownloadsDirectory()
    {
        return PreferredDownloadDirectoryHelper.TryGetDownloadsDirectory();
    }
}
