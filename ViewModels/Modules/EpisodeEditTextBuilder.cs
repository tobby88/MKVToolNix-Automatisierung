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

    public static string BuildReviewHint(
        bool requiresManualCheck,
        bool isManualCheckApproved,
        bool requiresMetadataReview,
        bool isMetadataReviewApproved)
    {
        var pendingChecks = new List<string>();

        if (requiresManualCheck && !isManualCheckApproved)
        {
            pendingChecks.Add("Quelle");
        }

        if (requiresMetadataReview && !isMetadataReviewApproved)
        {
            pendingChecks.Add("TVDB");
        }

        if (pendingChecks.Count > 0)
        {
            return string.Join(" + ", pendingChecks) + " prüfen";
        }

        if (requiresManualCheck || requiresMetadataReview)
        {
            return "Freigegeben";
        }

        return "Keine nötig";
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
