namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Baut die sichtbaren Status- und Hilfetexte für Episode- und Batch-UI konsistent an einer Stelle.
/// </summary>
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

    public static string BuildReviewHintTooltip(EpisodeReviewState reviewState)
    {
        return reviewState switch
        {
            EpisodeReviewState.Approved => "Alle nötigen Quellen- und Metadatenprüfungen sind bereits erledigt.",
            EpisodeReviewState.ManualCheckPending => "Die erkannte Quelle sollte vor dem Muxen geprüft und freigegeben werden.",
            EpisodeReviewState.MetadataReviewPending => "Die TVDB-Zuordnung sollte vor dem Muxen geprüft und freigegeben werden.",
            EpisodeReviewState.ManualAndMetadataPending => "Sowohl die Quelle als auch die TVDB-Zuordnung sollten vor dem Muxen geprüft werden.",
            _ => "Für diese Episode sind aktuell keine zusätzlichen Prüfungen nötig."
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

    public static string BuildManualCheckBadgeTooltip(ManualCheckBadgeState badgeState)
    {
        return badgeState switch
        {
            ManualCheckBadgeState.Pending => "Die Quelle ist prüfpflichtig. Öffne die Quellenprüfung, bevor du muxst.",
            _ => "Die aktuelle Quelle ist freigegeben oder benötigt keine zusätzliche Prüfung."
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

    public static string BuildMetadataBadgeTooltip(MetadataBadgeState badgeState)
    {
        return badgeState switch
        {
            MetadataBadgeState.Pending => "Die TVDB-Zuordnung sollte geprüft und freigegeben werden.",
            MetadataBadgeState.Approved => "Die TVDB-Zuordnung ist freigegeben oder automatisch sicher erkannt.",
            _ => "Es liegt noch keine freigegebene TVDB-Zuordnung vor. Bei Bedarf kannst du den TVDB-Dialog öffnen."
        };
    }

    public static string BuildOutputTargetBadgeText(OutputTargetBadgeState badgeState)
    {
        return badgeState switch
        {
            OutputTargetBadgeState.InLibrary => "In Bibliothek",
            OutputTargetBadgeState.NewForLibrary => "Neu für Bibliothek",
            OutputTargetBadgeState.CustomTarget => "Eigener Pfad",
            _ => "Bibliothek offen"
        };
    }

    public static string BuildOutputTargetBadgeTooltip(OutputTargetBadgeState badgeState)
    {
        return badgeState switch
        {
            OutputTargetBadgeState.InLibrary => "Die Ausgabe zeigt auf eine bereits vorhandene Datei in der Serienbibliothek.",
            OutputTargetBadgeState.NewForLibrary => "Die Ausgabe wird als neue Datei in der Serienbibliothek angelegt.",
            OutputTargetBadgeState.CustomTarget => "Die Ausgabe zeigt auf einen Pfad außerhalb der Serienbibliothek.",
            _ => "Das Ausgabeziel ist noch nicht vollständig festgelegt."
        };
    }

    public static string BuildArchiveStateTooltip(EpisodeArchiveState archiveState)
    {
        return archiveState == EpisodeArchiveState.Existing
            ? "Für diese Episode existiert bereits eine Datei in der Serienbibliothek. Ein Vergleich entscheidet, ob etwas ergänzt oder ersetzt werden muss."
            : "Für diese Episode liegt in der Serienbibliothek noch kein Ziel vor. Es wird eine neue MKV angelegt.";
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

    public static string BuildBatchStatusTooltip(BatchEpisodeStatusKind statusKind, string statusText)
    {
        var explanation = statusKind switch
        {
            BatchEpisodeStatusKind.Warning => "Der Eintrag wurde verarbeitet, aber es gab Warnungen. Details oder Protokoll prüfen.",
            BatchEpisodeStatusKind.Running => "Dieser Eintrag wird gerade geplant, kopiert oder gemuxt.",
            BatchEpisodeStatusKind.ComparisonPending => "Für eine bereits vorhandene Bibliotheksdatei fehlt noch der aktuelle Vergleich.",
            BatchEpisodeStatusKind.Ready => "Der Eintrag ist bereit für den Batch-Lauf.",
            BatchEpisodeStatusKind.UpToDate => "Die Zieldatei ist bereits vollständig vorhanden. Ein neuer Lauf ist normalerweise nicht nötig.",
            BatchEpisodeStatusKind.Success => "Der Eintrag wurde erfolgreich verarbeitet.",
            _ => "Bei diesem Eintrag ist ein Fehler aufgetreten."
        };

        var normalizedStatus = string.IsNullOrWhiteSpace(statusText)
            ? string.Empty
            : statusText.Trim();

        return string.IsNullOrWhiteSpace(normalizedStatus)
            || string.Equals(normalizedStatus, BuildBatchStatusText(statusKind), StringComparison.Ordinal)
            ? explanation
            : $"{explanation}{Environment.NewLine}Status: {normalizedStatus}";
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
