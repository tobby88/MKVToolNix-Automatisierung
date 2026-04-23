using System.Text.RegularExpressions;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Baut Ausgabepfade für neue Folgen und kann vorgeschlagene Archivpfade in benutzerdefinierte Zielwurzeln umsetzen.
/// </summary>
internal sealed class EpisodeOutputPathService
{
    private static readonly string[] SpecialArchiveDirectoryNames =
    [
        "Specials",
        "Season 0",
        "Season 00",
        "Trailers",
        "Backdrops"
    ];

    private static readonly Regex ArchiveEpisodeFileNamePattern = new(
        @"^(?<series>.+?)\s+-\s+S(?<season>\d{1,4}|xx)E(?<episode>\d{1,4}(?:\s*-\s*(?:E)?\d{1,4})?|xx)\s+-\s+(?<title>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly SeriesArchiveService _archiveService;

    public EpisodeOutputPathService(SeriesArchiveService archiveService)
    {
        _archiveService = archiveService;
    }

    /// <summary>
    /// Baut den finalen Ausgabepfad unter Berücksichtigung einer optionalen Zielwurzel.
    /// </summary>
    /// <param name="fallbackDirectory">Quellordner-Fallback, falls die Bibliothek nicht erreichbar ist.</param>
    /// <param name="seriesName">Serienname der Folge.</param>
    /// <param name="seasonNumber">Normalisierte Staffelnummer oder Jahresstaffel.</param>
    /// <param name="episodeNumber">Normalisierte Episodennummer.</param>
    /// <param name="title">Episodentitel.</param>
    /// <param name="outputRootOverride">Optionale alternative Zielwurzel.</param>
    /// <returns>Vollständiger Ausgabe-MKV-Pfad.</returns>
    public string BuildOutputPath(
        string fallbackDirectory,
        string seriesName,
        string seasonNumber,
        string episodeNumber,
        string title,
        string? outputRootOverride = null)
    {
        var suggestedOutputPath = _archiveService.BuildSuggestedOutputPath(
            fallbackDirectory,
            seriesName,
            seasonNumber,
            episodeNumber,
            title);

        if (string.IsNullOrWhiteSpace(outputRootOverride))
        {
            return suggestedOutputPath;
        }

        if (PathComparisonHelper.AreSamePath(outputRootOverride, _archiveService.ArchiveRootDirectory))
        {
            // Fuer die Serienbibliothek selbst soll die fachliche Serien-/Staffelstruktur stabil bleiben,
            // auch wenn die Reachability-Prüfung der Bibliothek gerade negativ ausfällt.
            // Das ist besonders für AD-only-Fälle wichtig, weil deren Archivvergleich sonst schon im Scan
            // auf einen flachen Dateinamen zusammenfällt und vorhandene Bibliotheksdateien nicht mehr trifft.
            return _archiveService.BuildArchiveOutputPath(seriesName, seasonNumber, episodeNumber, title);
        }

        if (IsArchivePath(suggestedOutputPath))
        {
            var relativePath = PathComparisonHelper.TryGetRelativePathWithinRoot(
                suggestedOutputPath,
                _archiveService.ArchiveRootDirectory)
                ?? Path.GetFileName(suggestedOutputPath);
            return Path.Combine(outputRootOverride, relativePath);
        }

        return Path.Combine(outputRootOverride, Path.GetFileName(suggestedOutputPath));
    }

    /// <summary>
    /// Liefert für AD-only-Fälle optional den fachlich passenden Bibliothekspfad, wenn unter der konfigurierten
    /// Serienbibliothek bereits eine passende MKV existiert.
    /// Dieser Fallback hilft Batch-Zeilen mit reinem AD-Eingang, deren automatischer Ausgabepfad zwischenzeitlich
    /// auf einen anderen Ort zeigte, obwohl die eigentliche Bibliotheksdatei vorhanden ist.
    /// </summary>
    /// <param name="outputRootOverride">Aktuell gewaehlte Batch-Zielwurzel.</param>
    /// <param name="seriesName">Serienname der Episode.</param>
    /// <param name="seasonNumber">Normalisierte Staffelnummer oder Jahresstaffel.</param>
    /// <param name="episodeNumber">Normalisierte Episodennummer.</param>
    /// <param name="title">Episodentitel.</param>
    /// <returns>Bestehender Bibliothekspfad oder <see langword="null"/>, wenn kein passender Bibliothekstreffer vorliegt.</returns>
    public string? TryResolveExistingArchiveOutputPath(
        string? outputRootOverride,
        string seriesName,
        string seasonNumber,
        string episodeNumber,
        string title)
    {
        if (!PathComparisonHelper.AreSamePath(outputRootOverride, _archiveService.ArchiveRootDirectory))
        {
            return null;
        }

        var archiveOutputPath = _archiveService.BuildArchiveOutputPath(seriesName, seasonNumber, episodeNumber, title);
        if (File.Exists(archiveOutputPath))
        {
            return archiveOutputPath;
        }

        return TryFindUniqueArchiveMatchBySeriesAndTitle(seriesName, seasonNumber, episodeNumber, title);
    }

    /// <summary>
    /// Sucht für nicht sicher TVDB-zuordenbare Sonder- oder Bonusfolgen nach einer bereits vorhandenen
    /// Bibliotheksdatei in den üblichen Sondermaterial-Ordnern einer Serie.
    /// </summary>
    /// <param name="outputRootOverride">Aktuelle Zielwurzel. Ein Treffer wird nur für die aktive Serienbibliothek geliefert.</param>
    /// <param name="seriesNames">Mögliche lokale oder gemappte Seriennamen.</param>
    /// <param name="title">Lokal erkannter Episoden- oder Bonusmaterialtitel.</param>
    /// <param name="originalLanguage">Optional bekannte Originalsprache der Serie aus einem gespeicherten Serienmapping.</param>
    /// <returns>Eindeutiger Archivtreffer oder <see langword="null"/>, wenn kein belastbarer Treffer existiert.</returns>
    public ArchiveSpecialEpisodeMatch? TryResolveExistingSpecialArchiveMatch(
        string? outputRootOverride,
        IEnumerable<string?> seriesNames,
        string title,
        string? originalLanguage = null)
    {
        if (!_archiveService.IsArchiveAvailable()
            || !IsArchiveRootTarget(outputRootOverride)
            || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var normalizedTitleKey = BuildSpecialArchiveTitleKey(title);
        if (string.IsNullOrWhiteSpace(normalizedTitleKey))
        {
            return null;
        }

        var distinctSeriesNames = seriesNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinctSeriesNames.Count == 0)
        {
            return null;
        }

        var matches = distinctSeriesNames
            .SelectMany(seriesName => EnumerateSpecialArchiveFiles(seriesName)
                .Select(path => TryBuildSpecialArchiveCandidate(path, seriesName, title)))
            .Where(candidate => candidate is not null)
            .Cast<SpecialArchiveCandidate>()
            .Select(candidate => candidate with
            {
                Score = CalculateSpecialArchiveMatchScore(normalizedTitleKey, candidate.TitleKey)
            })
            .Where(candidate => candidate.Score >= 80)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.FolderRank)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matches.Count == 0)
        {
            return null;
        }

