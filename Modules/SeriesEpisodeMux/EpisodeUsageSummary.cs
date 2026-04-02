namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

/// <summary>
/// Kurzfassung für die UI, wie eine Episode relativ zur Bibliothek eingeordnet wird.
/// </summary>
public sealed record EpisodeUsageSummary(
    string ArchiveAction,
    string ArchiveDetails,
    EpisodeUsageEntry MainVideo,
    EpisodeUsageEntry AdditionalVideos,
    EpisodeUsageEntry Audio,
    EpisodeUsageEntry AudioDescription,
    EpisodeUsageEntry Subtitles,
    EpisodeUsageEntry Attachments)
{
    /// <summary>
    /// Erzeugt eine Platzhalterzusammenfassung für noch nicht berechnete Pläne.
    /// </summary>
    /// <param name="archiveAction">Text zur aktuellen Archivaktion.</param>
    /// <param name="archiveDetails">Zusätzliche Details zum Archivstatus.</param>
    /// <returns>Platzhalterzusammenfassung mit offenen Feldern.</returns>
    public static EpisodeUsageSummary CreatePending(string archiveAction, string archiveDetails)
    {
        return new EpisodeUsageSummary(
            archiveAction,
            archiveDetails,
            EpisodeUsageEntry.Pending,
            EpisodeUsageEntry.Pending,
            EpisodeUsageEntry.Pending,
            EpisodeUsageEntry.Pending,
            EpisodeUsageEntry.Pending,
            EpisodeUsageEntry.Pending);
    }
}

/// <summary>
/// Eine einzelne Zeile der Nutzungsübersicht inklusive optionaler Altteile, die entfallen oder ersetzt werden.
/// </summary>
public sealed record EpisodeUsageEntry(
    string CurrentText,
    string? RemovedText,
    string? RemovedReason)
{
    /// <summary>
    /// Platzhalter für noch nicht berechnete Nutzungsinformationen.
    /// </summary>
    public static EpisodeUsageEntry Pending { get; } = new("(wird berechnet)", null, null);

    /// <summary>
    /// Kennzeichnet, dass bisherige Bestandteile entfallen oder ersetzt werden.
    /// </summary>
    public bool HasRemoved => !string.IsNullOrWhiteSpace(RemovedText);

    /// <summary>
    /// Kennzeichnet, dass für entfernte Bestandteile ein fachlicher Grund vorliegt.
    /// </summary>
    public bool HasRemovedReason => !string.IsNullOrWhiteSpace(RemovedReason);
}
