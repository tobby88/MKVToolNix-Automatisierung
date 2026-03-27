namespace MkvToolnixAutomatisierung.ViewModels.Modules;

public enum EpisodeReviewState
{
    NoneNeeded = 0,
    Approved = 1,
    ManualCheckPending = 2,
    MetadataReviewPending = 3,
    ManualAndMetadataPending = 4
}

public enum ManualCheckBadgeState
{
    Approved = 0,
    Pending = 1
}

public enum MetadataBadgeState
{
    Open = 0,
    Approved = 1,
    Pending = 2
}

public enum OutputTargetBadgeState
{
    Open = 0,
    NewForLibrary = 1,
    InLibrary = 2,
    CustomTarget = 3
}

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
