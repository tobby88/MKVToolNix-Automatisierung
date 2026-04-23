using System.Globalization;
using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Services.Emby;

/// <summary>
/// Orchestriert den lokalen Emby-Abgleich aus JSON-Metadatenreport, NFO-Provider-IDs und Emby-API-Itemdaten.
/// </summary>
internal sealed class EmbyMetadataSyncService
{
    private readonly IEmbyClient _embyClient;
    private readonly EmbyNfoProviderIdService _nfoProviderIds;

    /// <summary>
    /// Initialisiert den Service mit API-Client und NFO-Helfer.
    /// </summary>
    public EmbyMetadataSyncService(IEmbyClient embyClient, EmbyNfoProviderIdService nfoProviderIds)
    {
        _embyClient = embyClient;
        _nfoProviderIds = nfoProviderIds;
    }

    /// <summary>
    /// Liest einen Batch-Report mit neu erzeugten Ausgabedateien.
    /// </summary>
    public IReadOnlyList<EmbyImportEntry> LoadNewOutputReport(string reportPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);
        if (!File.Exists(reportPath))
        {
            throw new FileNotFoundException("Der ausgewählte Metadatenreport wurde nicht gefunden.", reportPath);
        }

        if (!string.Equals(Path.GetExtension(reportPath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Der Emby-Abgleich erwartet den strukturierten JSON-Metadatenreport (*.metadata.json). Die ältere TXT-Dateiliste wird nicht mehr importiert.");
        }

        return LoadStructuredOutputReport(reportPath);
    }

    /// <summary>
    /// Prüft die Emby-Verbindung anhand der gespeicherten Serveradresse und des API-Keys.
    /// </summary>
    public Task<EmbyServerInfo> TestConnectionAsync(AppEmbySettings settings, CancellationToken cancellationToken = default)
    {
        return _embyClient.GetSystemInfoAsync(settings, cancellationToken);
    }

    /// <summary>
    /// Sucht die zu einem lokalen Archivwurzelpfad passende Emby-Bibliothek anhand der in Emby
    /// konfigurierten Virtual Folders. Damit muss die Bibliothekswurzel nicht mehr als normales
    /// Medien-Item auffindbar sein.
    /// </summary>
    public async Task<EmbyLibraryMatch?> FindSeriesLibraryAsync(
        AppEmbySettings settings,
        string? archiveRootPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(archiveRootPath))
        {
            return null;
        }

        var libraries = await _embyClient.GetLibrariesAsync(settings, cancellationToken);
        var exactMatch = libraries
            .SelectMany(library => library.Locations.Select(location => new EmbyLibraryMatch(library, location)))
            .FirstOrDefault(match => PathComparisonHelper.AreSamePath(match.MatchedLocation, archiveRootPath));
        if (exactMatch is not null)
        {
            return exactMatch;
        }

        var rootMatch = libraries
            .SelectMany(library => library.Locations.Select(location => new EmbyLibraryMatch(library, location)))
            .FirstOrDefault(match =>
                PathComparisonHelper.IsPathWithinRoot(archiveRootPath, match.MatchedLocation)
                || PathComparisonHelper.IsPathWithinRoot(match.MatchedLocation, archiveRootPath));
        if (rootMatch is not null)
        {
            return rootMatch;
        }

        return FindComparableAlignedLibrary(libraries, archiveRootPath);
    }

    /// <summary>
    /// Liest einen aktuellen Snapshot einer einzelnen Emby-Bibliothek.
    /// </summary>
    public async Task<EmbyLibraryFolder?> GetLibraryByIdAsync(
        AppEmbySettings settings,
        string libraryId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryId);

