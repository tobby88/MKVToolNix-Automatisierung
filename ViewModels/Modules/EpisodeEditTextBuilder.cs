namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Baut die sichtbaren Status- und Hilfetexte für Episode- und Batch-UI konsistent an einer Stelle.
/// </summary>
internal static class EpisodeEditTextBuilder
{
    private static readonly string[] MultipartPlanReviewMarkers =
    [
        "doppelfolge",
        "mehrfachfolge",
        "gesplittete episodenvariante"
    ];

    private static readonly string[] ArchivePlanReviewMarkers =
    [
        "archivtreffer",
        "dateilaufzeit widersprechen",
        "laufzeit der quelle"
    ];

    private static readonly string[] OutputTargetPlanReviewMarkers =
    [
        "dieselbe ausgabedatei",
        "episodencode und ausgabeziel"
    ];

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

    public static string BuildReviewHint(EpisodeReviewState reviewState, string? planReviewLabel = null)
    {
        if (!string.IsNullOrWhiteSpace(planReviewLabel))
        {
            return reviewState switch
            {
                EpisodeReviewState.ManualCheckPending => $"Quelle + {planReviewLabel}",
                EpisodeReviewState.MetadataReviewPending => $"{planReviewLabel} + TVDB",
                EpisodeReviewState.ManualAndMetadataPending => $"Quelle + {planReviewLabel} + TVDB",
                _ => planReviewLabel
            };
        }

        return reviewState switch
        {
            EpisodeReviewState.Approved => "Freigegeben",
            EpisodeReviewState.ManualCheckPending => "Quelle prüfen",
            EpisodeReviewState.MetadataReviewPending => "TVDB prüfen",
            EpisodeReviewState.ManualAndMetadataPending => "Quelle + TVDB prüfen",
            _ => "Keine nötig"
        };
    }

    public static string BuildReviewHintTooltip(
        EpisodeReviewState reviewState,
        string? planReviewLabel = null,
        string? primaryPlanReviewNote = null)
    {
        if (!string.IsNullOrWhiteSpace(planReviewLabel))
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(primaryPlanReviewNote))
            {
                parts.Add(primaryPlanReviewNote.Trim());
            }

            parts.Add("Dieser Eintrag sollte vor dem Muxen fachlich noch geprüft werden.");

            if (reviewState is EpisodeReviewState.ManualCheckPending or EpisodeReviewState.ManualAndMetadataPending)
            {
                parts.Add("Zusätzlich sollte die erkannte Quelle noch freigegeben werden.");
            }

            if (reviewState is EpisodeReviewState.MetadataReviewPending or EpisodeReviewState.ManualAndMetadataPending)
            {
                parts.Add("Danach sollte bei Bedarf noch die TVDB-Zuordnung geprüft werden.");
            }