        var bestScore = matches[0].Score;
        var bestMatches = matches
            .Where(candidate => candidate.Score == bestScore)
            .ToList();
        if (bestMatches.Count != 1)
        {
            return null;
        }

        var best = bestMatches[0];
        return new ArchiveSpecialEpisodeMatch(
            best.Path,
            best.SeriesName,
            best.SeasonNumber,
            best.EpisodeNumber,
            best.Title,
            string.IsNullOrWhiteSpace(originalLanguage) ? null : originalLanguage.Trim());
    }

    /// <summary>
    /// Liefert optional einen UI-Hinweistext, wenn ein alternativer Zielordner wegen nicht erreichbarer Bibliothek
    /// nur eine flache Dateiliste statt der Serien-/Staffelstruktur aufnehmen kann.
    /// </summary>
    /// <param name="outputRootOverride">Aktuell gewählte alternative Zielwurzel.</param>
    /// <returns>Hinweistext für die GUI oder <see langword="null"/>, wenn kein zusätzlicher Hinweis nötig ist.</returns>
    public string? BuildOutputRootOverrideHint(string? outputRootOverride)
    {
        if (string.IsNullOrWhiteSpace(outputRootOverride)
            || _archiveService.IsArchiveAvailable()
            || PathComparisonHelper.AreSamePath(outputRootOverride, _archiveService.ArchiveRootDirectory))
        {
            return null;
        }

        return "Hinweis: Solange die Serienbibliothek nicht erreichbar ist, werden neue Ziele unter diesem Ordner bewusst flach als einzelne MKV-Dateien abgelegt.";
    }

    /// <summary>
    /// Prüft, ob ein Pfad innerhalb der konfigurierten Archivwurzel liegt.
    /// </summary>
    /// <param name="path">Zu prüfender Pfad.</param>
    /// <returns><see langword="true"/>, wenn der Pfad dem Archiv zugeordnet ist.</returns>
    public bool IsArchivePath(string? path)
    {
        return PathComparisonHelper.IsPathWithinRoot(path, _archiveService.ArchiveRootDirectory);
    }

    /// <summary>
    /// Sucht innerhalb des Serienordners der Bibliothek nach genau einer passenden MKV mit gleichem Titel.
    /// Dieser konservative Fallback greift nur, wenn der exakte Sollpfad nicht existiert, die Episode aber
    /// dennoch bereits archiviert wurde und der aktuelle Scan noch keinen stabilen Staffel-/Episodencode liefert.
    /// </summary>
    /// <param name="seriesName">Serienname der Episode.</param>
    /// <param name="seasonNumber">Normalisierte Staffelnummer oder Jahresstaffel.</param>
    /// <param name="episodeNumber">Normalisierte Episodennummer.</param>
    /// <param name="title">Episodentitel.</param>
    /// <returns>Eindeutig gefundener Bibliothekspfad oder <see langword="null"/> bei fehlendem oder mehrdeutigem Treffer.</returns>
    private string? TryFindUniqueArchiveMatchBySeriesAndTitle(
        string seriesName,
        string seasonNumber,
        string episodeNumber,
        string title)
    {
        var seriesDirectory = Path.Combine(
            _archiveService.ArchiveRootDirectory,
            EpisodeFileNameHelper.SanitizePathSegment(seriesName));
        if (!Directory.Exists(seriesDirectory))
        {
            return null;
        }

        var sanitizedTitle = Path.GetFileNameWithoutExtension(EpisodeFileNameHelper.SanitizeFileName($"{title}.mkv"));
        var titleSuffix = $" - {sanitizedTitle}";
        var titleMatches = Directory.EnumerateFiles(seriesDirectory, "*.mkv", SearchOption.AllDirectories)
            .Where(path => Path.GetFileNameWithoutExtension(path).EndsWith(titleSuffix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (titleMatches.Count == 0)
        {
            return null;
        }

        var normalizedSeasonNumber = EpisodeFileNameHelper.NormalizeSeasonNumber(seasonNumber);
        var normalizedEpisodeNumber = EpisodeFileNameHelper.NormalizeEpisodeNumber(episodeNumber);
        if (normalizedSeasonNumber != "xx" && normalizedEpisodeNumber != "xx")
        {
            var episodeToken = $" - S{normalizedSeasonNumber}E{normalizedEpisodeNumber} - ";
            var episodeCodeMatches = titleMatches
                .Where(path => Path.GetFileName(path).Contains(episodeToken, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (episodeCodeMatches.Count == 1)
            {
                return episodeCodeMatches[0];
            }
        }

        return titleMatches.Count == 1
            ? titleMatches[0]
            : null;
    }

    private bool IsArchiveRootTarget(string? outputRootOverride)
    {
        return string.IsNullOrWhiteSpace(outputRootOverride)
            || PathComparisonHelper.AreSamePath(outputRootOverride, _archiveService.ArchiveRootDirectory);
    }

    private IEnumerable<string> EnumerateSpecialArchiveFiles(string seriesName)
    {
        var seriesDirectory = Path.Combine(
            _archiveService.ArchiveRootDirectory,
            EpisodeFileNameHelper.SanitizePathSegment(seriesName));
        if (!Directory.Exists(seriesDirectory))
        {
            yield break;
        }

        var specialDirectories = Directory.EnumerateDirectories(seriesDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(directory => SpecialArchiveDirectoryNames.Contains(Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase))
            .OrderBy(GetSpecialArchiveDirectoryRank)
            .ThenBy(directory => directory, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var specialDirectory in specialDirectories)
        {
            foreach (var filePath in Directory.EnumerateFiles(specialDirectory, "*.mkv", SearchOption.AllDirectories))
            {
                yield return filePath;
            }
        }
    }

    private static SpecialArchiveCandidate? TryBuildSpecialArchiveCandidate(
        string filePath,
        string fallbackSeriesName,
        string sourceTitle)
    {
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
        var match = ArchiveEpisodeFileNamePattern.Match(fileNameWithoutExtension);
        var folderRank = GetSpecialArchiveDirectoryRank(Path.GetDirectoryName(filePath) ?? string.Empty);

        string seriesName;
        string seasonNumber;
        string episodeNumber;
        string title;

        if (match.Success)
        {
            seriesName = NormalizeArchiveDisplayText(match.Groups["series"].Value);
            seasonNumber = EpisodeFileNameHelper.NormalizeSeasonNumber(match.Groups["season"].Value);
            episodeNumber = EpisodeFileNameHelper.NormalizeEpisodeNumber(match.Groups["episode"].Value);
            title = NormalizeArchiveDisplayText(match.Groups["title"].Value);
        }
        else
        {
            seriesName = fallbackSeriesName;
            seasonNumber = IsSeasonZeroDirectory(filePath) ? "00" : "xx";
            episodeNumber = "xx";
            title = RemoveArchiveSeriesPrefix(fileNameWithoutExtension, fallbackSeriesName);
            title = NormalizeArchiveDisplayText(title);
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            title = NormalizeArchiveDisplayText(sourceTitle);
        }

        var titleKey = BuildSpecialArchiveTitleKey(title);
        return string.IsNullOrWhiteSpace(titleKey)
            ? null
            : new SpecialArchiveCandidate(
                filePath,
                seriesName,
                seasonNumber,
                episodeNumber,
                title,
                titleKey,
                folderRank,
                Score: 0);
    }

    private static int CalculateSpecialArchiveMatchScore(string sourceTitleKey, string archiveTitleKey)
    {
        if (string.Equals(sourceTitleKey, archiveTitleKey, StringComparison.Ordinal))
        {
            return 100;
        }

        if (sourceTitleKey.Length >= 8
            && archiveTitleKey.Length >= 8
            && (sourceTitleKey.Contains(archiveTitleKey, StringComparison.Ordinal)
                || archiveTitleKey.Contains(sourceTitleKey, StringComparison.Ordinal)))
        {
            return 88;
        }

        var sourceTokens = sourceTitleKey.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var archiveTokens = archiveTitleKey.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (sourceTokens.Length == 0 || archiveTokens.Length == 0)
        {
            return 0;
        }

        var sharedTokens = sourceTokens.Intersect(archiveTokens, StringComparer.OrdinalIgnoreCase).Count();
        var smallerTokenCount = Math.Min(sourceTokens.Length, archiveTokens.Length);
        return smallerTokenCount > 0 && sharedTokens == smallerTokenCount && sharedTokens >= 3
            ? 82
            : 0;
    }

    private static string BuildSpecialArchiveTitleKey(string value)
    {
        var normalized = NormalizeArchiveDisplayText(value);
        normalized = Regex.Replace(
            normalized,
            @"^\s*Extra(?:\s+zur\s+Folge\s+\d+)?\s*[-_:]\s*",
            string.Empty,
            RegexOptions.IgnoreCase);
        normalized = Regex.Replace(
            normalized,
            @"^\s*(?:Bonus|Special|Trailer|Backdrop)\s*[-_:]\s*",
            string.Empty,
            RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\(\s*mit\s+Audiodeskrip[^)]*\)", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\(\s*Audiodes(?:krip\w*)?[^)]*\)", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\(\s*H(?:ö|oe)rfassung\s*\)", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\bAudiodeskription\b", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\bAudiodes(?:krip\w*)?\b", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\bH(?:ö|oe)rfassung\b", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\bUT\b", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        return EpisodeMetadataMatchingHeuristics.NormalizeText(normalized);
    }

    private static string NormalizeArchiveDisplayText(string value)
    {
        var normalized = MojibakeRepair.NormalizeLikelyMojibake(value);
        normalized = EpisodeFileNameHelper.NormalizeTypography(normalized);
        normalized = normalized.Replace('_', ' ');
        normalized = Regex.Replace(normalized, @"-\d+$", string.Empty);
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        return Regex.Replace(normalized, @"\s*[-:]\s*$", string.Empty);
    }

    private static string RemoveArchiveSeriesPrefix(string fileNameWithoutExtension, string seriesName)
    {
        var normalizedFileName = NormalizeArchiveDisplayText(fileNameWithoutExtension);
        var normalizedSeriesName = NormalizeArchiveDisplayText(seriesName);
        if (normalizedFileName.StartsWith(normalizedSeriesName, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = normalizedFileName[normalizedSeriesName.Length..];
            remainder = Regex.Replace(remainder, @"^\s*[-_:]\s*", string.Empty);
            if (!string.IsNullOrWhiteSpace(remainder))
            {
                return remainder;
            }
        }

        return normalizedFileName;
    }

    private static bool IsSeasonZeroDirectory(string filePath)
    {
        var directoryName = Path.GetFileName(Path.GetDirectoryName(filePath) ?? string.Empty);
        return string.Equals(directoryName, "Specials", StringComparison.OrdinalIgnoreCase)
            || string.Equals(directoryName, "Season 0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(directoryName, "Season 00", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetSpecialArchiveDirectoryRank(string directoryPath)
    {
        var directoryName = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return directoryName.ToLowerInvariant() switch
        {
            "specials" => 0,
            "season 0" => 1,
            "season 00" => 1,
            "trailers" => 2,
            "backdrops" => 3,
            _ => 9
        };
    }

}

/// <summary>
/// Eindeutiger Treffer auf eine bereits vorhandene Sondermaterial-MKV im Serienarchiv.
/// </summary>
internal sealed record ArchiveSpecialEpisodeMatch(
    string OutputPath,
    string SeriesName,
    string SeasonNumber,
    string EpisodeNumber,
    string Title,
    string? OriginalLanguage);

internal sealed record SpecialArchiveCandidate(
    string Path,
    string SeriesName,
    string SeasonNumber,
    string EpisodeNumber,
    string Title,
    string TitleKey,
    int FolderRank,
    int Score);