        var libraries = await _embyClient.GetLibrariesAsync(settings, cancellationToken);
        return libraries.FirstOrDefault(library => string.Equals(library.Id, libraryId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Startet bevorzugt einen gezielten Scan der konfigurierten Serienbibliothek.
    /// Falls Emby den Bibliothekswurzelpfad nicht direkt als Item auflösen kann,
    /// wird konservativ auf den globalen Library-Scan zurückgefallen.
    /// </summary>
    public async Task<EmbyLibraryScanTriggerResult> TriggerSeriesLibraryScanAsync(
        AppEmbySettings settings,
        string? archiveRootPath,
        CancellationToken cancellationToken = default)
    {
        var matchedLibrary = await FindSeriesLibraryAsync(settings, archiveRootPath, cancellationToken);
        if (matchedLibrary is not null)
        {
            await _embyClient.TriggerItemFileScanAsync(settings, matchedLibrary.Library.Id, cancellationToken);
            return new EmbyLibraryScanTriggerResult(
                UsedGlobalLibraryScan: false,
                $"Serienbibliotheksscan angestoßen: {matchedLibrary.Library.Name} ({matchedLibrary.MatchedLocation})",
                matchedLibrary.Library,
                matchedLibrary.MatchedLocation);
        }

        await _embyClient.TriggerLibraryScanAsync(settings, cancellationToken);
        return string.IsNullOrWhiteSpace(archiveRootPath)
            ? new EmbyLibraryScanTriggerResult(
                UsedGlobalLibraryScan: true,
                "Globaler Emby-Library-Scan angestoßen, weil kein Serienbibliothekspfad konfiguriert ist.",
                Library: null,
                MatchedLibraryPath: null)
            : new EmbyLibraryScanTriggerResult(
                UsedGlobalLibraryScan: true,
                $"Globaler Emby-Library-Scan angestoßen, weil in Emby keine passende Serienbibliothek zur Archivwurzel gefunden wurde: {archiveRootPath}",
                Library: null,
                MatchedLibraryPath: null);
    }

    /// <summary>
    /// Ermittelt NFO- und, falls konfiguriert, Emby-Provider-IDs für eine MKV.
    /// </summary>
    public async Task<EmbyFileAnalysis> AnalyzeFileAsync(
        AppEmbySettings settings,
        string mediaFilePath,
        bool queryEmby,
        string? archiveRootPath = null,
        EmbyLibraryMatch? libraryMatch = null,
        CancellationToken cancellationToken = default)
    {
        var localAnalysis = AnalyzeLocalFile(mediaFilePath);
        if (!queryEmby || !localAnalysis.MediaFileExists)
        {
            return localAnalysis;
        }

        var embyItem = await FindItemByPathAsync(
            settings,
            mediaFilePath,
            archiveRootPath,
            libraryMatch,
            cancellationToken);
        return localAnalysis.WithEmbyItem(embyItem);
    }

    /// <summary>
    /// Ermittelt nur lokale Datei- und NFO-Daten ohne Emby-Netzwerkzugriff.
    /// </summary>
    public EmbyFileAnalysis AnalyzeLocalFile(string mediaFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaFilePath);

        var mediaFileExists = File.Exists(mediaFilePath);
        var nfoResult = _nfoProviderIds.ReadProviderIds(mediaFilePath);
        return new EmbyFileAnalysis(
            mediaFilePath,
            nfoResult.NfoPath,
            mediaFileExists,
            nfoResult.NfoExists,
            nfoResult.ProviderIds,
            EmbyItem: null,
            nfoResult.WarningMessage);
    }

    /// <summary>
    /// Sucht das Emby-Item zu einer MKV.
    /// </summary>
    public Task<EmbyItem?> FindItemByPathAsync(
        AppEmbySettings settings,
        string mediaFilePath,
        string? archiveRootPath = null,
        EmbyLibraryMatch? libraryMatch = null,
        CancellationToken cancellationToken = default)
    {
        return FindItemByPathInternalAsync(
            settings,
            mediaFilePath,
            archiveRootPath,
            libraryMatch,
            cancellationToken);
    }

    /// <summary>
    /// Aktualisiert die Provider-IDs in der lokalen NFO.
    /// </summary>
    public EmbyNfoUpdateResult UpdateNfoProviderIds(string mediaFilePath, EmbyProviderIds providerIds, bool removeImdbId = false)
    {
        return _nfoProviderIds.UpdateProviderIds(mediaFilePath, providerIds, removeImdbId);
    }

    /// <summary>
    /// Markiert erfolgreich abgearbeitete Reporteinträge und verschiebt vollständig erledigte Reports in einen <c>done</c>-Unterordner.
    /// </summary>
    public EmbyReportCompletionResult MarkOutputReportsDone(
        IReadOnlyList<string> reportPaths,
        IReadOnlyCollection<string> completedMediaFilePaths)
    {
        ArgumentNullException.ThrowIfNull(reportPaths);
        ArgumentNullException.ThrowIfNull(completedMediaFilePaths);
        if (reportPaths.Count == 0 || completedMediaFilePaths.Count == 0)
        {
            return EmbyReportCompletionResult.Empty;
        }

        var completedPathSet = completedMediaFilePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (completedPathSet.Count == 0)
        {
            return EmbyReportCompletionResult.Empty;
        }

        var now = DateTimeOffset.Now;
        var updatedReports = new List<string>();
        var movedReports = new List<EmbyMovedReport>();
        var failedReports = new List<string>();

        foreach (var reportPath in reportPaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var completion = MarkSingleOutputReportDone(reportPath, completedPathSet, now);
                if (completion.MovedReport is not null)
                {
                    movedReports.Add(completion.MovedReport);
                }
                else if (!string.IsNullOrWhiteSpace(completion.UpdatedReportPath))
                {
                    updatedReports.Add(completion.UpdatedReportPath!);
                }
            }
            catch (Exception ex)
            {
                failedReports.Add($"{reportPath}: {ex.Message}");
            }
        }

        return new EmbyReportCompletionResult(updatedReports, movedReports, failedReports);
    }

