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
    /// Ergänzende Hinweise, die fachlich zur Planung gehören, aber nicht redundant zur Archivzeile sind.
    /// </summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>
    /// Kennzeichnet, dass im zusammengeführten Planblock zusätzliche Hinweise angezeigt werden sollen.
    /// </summary>
    public bool HasNotes => Notes.Count > 0;

    /// <inheritdoc />
    public bool Equals(EpisodeUsageSummary? other)
    {
        return other is not null
            && string.Equals(ArchiveAction, other.ArchiveAction, StringComparison.Ordinal)
            && string.Equals(ArchiveDetails, other.ArchiveDetails, StringComparison.Ordinal)
            && EqualityComparer<EpisodeUsageEntry>.Default.Equals(MainVideo, other.MainVideo)
            && EqualityComparer<EpisodeUsageEntry>.Default.Equals(AdditionalVideos, other.AdditionalVideos)
            && EqualityComparer<EpisodeUsageEntry>.Default.Equals(Audio, other.Audio)
            && EqualityComparer<EpisodeUsageEntry>.Default.Equals(AudioDescription, other.AudioDescription)
            && EqualityComparer<EpisodeUsageEntry>.Default.Equals(Subtitles, other.Subtitles)
            && EqualityComparer<EpisodeUsageEntry>.Default.Equals(Attachments, other.Attachments)
            && Notes.SequenceEqual(other.Notes, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ArchiveAction, StringComparer.Ordinal);
        hash.Add(ArchiveDetails, StringComparer.Ordinal);
        hash.Add(MainVideo);
        hash.Add(AdditionalVideos);
        hash.Add(Audio);
        hash.Add(AudioDescription);
        hash.Add(Subtitles);
        hash.Add(Attachments);
        foreach (var note in Notes)
        {
            hash.Add(note, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

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
public sealed record EpisodeUsageEntry
{
    /// <summary>
    /// Initialisiert eine reine Textzeile. Bestehende Aufrufer bleiben dadurch kompatibel, während die UI
    /// bei neueren Plänen zusätzlich typisierte Einzelbestandteile auswerten kann.
    /// </summary>
    public EpisodeUsageEntry(string CurrentText, string? RemovedText, string? RemovedReason)
        : this(CurrentText, RemovedText, RemovedReason, SplitCurrentText(CurrentText))
    {
    }

    /// <summary>
    /// Initialisiert eine Zeile mit explizit typisierten Einzelbestandteilen.
    /// </summary>
    public EpisodeUsageEntry(
        string CurrentText,
        string? RemovedText,
        string? RemovedReason,
        IReadOnlyList<EpisodeUsageItem> currentItems)
    {
        this.CurrentText = CurrentText;
        this.RemovedText = RemovedText;
        this.RemovedReason = RemovedReason;
        CurrentItems = currentItems.Count == 0
            ? [new EpisodeUsageItem(CurrentText, EpisodeUsageItemKind.Neutral)]
            : currentItems;
    }

    /// <summary>
    /// Platzhalter für noch nicht berechnete Nutzungsinformationen.
    /// </summary>
    public static EpisodeUsageEntry Pending { get; } = new("(wird berechnet)", null, null);

    /// <summary>
    /// Kompatibler Gesamttext für Vorschau, Tests und reine Textausgaben.
    /// </summary>
    public string CurrentText { get; init; }

    /// <summary>
    /// Text der bisherigen Bestandteile, die durch den Plan entfallen oder ersetzt werden.
    /// </summary>
    public string? RemovedText { get; init; }

    /// <summary>
    /// Fachlicher Grund für entfallende bisherige Bestandteile.
    /// </summary>
    public string? RemovedReason { get; init; }

    /// <summary>
    /// Einzelbestandteile des aktuellen Plans inklusive Herkunft. Die UI nutzt diese Liste, um neue
    /// Quellen gegenüber wiederverwendeten Zielspuren sichtbar zu unterscheiden.
    /// </summary>
    public IReadOnlyList<EpisodeUsageItem> CurrentItems { get; init; }

    /// <summary>
    /// Kennzeichnet, dass bisherige Bestandteile entfallen oder ersetzt werden.
    /// </summary>
    public bool HasRemoved => !string.IsNullOrWhiteSpace(RemovedText);

    /// <summary>
    /// Kennzeichnet, dass für entfernte Bestandteile ein fachlicher Grund vorliegt.
    /// </summary>
    public bool HasRemovedReason => !string.IsNullOrWhiteSpace(RemovedReason);

    /// <inheritdoc />
    public bool Equals(EpisodeUsageEntry? other)
    {
        return other is not null
            && string.Equals(CurrentText, other.CurrentText, StringComparison.Ordinal)
            && string.Equals(RemovedText, other.RemovedText, StringComparison.Ordinal)
            && string.Equals(RemovedReason, other.RemovedReason, StringComparison.Ordinal)
            && CurrentItems.SequenceEqual(other.CurrentItems);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CurrentText, StringComparer.Ordinal);
        hash.Add(RemovedText, StringComparer.Ordinal);
        hash.Add(RemovedReason, StringComparer.Ordinal);
        foreach (var item in CurrentItems)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    private static IReadOnlyList<EpisodeUsageItem> SplitCurrentText(string currentText)
    {
        return currentText
            .Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => new EpisodeUsageItem(line, EpisodeUsageItemKind.Neutral))
            .ToList();
    }
}

/// <summary>
/// Herkunft eines aktuell geplanten Bestandteils für die visuelle Hervorhebung im Mux-Review.
/// </summary>
public enum EpisodeUsageItemKind
{
    /// <summary>
    /// Normale, nicht besonders hervorgehobene Information.
    /// </summary>
    Neutral,

    /// <summary>
    /// Bestandteil wird aus einer vorhandenen Ziel-/Archivdatei wiederverwendet.
    /// </summary>
    Existing,

    /// <summary>
    /// Bestandteil kommt neu zu einer bereits vorhandenen Archivdatei hinzu.
    /// </summary>
    Added
}

/// <summary>
/// Ein einzelner aktuell geplanter Track oder Anhang inklusive Herkunft.
/// </summary>
public sealed record EpisodeUsageItem(string Text, EpisodeUsageItemKind Kind)
{
    /// <summary>
    /// Stringwert für XAML-Trigger, damit die View nicht direkt enumtypabhängig sein muss.
    /// </summary>
    public string KindName => Kind.ToString();

    /// <summary>
    /// Kennzeichnet einen neu hinzukommenden Bestandteil.
    /// </summary>
    public bool IsAdded => Kind == EpisodeUsageItemKind.Added;

    /// <summary>
    /// Kennzeichnet einen aus der Zieldatei wiederverwendeten Bestandteil.
    /// </summary>
    public bool IsExisting => Kind == EpisodeUsageItemKind.Existing;
}
