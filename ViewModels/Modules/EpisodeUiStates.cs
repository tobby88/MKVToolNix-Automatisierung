namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Zusammengefasster Prüfstatus einer Episode aus manueller Quellenprüfung und TVDB-Review.
/// </summary>
public enum EpisodeReviewState
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
public enum ManualCheckBadgeState
{
    Approved = 0,
    Pending = 1
}

/// <summary>
/// Farb-/Textzustand des Badges für den TVDB-Status.
/// </summary>
public enum MetadataBadgeState
{
    Open = 0,
    Approved = 1,
    Pending = 2
}

/// <summary>
/// Visuelle Einordnung des Ausgabepfads relativ zur Bibliothek.
/// </summary>
public enum OutputTargetBadgeState
{
    Open = 0,
    NewForLibrary = 1,
    InLibrary = 2,
    CustomTarget = 3
}

/// <summary>
/// Sortier- und Darstellungszustände einzelner Batch-Zeilen.
/// </summary>
public enum BatchEpisodeStatusKind
{
    Error = 0,
    Warning = 1,
    Running = 2,
    ComparisonPending = 3,
    Ready = 4,
    UpToDate = 5,
    Success = 6
}
