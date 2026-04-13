namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Bewertet Mediendateien mit bewusst billigen lokalen Signalen auf offensichtlich unvollständige Downloads.
/// </summary>
internal static class MediaFileHealth
{
    private const double MinimumExpectedSizeRatio = 0.75;
    private const long MinimumRelevantSizeShortfallBytes = 10L * 1024 * 1024;
    private const double MinimumPlausibleBytesPerSecond = 32d * 1024d;
    private static readonly TimeSpan MinimumDurationForBitrateHeuristic = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Prüft eine MP4-Datei gegen die zugehörigen TXT-Metadaten.
    /// </summary>
    /// <param name="filePath">Zu prüfende Mediendatei.</param>
    /// <param name="metadata">Begleitmetadaten derselben Quelle.</param>
    /// <returns><see cref="MediaFileHealthResult.Healthy"/>, wenn kein starkes Defekt-Signal vorliegt.</returns>
    public static MediaFileHealthResult CheckMp4File(string filePath, CompanionTextMetadata metadata)
    {
        return CheckMp4File(filePath, metadata.Duration, metadata.ExpectedSizeBytes, includeDurationHeuristic: true);
    }

    /// <summary>
    /// Prüft eine MP4-Datei gegen die zugehörigen erweiterten TXT-Metadaten.
    /// </summary>
    /// <param name="filePath">Zu prüfende Mediendatei.</param>
    /// <param name="details">Begleitmetadaten derselben Quelle.</param>
    /// <returns><see cref="MediaFileHealthResult.Healthy"/>, wenn kein starkes Defekt-Signal vorliegt.</returns>
    public static MediaFileHealthResult CheckMp4File(string filePath, CompanionTextDetails details)
    {
        return CheckMp4File(filePath, details.Duration, details.ExpectedSizeBytes, includeDurationHeuristic: true);
    }

    /// <summary>
    /// Prüft nur die explizit in der TXT angegebene Zielgröße.
    /// </summary>
    /// <param name="filePath">Zu prüfende Mediendatei.</param>
    /// <param name="metadata">Begleitmetadaten derselben Quelle.</param>
    /// <returns><see cref="MediaFileHealthResult.Healthy"/>, wenn kein starkes Defekt-Signal vorliegt.</returns>
    public static MediaFileHealthResult CheckMp4FileAgainstDeclaredSize(string filePath, CompanionTextMetadata metadata)
    {
        return CheckMp4File(filePath, expectedDuration: null, metadata.ExpectedSizeBytes, includeDurationHeuristic: false);
    }

    private static MediaFileHealthResult CheckMp4File(
        string filePath,
        TimeSpan? expectedDuration,
        long? expectedSizeBytes,
        bool includeDurationHeuristic)
    {
        if (!Path.GetExtension(filePath).Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(filePath))
        {
            return MediaFileHealthResult.Healthy;
        }

        var actualSizeBytes = new FileInfo(filePath).Length;
        if (IsClearlySmallerThanExpectedSize(actualSizeBytes, expectedSizeBytes))
        {
            return MediaFileHealthResult.Defective(
                $"MP4 ist deutlich kleiner als die in der TXT erwartete Größe ({FormatFileSize(actualSizeBytes)} statt {FormatFileSize(expectedSizeBytes!.Value)}).");
        }

        if (includeDurationHeuristic && IsClearlyTooSmallForExpectedDuration(actualSizeBytes, expectedDuration))
        {
            return MediaFileHealthResult.Defective(
                $"MP4 ist für die in der TXT angegebene Laufzeit auffällig klein ({FormatFileSize(actualSizeBytes)} bei {FormatDuration(expectedDuration!.Value)}).");
        }

        return MediaFileHealthResult.Healthy;
    }

    private static bool IsClearlySmallerThanExpectedSize(long actualSizeBytes, long? expectedSizeBytes)
    {
        if (expectedSizeBytes is null or <= 0)
        {
            return false;
        }

        var shortfallBytes = expectedSizeBytes.Value - actualSizeBytes;
        return shortfallBytes >= MinimumRelevantSizeShortfallBytes
            && actualSizeBytes < expectedSizeBytes.Value * MinimumExpectedSizeRatio;
    }

    private static bool IsClearlyTooSmallForExpectedDuration(long actualSizeBytes, TimeSpan? expectedDuration)
    {
        if (expectedDuration is null || expectedDuration.Value < MinimumDurationForBitrateHeuristic)
        {
            return false;
        }

        // Nur extreme Ausreißer werden ohne externen Probe-Aufruf aussortiert. Damit
        // bleiben absichtlich stark komprimierte, aber vollständige Dateien im normalen
        // Pfad, während typische abgebrochene ORF/HLS-Downloads von wenigen Sekunden
        // zuverlässig auffallen.
        var minimumPlausibleSize = expectedDuration.Value.TotalSeconds * MinimumPlausibleBytesPerSecond;
        return actualSizeBytes < minimumPlausibleSize;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["Bytes", "KiB", "MiB", "GiB", "TiB"];
        double value = Math.Max(0, bytes);
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours:0}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:0}:{duration.Seconds:00}";
    }
}

/// <summary>
/// Ergebnis einer billigen lokalen Datei-Health-Prüfung.
/// </summary>
internal sealed record MediaFileHealthResult(bool IsUsable, string? Reason)
{
    /// <summary>
    /// Ergebnis ohne starkes Defekt-Signal.
    /// </summary>
    public static MediaFileHealthResult Healthy { get; } = new(true, null);

    /// <summary>
    /// Erzeugt ein Ergebnis für eine offensichtlich defekte oder unvollständige Datei.
    /// </summary>
    /// <param name="reason">Kurzer, UI-tauglicher Grundtext.</param>
    /// <returns>Nicht verwendbares Health-Ergebnis.</returns>
    public static MediaFileHealthResult Defective(string reason)
    {
        return new MediaFileHealthResult(false, reason);
    }
}