            return string.Join(Environment.NewLine, parts);
        }

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
            BatchEpisodeStatusKind.Cancelled => "Abgebrochen",
            BatchEpisodeStatusKind.ComparisonPending => "Vergleich offen",
            BatchEpisodeStatusKind.ReviewPending => "Prüfung offen",
            BatchEpisodeStatusKind.Ready => "Bereit",
            BatchEpisodeStatusKind.UpToDate => "Ziel aktuell",
            BatchEpisodeStatusKind.Success => "Erfolgreich",
            _ => "Fehler"
        };
    }

    public static string BuildSingleExecutionStatusText(SingleEpisodeExecutionStatusKind statusKind)
    {
        return statusKind switch
        {
            SingleEpisodeExecutionStatusKind.Warning => "Warnung",
            SingleEpisodeExecutionStatusKind.Running => "Läuft",
            SingleEpisodeExecutionStatusKind.Cancelled => "Abgebrochen",
            SingleEpisodeExecutionStatusKind.ComparisonPending => "Vergleich offen",
            SingleEpisodeExecutionStatusKind.Ready => "Bereit",
            SingleEpisodeExecutionStatusKind.UpToDate => "Ziel aktuell",
            SingleEpisodeExecutionStatusKind.Success => "Erfolgreich",
            _ => "Fehler"
        };
    }

    public static string BuildSingleExecutionStatusTooltip(SingleEpisodeExecutionStatusKind statusKind, string statusText)
    {
        var explanation = statusKind switch
        {
            SingleEpisodeExecutionStatusKind.Warning => "Der Einzellauf wurde verarbeitet, aber es gab Warnungen oder eine benötigte Freigabe fehlt.",
            SingleEpisodeExecutionStatusKind.Running => "Im Einzelmodus läuft gerade Erkennung, Vorschau, Kopieren oder Muxing.",
            SingleEpisodeExecutionStatusKind.Cancelled => "Die letzte Einzelaktion wurde abgebrochen.",
            SingleEpisodeExecutionStatusKind.ComparisonPending => "Die Erkennung ist abgeschlossen, aber der Zielvergleich konnte noch nicht belastbar berechnet werden.",
            SingleEpisodeExecutionStatusKind.Ready => "Der Einzelmodus ist bereit für Vorschau oder Mux.",
            SingleEpisodeExecutionStatusKind.UpToDate => "Die Zieldatei ist bereits vollständig vorhanden. Ein neuer Mux ist normalerweise nicht nötig.",
            SingleEpisodeExecutionStatusKind.Success => "Der letzte Einzellauf wurde erfolgreich verarbeitet.",
            _ => "Bei der letzten Einzelaktion ist ein Fehler aufgetreten."
        };

        var normalizedStatus = string.IsNullOrWhiteSpace(statusText)
            ? string.Empty
            : statusText.Trim();

        return string.IsNullOrWhiteSpace(normalizedStatus)
            || string.Equals(normalizedStatus, BuildSingleExecutionStatusText(statusKind), StringComparison.Ordinal)
            ? explanation
            : $"{explanation}{Environment.NewLine}Status: {normalizedStatus}";
    }

    public static string BuildBatchStatusTooltip(BatchEpisodeStatusKind statusKind, string statusText)
    {
        var explanation = statusKind switch
        {
            BatchEpisodeStatusKind.Warning => "Der Eintrag wurde verarbeitet, aber es gab Warnungen. Details oder Protokoll prüfen.",
            BatchEpisodeStatusKind.Running => "Dieser Eintrag wird gerade geplant, kopiert oder gemuxt.",
            BatchEpisodeStatusKind.Cancelled => "Dieser Eintrag wurde durch Benutzerabbruch nicht vollständig verarbeitet und kann erneut gestartet werden.",
            BatchEpisodeStatusKind.ComparisonPending => "Für eine bereits vorhandene Bibliotheksdatei fehlt noch der aktuelle Vergleich.",
            BatchEpisodeStatusKind.ReviewPending => "Der Vergleich ist abgeschlossen, aber vor dem Muxen sollte noch ein fachlicher Hinweis geprüft werden.",
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

    public static bool IsActionablePlanReviewNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return false;
        }

        return ContainsAny(note, MultipartPlanReviewMarkers)
            || ContainsAny(note, ArchivePlanReviewMarkers)
            || ContainsAny(note, OutputTargetPlanReviewMarkers);
    }

    public static string? GetPrimaryActionablePlanReviewNote(IEnumerable<string> notes)
    {
        return notes
            .Where(IsActionablePlanReviewNote)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetPlanReviewPriority)
            .ThenBy(note => note, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static string? BuildPlanReviewLabel(IEnumerable<string> notes)
    {
        var primaryNote = GetPrimaryActionablePlanReviewNote(notes);
        if (string.IsNullOrWhiteSpace(primaryNote))
        {
            return null;
        }

        if (ContainsAny(primaryNote, MultipartPlanReviewMarkers))
        {
            return "Mehrfachfolge prüfen";
        }

        if (ContainsAny(primaryNote, ArchivePlanReviewMarkers))
        {
            return "Archiv prüfen";
        }

        if (ContainsAny(primaryNote, OutputTargetPlanReviewMarkers))
        {
            return "Ziel prüfen";
        }

        return "Hinweis prüfen";
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

    private static int GetPlanReviewPriority(string note)
    {
        if (ContainsAny(note, MultipartPlanReviewMarkers))
        {
            return 0;
        }

        if (ContainsAny(note, ArchivePlanReviewMarkers))
        {
            return 1;
        }

        if (ContainsAny(note, OutputTargetPlanReviewMarkers))
        {
            return 2;
        }

        return 3;
    }

    private static bool ContainsAny(string value, IEnumerable<string> needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
