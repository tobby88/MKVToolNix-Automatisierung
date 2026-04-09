namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Zusammengefasster Prüfstatus einer Episode aus manueller Quellenprüfung und TVDB-Review.
/// </summary>
internal enum EpisodeReviewState
{
    NoneNeeded = 0,
    Approved = 1,
    ManualCheckPending = 2,
    MetadataReviewPending = 3,
    ManualAndMetadataPending = 4
}

/// <summary>
/// Farb-/Textzustand des Badges für die Quellenprüfung.
/// </summary>
internal enum ManualCheckBadgeState
{
    Approved = 0,
    Pending = 1
}

/// <summary>
/// Farb-/Textzustand des Badges für den TVDB-Status.
/// </summary>
internal enum MetadataBadgeState
{
    Open = 0,
    Approved = 1,
    Pending = 2
}

/// <summary>
/// Bestimmt fachliche Badge-Zustände unabhängig von konkreten Farben oder Texten.
/// </summary>
internal static class EpisodeUiStateResolver
{
    /// <summary>
    /// Leitet den sichtbaren TVDB-Badge-Zustand aus Reviewpflicht und tatsächlicher Freigabe ab.
    /// </summary>
    /// <param name="hasPendingMetadataReview">Gibt an, ob noch eine explizite TVDB-Prüfung offen ist.</param>
    /// <param name="isMetadataReviewApproved">Gibt an, ob die aktuelle Zuordnung wirklich freigegeben wurde.</param>
    /// <returns>Passender Badge-Zustand für die Einzelansicht.</returns>
    public static MetadataBadgeState ResolveMetadataBadgeState(
        bool hasPendingMetadataReview,
        bool isMetadataReviewApproved)
    {
        if (hasPendingMetadataReview)
        {
            return MetadataBadgeState.Pending;
        }

        return isMetadataReviewApproved
            ? MetadataBadgeState.Approved
            : MetadataBadgeState.Open;
    }
}

/// <summary>
/// Visuelle Einordnung des Ausgabepfads relativ zur Bibliothek.
/// </summary>
internal enum OutputTargetBadgeState
{
    Open = 0,
    NewForLibrary = 1,
    InLibrary = 2,
    CustomTarget = 3
}

/// <summary>
/// Sortier- und Darstellungszustände einzelner Batch-Zeilen.
/// </summary>
internal enum BatchEpisodeStatusKind
{
    Error = 0,
    Warning = 1,
    Running = 2,
    Cancelled = 3,
    ComparisonPending = 4,
    ReviewPending = 5,
    Ready = 6,
    UpToDate = 7,
    Success = 8
}
