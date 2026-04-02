namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Baut Ausgabepfade für neue Folgen und kann vorgeschlagene Archivpfade in benutzerdefinierte Zielwurzeln umsetzen.
/// </summary>
internal sealed class EpisodeOutputPathService
{
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
            // auch wenn die Reachability-Pruefung der Bibliothek gerade negativ ausfaellt.
            // Das ist besonders fuer AD-only-Faelle wichtig, weil deren Archivvergleich sonst schon im Scan
            // auf einen flachen Dateinamen zusammenfaellt und vorhandene Bibliotheksdateien nicht mehr trifft.
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
    /// Liefert fuer AD-only-Faelle optional den fachlich passenden Bibliothekspfad, wenn unter der konfigurierten
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

        var normalizedSeasonNumber = EpisodeFileNameHelper.NormalizeEpisodeNumber(seasonNumber);
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
}