    /// <summary>
    /// Fordert nach NFO-Änderungen einen gezielten Emby-Metadaten-Refresh an.
    /// </summary>
    public Task RefreshItemMetadataAsync(
        AppEmbySettings settings,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        return _embyClient.RefreshItemMetadataAsync(settings, itemId, cancellationToken);
    }

    private static IReadOnlyList<EmbyImportEntry> LoadStructuredOutputReport(string reportPath)
    {
        var report = BatchOutputMetadataReportJson.Deserialize(File.ReadAllText(reportPath));
        if (report is null)
        {
            throw new InvalidDataException("Der ausgewählte Metadaten-Report konnte nicht gelesen werden.");
        }

        return report.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.OutputPath))
            .Where(item => string.Equals(Path.GetExtension(item.OutputPath), ".mkv", StringComparison.OrdinalIgnoreCase))
            .Select(item => new EmbyImportEntry(item.OutputPath, BuildProviderIds(item)))
            .GroupBy(item => item.MediaFilePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.MediaFilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static SingleReportCompletionResult MarkSingleOutputReportDone(
        string reportPath,
        IReadOnlySet<string> completedMediaFilePaths,
        DateTimeOffset completedAt)
    {
        if (!File.Exists(reportPath))
        {
            return SingleReportCompletionResult.Empty;
        }

        var report = BatchOutputMetadataReportJson.Deserialize(File.ReadAllText(reportPath));
        if (report is null)
        {
            throw new InvalidDataException("Der Metadaten-Report konnte nicht gelesen werden.");
        }

        var relevantItems = report.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.OutputPath))
            .Where(item => string.Equals(Path.GetExtension(item.OutputPath), ".mkv", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (relevantItems.Count == 0)
        {
            return SingleReportCompletionResult.Empty;
        }

        var changed = false;
        foreach (var item in relevantItems.Where(item => completedMediaFilePaths.Contains(item.OutputPath)))
        {
            if (item.EmbySyncDone == true)
            {
                continue;
            }

            item.EmbySyncDone = true;
            item.EmbySyncDoneAt = completedAt;
            changed = true;
        }

        if (!changed)
        {
            return SingleReportCompletionResult.Empty;
        }

        var isComplete = relevantItems.All(item => item.EmbySyncDone == true);
        if (isComplete)
        {
            report.EmbySyncCompletedAt = completedAt;
        }

        File.WriteAllText(reportPath, BatchOutputMetadataReportJson.Serialize(report));

        if (!isComplete || IsAlreadyInDoneDirectory(reportPath))
        {
            return new SingleReportCompletionResult(reportPath, MovedReport: null);
        }

        var targetDirectory = Path.Combine(Path.GetDirectoryName(reportPath)!, "done");
        Directory.CreateDirectory(targetDirectory);
        var targetPath = BuildUniqueReportPath(Path.Combine(targetDirectory, Path.GetFileName(reportPath)));
        File.Move(reportPath, targetPath);
        return new SingleReportCompletionResult(
            UpdatedReportPath: null,
            new EmbyMovedReport(reportPath, targetPath));
    }

    private static bool IsAlreadyInDoneDirectory(string reportPath)
    {
        var directory = Path.GetFileName(Path.GetDirectoryName(reportPath));
        return string.Equals(directory, "done", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildUniqueReportPath(string targetPath)
    {
        if (!File.Exists(targetPath))
        {
            return targetPath;
        }

        var directory = Path.GetDirectoryName(targetPath)!;
        var fileName = Path.GetFileNameWithoutExtension(targetPath);
        var extension = Path.GetExtension(targetPath);
        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(directory, $"{fileName} ({index}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static EmbyProviderIds BuildProviderIds(BatchOutputMetadataEntry item)
    {
        var tvdbId = string.IsNullOrWhiteSpace(item.ProviderIds?.Tvdb)
            ? item.TvdbEpisodeId ?? item.Tvdb?.EpisodeId?.ToString(CultureInfo.InvariantCulture)
            : item.ProviderIds!.Tvdb;
        var imdbId = string.IsNullOrWhiteSpace(item.ProviderIds?.Imdb)
            ? null
            : item.ProviderIds!.Imdb;

        return new EmbyProviderIds(tvdbId, imdbId);
    }

    private async Task<EmbyItem?> FindItemByPathInternalAsync(
        AppEmbySettings settings,
        string mediaFilePath,
        string? archiveRootPath,
        EmbyLibraryMatch? libraryMatch,
        CancellationToken cancellationToken)
    {
        foreach (var lookupPath in await BuildLookupPathsAsync(settings, mediaFilePath, archiveRootPath, libraryMatch, cancellationToken))
        {
            var embyItem = await _embyClient.FindItemByPathAsync(settings, lookupPath, cancellationToken);
            if (embyItem is not null)
            {
                return embyItem;
            }
        }

        return null;
    }

    private async Task<IReadOnlyList<string>> BuildLookupPathsAsync(
        AppEmbySettings settings,
        string mediaFilePath,
        string? archiveRootPath,
        EmbyLibraryMatch? libraryMatch,
        CancellationToken cancellationToken)
    {
        var lookupPaths = new List<string>();
        // Emby kann auf Linux laufen, während die App Windows-Pfade kennt. Deshalb versuchen wir
        // zuerst den zur gematchten Bibliothek übersetzten Archivpfad und erst danach den Rohpfad.
        var translatedLookupPath = await TryTranslateMediaPathToEmbyLibraryAsync(
            settings,
            mediaFilePath,
            archiveRootPath,
            libraryMatch,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(translatedLookupPath))
        {
            lookupPaths.Add(translatedLookupPath);
        }

        if (!lookupPaths.Contains(mediaFilePath, StringComparer.OrdinalIgnoreCase))
        {
            lookupPaths.Add(mediaFilePath);
        }

        return lookupPaths;
    }

    private async Task<string?> TryTranslateMediaPathToEmbyLibraryAsync(
        AppEmbySettings settings,
        string mediaFilePath,
        string? archiveRootPath,
        EmbyLibraryMatch? libraryMatch,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(archiveRootPath))
        {
            return null;
        }

        var effectiveLibraryMatch = libraryMatch
            ?? await FindSeriesLibraryAsync(settings, archiveRootPath, cancellationToken);
        if (effectiveLibraryMatch is null)
        {
            return null;
        }

        var relativePath = PathComparisonHelper.TryGetRelativePathWithinRoot(mediaFilePath, archiveRootPath);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var translatedRelativePath = TryTranslateRelativePathWithinAlignedRoots(
            archiveRootPath,
            effectiveLibraryMatch.MatchedLocation,
            relativePath);
        if (translatedRelativePath is null)
        {
            return null;
        }

        return CombineLibraryLocationWithRelativePath(effectiveLibraryMatch.MatchedLocation, translatedRelativePath);
    }

    /// <summary>
    /// Sucht eine cross-platform plausible Zuordnung zwischen lokaler Archivwurzel und Emby-Library.
    /// </summary>
    /// <remarks>
    /// Windows- und Linux-Setups unterscheiden sich oft nur durch Präfixe oder dadurch, dass eine Seite
    /// einen Eltern- oder Kindordner des eigentlichen Medienroots konfiguriert hat. Die Ausrichtung sucht
    /// deshalb eine gemeinsame Segmentfolge, erlaubt aber bewusst nur Parent/Child-Beziehungen und keine
    /// bloßen Geschwisterpfade. Bei mehreren gleich guten Treffern wird konservativ <see langword="null"/>
    /// zurückgegeben, damit kein falscher Library-Scan angestoßen wird.
    /// </remarks>
    private static EmbyLibraryMatch? FindComparableAlignedLibrary(
        IReadOnlyList<EmbyLibraryFolder> libraries,
        string archiveRootPath)
    {
        var candidates = libraries
            .SelectMany(library => library.Locations.Select(location => new EmbyLibraryMatch(library, location)))
            .Select(match => new
            {
                Match = match,
                Alignment = TryAlignComparableRoots(archiveRootPath, match.MatchedLocation)
            })
            .Where(candidate => candidate.Alignment is not null)
            .Select(candidate => new
            {
                candidate.Match,
                Alignment = candidate.Alignment!,
                candidate.Alignment!.CommonSegmentCount,
                TrailingSegments = candidate.Alignment.LeftTrailingSegments.Count + candidate.Alignment.RightTrailingSegments.Count,
                IgnoredPrefixSegments = candidate.Alignment.LeftIgnoredPrefixCount + candidate.Alignment.RightIgnoredPrefixCount
            })
            .OrderByDescending(candidate => candidate.CommonSegmentCount)
            .ThenBy(candidate => candidate.TrailingSegments)
            .ThenBy(candidate => candidate.IgnoredPrefixSegments)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var bestCandidate = candidates[0];
        var isAmbiguous = candidates
            .Skip(1)
            .Any(candidate =>
                candidate.CommonSegmentCount == bestCandidate.CommonSegmentCount
                && candidate.TrailingSegments == bestCandidate.TrailingSegments
                && candidate.IgnoredPrefixSegments == bestCandidate.IgnoredPrefixSegments);

        return isAmbiguous ? null : bestCandidate.Match;
    }

    /// <summary>
    /// Übersetzt einen bereits lokal berechneten Relativpfad von der Archivwurzel auf den gematchten Emby-Library-Root.
    /// </summary>
    /// <remarks>
    /// Wenn Emby eine Eltern-Library des konfigurierten lokalen Roots nutzt, müssen die zusätzlichen lokalen
    /// Segmente vor den eigentlichen Datei-Relativpfad gesetzt werden. Nutzt Emby dagegen eine Kind-Library,
    /// muss dieser Unterordner aus dem lokalen Relativpfad entfernt werden, weil er im Remote-Root bereits
    /// enthalten ist. Geschwisterpfade werden hier absichtlich nicht "geraten".
    /// </remarks>
    private static string? TryTranslateRelativePathWithinAlignedRoots(
        string archiveRootPath,
        string matchedLibraryLocation,
        string localRelativePath)
    {
        var alignment = TryAlignComparableRoots(archiveRootPath, matchedLibraryLocation);
        if (alignment is null)
        {
            return null;
        }

        var relativeSegments = localRelativePath
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (alignment.RightTrailingSegments.Count > 0)
        {
            if (relativeSegments.Count < alignment.RightTrailingSegments.Count)
            {
                return null;
            }

            for (var index = 0; index < alignment.RightTrailingSegments.Count; index++)
            {
                if (!string.Equals(relativeSegments[index], alignment.RightTrailingSegments[index], StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            relativeSegments = relativeSegments
                .Skip(alignment.RightTrailingSegments.Count)
                .ToList();
        }

        if (alignment.LeftTrailingSegments.Count > 0)
        {
            relativeSegments.InsertRange(0, alignment.LeftTrailingSegments);
        }

        return string.Join(Path.DirectorySeparatorChar, relativeSegments);
    }

    private static string CombineLibraryLocationWithRelativePath(string libraryLocation, string relativePath)
    {
        var preferredSeparator = libraryLocation.Contains('/')
            ? '/'
            : '\\';
        var trimmedLibraryLocation = libraryLocation.TrimEnd('/', '\\');
        var relativeSegments = relativePath
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return relativeSegments.Length == 0
            ? trimmedLibraryLocation
            : trimmedLibraryLocation + preferredSeparator + string.Join(preferredSeparator, relativeSegments);
    }

    /// <summary>
    /// Baut eine conservative Parent/Child-Ausrichtung zwischen zwei plattformübergreifend vergleichbaren Roots.
    /// </summary>
    private static ComparableRootAlignment? TryAlignComparableRoots(string leftPath, string rightPath)
    {
        var leftSegments = GetComparablePathSegments(leftPath);
        var rightSegments = GetComparablePathSegments(rightPath);
        var minimumCommonSegments = Math.Min(2, Math.Min(leftSegments.Count, rightSegments.Count));
        if (minimumCommonSegments == 0)
        {
            return null;
        }

        ComparableRootAlignment? bestAlignment = null;
        var bestScore = int.MinValue;
        var hasAmbiguousBest = false;

        for (var leftStart = 0; leftStart < leftSegments.Count; leftStart++)
        {
            for (var rightStart = 0; rightStart < rightSegments.Count; rightStart++)
            {
                var commonSegmentCount = CountCommonForwardSegments(leftSegments, leftStart, rightSegments, rightStart);
                if (commonSegmentCount < minimumCommonSegments)
                {
                    continue;
                }

                var leftTrailingCount = leftSegments.Count - (leftStart + commonSegmentCount);
                var rightTrailingCount = rightSegments.Count - (rightStart + commonSegmentCount);
                if (leftTrailingCount > 0 && rightTrailingCount > 0)
                {
                    continue;
                }

                var reachesLeftEnd = leftStart + commonSegmentCount == leftSegments.Count;
                var reachesRightEnd = rightStart + commonSegmentCount == rightSegments.Count;
                if (!reachesLeftEnd && !reachesRightEnd)
                {
                    continue;
                }

                var alignment = new ComparableRootAlignment(
                    commonSegmentCount,
                    leftSegments
                        .Skip(leftStart + commonSegmentCount)
                        .ToArray(),
                    rightSegments
                        .Skip(rightStart + commonSegmentCount)
                        .ToArray(),
                    leftStart,
                    rightStart);
                var score = commonSegmentCount * 100
                            - (alignment.LeftTrailingSegments.Count + alignment.RightTrailingSegments.Count) * 10
                            - (alignment.LeftIgnoredPrefixCount + alignment.RightIgnoredPrefixCount);

                if (score > bestScore)
                {
                    bestAlignment = alignment;
                    bestScore = score;
                    hasAmbiguousBest = false;
                }
                else if (score == bestScore && bestAlignment is not null)
                {
                    hasAmbiguousBest = true;
                }
            }
        }

        return hasAmbiguousBest ? null : bestAlignment;
    }

    private static int CountCommonForwardSegments(
        IReadOnlyList<string> leftSegments,
        int leftStart,
        IReadOnlyList<string> rightSegments,
        int rightStart)
    {
        var count = 0;
        while (leftStart + count < leftSegments.Count
               && rightStart + count < rightSegments.Count
               && string.Equals(leftSegments[leftStart + count], rightSegments[rightStart + count], StringComparison.OrdinalIgnoreCase))
        {
            count++;
        }

        return count;
    }

    private static IReadOnlyList<string> GetComparablePathSegments(string path)
    {
        return path
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => !LooksLikeWindowsDrive(segment))
            .ToList();
    }

    private static bool LooksLikeWindowsDrive(string segment)
    {
        return segment.Length == 2
            && char.IsLetter(segment[0])
            && segment[1] == ':';
    }
}

internal sealed record EmbyImportEntry(string MediaFilePath, EmbyProviderIds ProviderIds);

internal sealed record EmbyReportCompletionResult(
    IReadOnlyList<string> UpdatedReportPaths,
    IReadOnlyList<EmbyMovedReport> MovedReports,
    IReadOnlyList<string> FailedReports)
{
    public static EmbyReportCompletionResult Empty { get; } = new([], [], []);
}

internal sealed record EmbyMovedReport(string SourcePath, string TargetPath);

internal sealed record SingleReportCompletionResult(string? UpdatedReportPath, EmbyMovedReport? MovedReport)
{
    public static SingleReportCompletionResult Empty { get; } = new(null, null);
}

/// <summary>
/// Beschreibt, ob der Emby-Abgleich den bevorzugten Serienbibliotheksscan oder den globalen Fallback nutzen musste.
/// </summary>
internal sealed record EmbyLibraryScanTriggerResult(
    bool UsedGlobalLibraryScan,
    string Message,
    EmbyLibraryFolder? Library,
    string? MatchedLibraryPath);

/// <summary>
/// Zuordnung zwischen konfigurierter Archivwurzel und der dazu passenden Emby-Bibliothek.
/// </summary>
internal sealed record EmbyLibraryMatch(
    EmbyLibraryFolder Library,
    string MatchedLocation);

/// <summary>
/// Kombiniert lokale NFO-Daten und optionale Emby-Item-Daten für eine einzelne MKV.
/// </summary>
internal sealed record EmbyFileAnalysis(
    string MediaFilePath,
    string NfoPath,
    bool MediaFileExists,
    bool NfoExists,
    EmbyProviderIds NfoProviderIds,
    EmbyItem? EmbyItem,
    string? WarningMessage)
{
    /// <summary>
    /// Effektive Provider-IDs, bei denen vorhandene NFO-Werte Vorrang vor Emby-Daten haben.
    /// </summary>
    public EmbyProviderIds EffectiveProviderIds => NfoProviderIds.MergeFallback(EmbyProviderIdsFromItem(EmbyItem));

    /// <summary>
    /// Erstellt eine Kopie mit aktualisiertem Emby-Item.
    /// </summary>
    public EmbyFileAnalysis WithEmbyItem(EmbyItem? embyItem)
    {
        return this with { EmbyItem = embyItem };
    }

    private static EmbyProviderIds EmbyProviderIdsFromItem(EmbyItem? item)
    {
        return item is null
            ? EmbyProviderIds.Empty
            : new EmbyProviderIds(
                item.GetProviderId("Tvdb") ?? item.GetProviderId("TvdbSeries"),
                item.GetProviderId("Imdb"));
    }
}

/// <summary>
/// Beschreibt die plattformübergreifende Ausrichtung zweier logisch zusammengehöriger Root-Pfade.
/// </summary>
internal sealed record ComparableRootAlignment(
    int CommonSegmentCount,
    IReadOnlyList<string> LeftTrailingSegments,
    IReadOnlyList<string> RightTrailingSegments,
    int LeftIgnoredPrefixCount,
    int RightIgnoredPrefixCount);
