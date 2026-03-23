namespace MkvToolnixAutomatisierung.ViewModels.Modules;

internal static class EpisodeEditTextBuilder
{
    public static string BuildManualCheckText(bool requiresManualCheck, bool isManualCheckApproved)
    {
        if (!requiresManualCheck)
        {
            return string.Empty;
        }

        return isManualCheckApproved
            ? "Die aktuell ausgewählte Quelle wurde bereits geprüft und freigegeben."
            : "Die aktuell ausgewählte Quelle ist prüfpflichtig. Bitte vor dem Muxen kurz prüfen und freigeben.";
    }

    public static EpisodeReviewState GetReviewState(
        bool requiresManualCheck,
        bool isManualCheckApproved,
        bool requiresMetadataReview,
        bool isMetadataReviewApproved)
    {
        var hasPendingManualCheck = requiresManualCheck && !isManualCheckApproved;
        var hasPendingMetadataReview = requiresMetadataReview && !isMetadataReviewApproved;

        if (hasPendingManualCheck && hasPendingMetadataReview)
        {
            return EpisodeReviewState.ManualAndMetadataPending;
        }

        if (hasPendingManualCheck)
        {
            return EpisodeReviewState.ManualCheckPending;
        }

        if (hasPendingMetadataReview)
        {
            return EpisodeReviewState.MetadataReviewPending;
        }

        if (requiresManualCheck || requiresMetadataReview)
        {
            return EpisodeReviewState.Approved;
        }

        return EpisodeReviewState.NoneNeeded;
    }

    public static string BuildReviewHint(EpisodeReviewState reviewState)
    {
        return reviewState switch
        {
            EpisodeReviewState.Approved => "Freigegeben",
            EpisodeReviewState.ManualCheckPending => "Quelle prüfen",
            EpisodeReviewState.MetadataReviewPending => "TVDB prüfen",
            EpisodeReviewState.ManualAndMetadataPending => "Quelle + TVDB prüfen",
            _ => "Keine nötig"
        };
    }

    public static string BuildManualCheckBadgeText(ManualCheckBadgeState badgeState)
    {
        return badgeState switch
        {
            ManualCheckBadgeState.Pending => "Quelle prüfen",
            _ => "Quelle ok"
        };
    }

    public static string BuildMetadataBadgeText(MetadataBadgeState badgeState)
    {
        return badgeState switch
        {
            MetadataBadgeState.Pending => "TVDB prüfen",
            MetadataBadgeState.Approved => "TVDB ok",
            _ => "TVDB offen"
        };
    }

    public static string BuildOutputTargetBadgeText(OutputTargetBadgeState badgeState)
    {
        return badgeState switch
        {
            OutputTargetBadgeState.InLibrary => "In Bibliothek",
            OutputTargetBadgeState.NewForLibrary => "Neu für Bibliothek",
            _ => "Bibliothek offen"
        };
    }

    public static string BuildBatchStatusText(BatchEpisodeStatusKind statusKind)
    {
        return statusKind switch
        {
            BatchEpisodeStatusKind.Warning => "Warnung",
            BatchEpisodeStatusKind.Running => "Läuft",
            BatchEpisodeStatusKind.ComparisonPending => "Vergleich offen",
            BatchEpisodeStatusKind.Ready => "Bereit",
            BatchEpisodeStatusKind.UpToDate => "Ziel aktuell",
            BatchEpisodeStatusKind.Success => "Erfolgreich",
            _ => "Fehler"
        };
    }

    public static string BuildNotesDisplayText(IReadOnlyList<string> notes)
    {
        return notes.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, notes.Select(note => "- " + note));
    }

    public static string FormatPaths(IEnumerable<string> paths)
    {
        var list = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return list.Count == 0
            ? "(keine)"
            : string.Join(Environment.NewLine, list);
    }
}
