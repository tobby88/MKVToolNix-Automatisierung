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

        if (IsArchivePath(suggestedOutputPath))
        {
            if (PathComparisonHelper.AreSamePath(outputRootOverride, _archiveService.ArchiveRootDirectory))
            {
                return suggestedOutputPath;
            }

            var relativePath = PathComparisonHelper.TryGetRelativePathWithinRoot(
                suggestedOutputPath,
                _archiveService.ArchiveRootDirectory)
                ?? Path.GetFileName(suggestedOutputPath);
            return Path.Combine(outputRootOverride, relativePath);
        }

        return Path.Combine(outputRootOverride, Path.GetFileName(suggestedOutputPath));
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
}
