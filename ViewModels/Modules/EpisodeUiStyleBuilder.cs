namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Liefert die zentral definierten Badge-Farben, damit Statusdarstellung nicht in XAML verteilt werden muss.
/// </summary>
internal static class EpisodeUiStyleBuilder
{
    public static string BuildArchiveBadgeBackground(EpisodeArchiveState archiveState)
    {
        return archiveState == EpisodeArchiveState.Existing ? "#E8F3FF" : "#EEF6E8";
    }

    public static string BuildArchiveBadgeBorderBrush(EpisodeArchiveState archiveState)
    {
        return archiveState == EpisodeArchiveState.Existing ? "#8CB4D8" : "#88B06E";
    }

    public static string BuildReviewBadgeBackground(EpisodeReviewState reviewState)
    {
        return reviewState switch
        {
            EpisodeReviewState.Approved or EpisodeReviewState.NoneNeeded => "#EEF6E8",
            EpisodeReviewState.ManualCheckPending or EpisodeReviewState.MetadataReviewPending or EpisodeReviewState.ManualAndMetadataPending => "#FFF4D6",
            _ => "#F3F6FA"
        };
    }

    public static string BuildReviewBadgeBorderBrush(EpisodeReviewState reviewState)
    {
        return reviewState switch
        {
            EpisodeReviewState.Approved or EpisodeReviewState.NoneNeeded => "#88B06E",
            EpisodeReviewState.ManualCheckPending or EpisodeReviewState.MetadataReviewPending or EpisodeReviewState.ManualAndMetadataPending => "#D8B46A",
            _ => "#C7D1DC"
        };
    }

    public static string BuildBatchStatusBadgeBackground(BatchEpisodeStatusKind statusKind)
    {
        return statusKind switch
        {
            BatchEpisodeStatusKind.Error => "#FCE8E8",
            BatchEpisodeStatusKind.Warning or BatchEpisodeStatusKind.Running or BatchEpisodeStatusKind.ReviewPending => "#FFF4D6",
            BatchEpisodeStatusKind.Cancelled => "#F3F6FA",
            BatchEpisodeStatusKind.ComparisonPending => "#E8F3FF",
            BatchEpisodeStatusKind.Ready or BatchEpisodeStatusKind.UpToDate or BatchEpisodeStatusKind.Success => "#EEF6E8",
            _ => "#F3F6FA"
        };
    }

    public static string BuildBatchStatusBadgeBorderBrush(BatchEpisodeStatusKind statusKind)
    {
        return statusKind switch
        {
            BatchEpisodeStatusKind.Error => "#D28A8A",
            BatchEpisodeStatusKind.Warning or BatchEpisodeStatusKind.Running or BatchEpisodeStatusKind.ReviewPending => "#D8B46A",
            BatchEpisodeStatusKind.Cancelled => "#C7D1DC",
            BatchEpisodeStatusKind.ComparisonPending => "#8CB4D8",
            BatchEpisodeStatusKind.Ready or BatchEpisodeStatusKind.UpToDate or BatchEpisodeStatusKind.Success => "#88B06E",
            _ => "#C7D1DC"
        };
    }

    public static string BuildManualCheckBadgeBackground(ManualCheckBadgeState badgeState)
    {
        return badgeState == ManualCheckBadgeState.Pending ? "#FFF4D6" : "#EEF6E8";
    }

    public static string BuildManualCheckBadgeBorderBrush(ManualCheckBadgeState badgeState)
    {
        return badgeState == ManualCheckBadgeState.Pending ? "#D8B46A" : "#88B06E";
    }

    public static string BuildMetadataBadgeBackground(MetadataBadgeState badgeState)
    {
        return badgeState switch
        {
            MetadataBadgeState.Pending => "#FFF4D6",
            MetadataBadgeState.Open => "#E8F3FF",
            _ => "#EEF6E8"
        };
    }

    public static string BuildMetadataBadgeBorderBrush(MetadataBadgeState badgeState)
    {
        return badgeState switch
        {
            MetadataBadgeState.Pending => "#D8B46A",
            MetadataBadgeState.Open => "#8CB4D8",
            _ => "#88B06E"
        };
    }

    public static string BuildOutputTargetBadgeBackground(OutputTargetBadgeState badgeState)
    {
        return badgeState switch
        {
            OutputTargetBadgeState.InLibrary => "#EEF6E8",
            OutputTargetBadgeState.CustomTarget => "#F3F6FA",
            _ => "#E8F3FF"
        };
    }

    public static string BuildOutputTargetBadgeBorderBrush(OutputTargetBadgeState badgeState)
    {
        return badgeState switch
        {
            OutputTargetBadgeState.InLibrary => "#88B06E",
            OutputTargetBadgeState.CustomTarget => "#C7D1DC",
            _ => "#8CB4D8"
        };
    }

    public static string BuildSingleExecutionStatusBadgeBackground(SingleEpisodeExecutionStatusKind statusKind)
    {
        return statusKind switch
        {
            SingleEpisodeExecutionStatusKind.Error => "#FCE8E8",
            SingleEpisodeExecutionStatusKind.Warning or SingleEpisodeExecutionStatusKind.Running => "#FFF4D6",
            SingleEpisodeExecutionStatusKind.Cancelled => "#F3F6FA",
            SingleEpisodeExecutionStatusKind.ComparisonPending => "#E8F3FF",
            SingleEpisodeExecutionStatusKind.Ready or SingleEpisodeExecutionStatusKind.UpToDate or SingleEpisodeExecutionStatusKind.Success => "#EEF6E8",
            _ => "#F3F6FA"
        };
    }

    public static string BuildSingleExecutionStatusBadgeBorderBrush(SingleEpisodeExecutionStatusKind statusKind)
    {
        return statusKind switch
        {
            SingleEpisodeExecutionStatusKind.Error => "#D28A8A",
            SingleEpisodeExecutionStatusKind.Warning or SingleEpisodeExecutionStatusKind.Running => "#D8B46A",
            SingleEpisodeExecutionStatusKind.Cancelled => "#C7D1DC",
            SingleEpisodeExecutionStatusKind.ComparisonPending => "#8CB4D8",
            SingleEpisodeExecutionStatusKind.Ready or SingleEpisodeExecutionStatusKind.UpToDate or SingleEpisodeExecutionStatusKind.Success => "#88B06E",
            _ => "#C7D1DC"
        };
    }
}
