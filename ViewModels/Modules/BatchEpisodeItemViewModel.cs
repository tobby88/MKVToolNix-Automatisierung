using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services.Metadata;
using System.Threading;
using System.Runtime.CompilerServices;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Rohes Scan-Ergebnis, bevor daraus eine bindbare Batch-Zeile gebaut wird.
/// </summary>
internal sealed record BatchScanResult(
    int Index,
    string SourcePath,
    AutoDetectedEpisodeFiles? Detected,
    EpisodeMetadataGuess? LocalGuess,
    EpisodeMetadataResolutionResult? MetadataResolution,
    string? OutputPath,
    string? ErrorMessage);

/// <summary>
/// Repräsentiert eine Zeile im Batch-Bildschirm inklusive Auswahl-, Status- und Review-Zustand.
/// </summary>
internal sealed class BatchEpisodeItemViewModel : EpisodeEditModel
{
    private const string OutputCollisionNotePrefix = "Mehrere getrennt erkannte Quellen zeigen auf dieselbe Ausgabedatei";
    private bool _isSelected;
    private bool _isApplyingSharedMetadataState;
    private bool _isArchiveTargetPath;
    private string? _statusTextOverride;
    private BatchEpisodeStatusKind _statusKind;
    private int _comparisonInputVersion;

    private BatchEpisodeItemViewModel(
        string requestedMainVideoPath,
        string mainVideoPath,
        bool hasPrimaryVideoSource,
        string localSeriesName,
        string localSeasonNumber,
        string localEpisodeNumber,
        string localTitle,
        string seriesName,
        string seasonNumber,
        string episodeNumber,
        IReadOnlyList<string> additionalVideoPaths,
        string? audioDescriptionPath,
        IReadOnlyList<string> subtitlePaths,
        IReadOnlyList<string> attachmentPaths,
        IReadOnlyList<string> relatedEpisodeFilePaths,
        string outputPath,
        string title,
        string metadataStatusText,
        TvdbEpisodeSelection? tvdbSelection,
        bool requiresMetadataReview,
        bool isMetadataReviewApproved,
        BatchEpisodeStatusKind statusKind,
        string planSummaryText,
        EpisodeUsageSummary? usageSummary,
        EpisodeArchiveState archiveState,
        bool isArchiveTargetPath,
        bool isSelected,
        bool requiresManualCheck,
        IReadOnlyList<string> manualCheckFilePaths,
        IReadOnlyList<string> notes)
        : base(
            requestedMainVideoPath,
            mainVideoPath,
            hasPrimaryVideoSource,
            localSeriesName,
            localSeasonNumber,
            localEpisodeNumber,
            localTitle,
            seriesName,
            seasonNumber,
            episodeNumber,
            additionalVideoPaths,
            audioDescriptionPath,
            subtitlePaths,
            attachmentPaths,
            relatedEpisodeFilePaths,
            outputPath,
            title,
            metadataStatusText,
            tvdbSelection,
            requiresMetadataReview,
            isMetadataReviewApproved,
            planSummaryText,
            usageSummary,
            archiveState,
            requiresManualCheck,
            manualCheckFilePaths,
            notes)
    {
        _isArchiveTargetPath = isArchiveTargetPath;
        _statusKind = statusKind;
        _isSelected = isSelected;
    }

    /// <summary>
    /// Kennzeichnet, ob die Episode aktuell für Pflichtprüfungen oder Batch-Ausführung markiert ist.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Liefert den sichtbaren Batch-Status einschließlich optionaler textlicher Override-Fälle.
    /// </summary>
    public string Status => string.IsNullOrWhiteSpace(_statusTextOverride)
        ? EpisodeEditTextBuilder.BuildBatchStatusText(StatusKind)
        : _statusTextOverride;

    /// <summary>
    /// Fachlicher Statusschlüssel der Batch-Zeile. Daraus werden Anzeige, Sortierung und Badge-Farben abgeleitet.
    /// </summary>
    public BatchEpisodeStatusKind StatusKind
    {
        get => _statusKind;
        private set
        {
            if (_statusKind == value)
            {
                return;
            }

            _statusKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusSortKey));
            OnPropertyChanged(nameof(HasErrorStatus));
            OnPropertyChanged(nameof(StatusBadgeBackground));
            OnPropertyChanged(nameof(StatusBadgeBorderBrush));
            OnPropertyChanged(nameof(StatusTooltip));
        }
    }

    /// <summary>
    /// Vereinfacht Fehlerfilter und Fehler-Badge-Logik in der Batch-Liste.
    /// </summary>
    public bool HasErrorStatus => StatusKind == BatchEpisodeStatusKind.Error;

    /// <summary>
    /// Monotone Versionsnummer aller planrelevanten Eingaben dieser Batch-Zeile.
    /// Hintergrundvergleiche verwerfen Ergebnisse, sobald sich diese Nummer während
    /// eines laufenden Refreshs ändert.
    /// </summary>
    internal int ComparisonInputVersion => Volatile.Read(ref _comparisonInputVersion);

    /// <summary>
    /// Sortierschlüssel für die Status-basierte Batch-Sortierung.
    /// </summary>
    public int StatusSortKey => (int)StatusKind;

    public string StatusBadgeBackground => EpisodeUiStyleBuilder.BuildBatchStatusBadgeBackground(StatusKind);

    public string StatusBadgeBorderBrush => EpisodeUiStyleBuilder.BuildBatchStatusBadgeBorderBrush(StatusKind);

    public string StatusTooltip => EpisodeEditTextBuilder.BuildBatchStatusTooltip(StatusKind, Status);

    /// <summary>
    /// Kennzeichnet den Spezialfall, dass am Ziel bereits eine Bibliotheksdatei liegt, die fachlich
    /// als Vergleichs- und Wiederverwendungsbasis behandelt werden muss.
    /// </summary>
    internal bool HasArchiveComparisonTarget => _isArchiveTargetPath && ArchiveState == EpisodeArchiveState.Existing;

    /// <summary>
    /// Spiegelt einen aktuell sichtbaren Batch-Ausgabezielkonflikt in den UI-Hinweisen, ohne
    /// ihn als dauerhafte Detection-Entscheidung in spätere Planläufe einzubrennen.
    /// </summary>
    internal bool SetOutputTargetCollisionState(bool hasCollision)
    {
        var collisionNote = hasCollision
            ? BuildOutputTargetCollisionNote(OutputPath)
            : null;
        var previousCollisionNote = Notes.FirstOrDefault(IsOutputTargetCollisionNote);
        var noteChanged = !string.Equals(previousCollisionNote, collisionNote, StringComparison.OrdinalIgnoreCase);

        UpdateNotes(existingNotes => ReplaceOutputTargetCollisionNotes(existingNotes, collisionNote));
        UpdatePlanNotes(existingNotes => ReplaceOutputTargetCollisionNotes(existingNotes, collisionNote));

        if (noteChanged)
        {
            MarkComparisonInputsChanged();
        }

        if (hasCollision)
        {
            SetStatus(BatchEpisodeStatusKind.Warning);
            return noteChanged;
        }

        if (noteChanged || StatusKind == BatchEpisodeStatusKind.Warning)
        {
            RefreshArchivePresence();
        }

        return noteChanged;
    }

    /// <summary>
    /// Setzt fachlichen Status und optionalen Anzeige-Override konsistent.
    /// </summary>
    public void SetStatus(BatchEpisodeStatusKind statusKind, string? statusText = null)
    {
        var previousStatus = Status;
        StatusKind = statusKind;
        var normalizedOverride = string.IsNullOrWhiteSpace(statusText) ? null : statusText;
        if (_statusTextOverride != normalizedOverride)
        {
            _statusTextOverride = normalizedOverride;
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusTooltip));
            return;
        }

        if (!string.Equals(previousStatus, Status, StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusTooltip));
        }
    }

    /// <summary>
    /// Baut eine normale Batch-Zeile aus einem erfolgreichen Detection- und Metadaten-Ergebnis auf.
    /// </summary>
    public static BatchEpisodeItemViewModel CreateFromDetection(
        string requestedMainVideoPath,
        EpisodeMetadataGuess localGuess,
        AutoDetectedEpisodeFiles detected,
        EpisodeMetadataResolutionResult metadataResolution,
        string outputPath,
        BatchEpisodeStatusKind statusKind,
        bool isSelected,
        bool isArchiveTargetPath = false)
    {
        var outputExists = File.Exists(outputPath);
        var archiveState = outputExists ? EpisodeArchiveState.Existing : EpisodeArchiveState.New;
        var item = new BatchEpisodeItemViewModel(
            requestedMainVideoPath,
            detected.MainVideoPath,
            detected.HasPrimaryVideoSource,
            localGuess.SeriesName,
            localGuess.SeasonNumber,
            localGuess.EpisodeNumber,
            localGuess.EpisodeTitle,
            detected.SeriesName,
            detected.SeasonNumber,
            detected.EpisodeNumber,
            detected.AdditionalVideoPaths,
            detected.AudioDescriptionPath,
            detected.SubtitlePaths,
            detected.AttachmentPaths,
            detected.RelatedFilePaths,
            outputPath,
            detected.SuggestedTitle,
            metadataResolution.StatusText,
            metadataResolution.Selection,
            metadataResolution.RequiresReview,
            DetermineAutomaticMetadataApproval(metadataResolution),
            statusKind,
            BuildPendingPlanSummary(outputExists, isArchiveTargetPath, detected.HasPrimaryVideoSource),
            BuildPendingUsageSummary(outputExists, isArchiveTargetPath, detected.HasPrimaryVideoSource),
            archiveState,
            isArchiveTargetPath,
            isSelected,
            detected.RequiresManualCheck,
            detected.ManualCheckFilePaths,
            detected.Notes);

        if (!detected.HasPrimaryVideoSource && statusKind == BatchEpisodeStatusKind.Warning)
        {
            item.SetStatus(BatchEpisodeStatusKind.Warning, item.BuildMissingPrimaryVideoStatusText());
        }

        return item;
    }

    /// <summary>
    /// Baut einen rein fehlerhaften Listenplatzhalter, wenn bereits während Scan oder Detection
    /// keine sinnvoll bearbeitbare Episode erzeugt werden konnte.
    /// </summary>
    public static BatchEpisodeItemViewModel CreateErrorItem(string requestedMainVideoPath, string errorMessage)
    {
        return new BatchEpisodeItemViewModel(
            requestedMainVideoPath,
            requestedMainVideoPath,
            true,
            Path.GetFileNameWithoutExtension(requestedMainVideoPath),
            "xx",
            "xx",
            Path.GetFileNameWithoutExtension(requestedMainVideoPath),
            Path.GetFileNameWithoutExtension(requestedMainVideoPath),
            "xx",
            "xx",
            [],
            null,
            [],
            [],
            [],
            string.Empty,
            Path.GetFileNameWithoutExtension(requestedMainVideoPath),
            "Keine TVDB-Daten vorhanden.",
            tvdbSelection: null,
            false,
            true,
            BatchEpisodeStatusKind.Error,
            "Keine Plan-Zusammenfassung verfügbar.",
            EpisodeUsageSummary.CreatePending("Fehler", "Keine Plan-Zusammenfassung verfügbar."),
            EpisodeArchiveState.New,
            isArchiveTargetPath: false,
            isSelected: false,
            requiresManualCheck: false,
            manualCheckFilePaths: [],
            notes: [errorMessage]);
    }

    /// <summary>
    /// Übernimmt ein neues Detection-Ergebnis in eine bestehende Batch-Zeile und markiert sie wieder als ausgewählt.
    /// </summary>
    public void ApplyDetection(
        string requestedMainVideoPath,
        EpisodeMetadataGuess localGuess,
        AutoDetectedEpisodeFiles detected,
        EpisodeMetadataResolutionResult metadataResolution,
        string outputPath,
        BatchEpisodeStatusKind statusKind,
        bool isArchiveTargetPath)
    {
        _isArchiveTargetPath = isArchiveTargetPath;
        ApplySharedMetadataState(() => ApplyDetectedEpisodeState(
            requestedMainVideoPath,
            localGuess,
            detected,
            metadataResolution,
            outputPath));
        ApplyArchiveState(statusKind, refreshArchiveState: false);
        IsSelected = true;
    }

    public override void SetAudioDescription(string? path)
    {
        base.SetAudioDescription(path);
        MarkComparisonInputsChanged();
        IsSelected = true;
    }

    public override void SetSubtitles(IEnumerable<string> paths)
    {
        base.SetSubtitles(paths);
        MarkComparisonInputsChanged();
        IsSelected = true;
    }

    public override void SetAttachments(IEnumerable<string> paths)
    {
        base.SetAttachments(paths);
        MarkComparisonInputsChanged();
        IsSelected = true;
    }

    public override void SetOutputPath(string outputPath)
    {
        base.SetOutputPath(outputPath);
        MarkComparisonInputsChanged();
        IsSelected = true;
    }

    public override void SetAutomaticOutputPath(string outputPath)
    {
        base.SetAutomaticOutputPath(outputPath);
        MarkComparisonInputsChanged();
    }

    /// <summary>
    /// Setzt einen manuell gewählten Ausgabepfad zusammen mit seinem Archiv-Kontext.
    /// </summary>
    public void SetOutputPathWithContext(string outputPath, bool isArchiveTargetPath)
    {
        _isArchiveTargetPath = isArchiveTargetPath;
        SetOutputPath(outputPath);
        ApplyArchiveState(refreshArchiveState: false);
    }

    /// <summary>
    /// Setzt einen automatisch bestimmten Ausgabepfad zusammen mit seinem Archiv-Kontext.
    /// Der Archivvergleich wird nur dann neu berechnet, wenn sich der effektive Zielpfad wirklich geändert hat.
    /// </summary>
    public void SetAutomaticOutputPathWithContext(string outputPath, bool isArchiveTargetPath)
    {
        var previousOutputPath = OutputPath;
        _isArchiveTargetPath = isArchiveTargetPath;
        SetAutomaticOutputPath(outputPath);
        if (!string.Equals(previousOutputPath, OutputPath, StringComparison.OrdinalIgnoreCase))
        {
            ApplyArchiveState(refreshArchiveState: false);
        }
    }

    public override void ApproveCurrentReviewTarget()
    {
        base.ApproveCurrentReviewTarget();
        IsSelected = true;
    }

    public override void ApplyTvdbSelection(TvdbEpisodeSelection selection)
    {
        ApplySharedMetadataState(() => base.ApplyTvdbSelection(selection));
        IsSelected = true;
    }

    public override void ApplyLocalMetadataGuess()
    {
        ApplySharedMetadataState(base.ApplyLocalMetadataGuess);
        IsSelected = true;
    }

    public override void ApproveMetadataReview(string statusText)
    {
        base.ApproveMetadataReview(statusText);
        IsSelected = true;
    }

    /// <summary>
    /// Aktualisiert Status und Archivpräsenz, wenn sich die Zieldatei außerhalb der Zeile geändert hat.
    /// </summary>
    public void RefreshArchivePresence(BatchEpisodeStatusKind? statusOverride = null, string? statusText = null)
    {
        if (statusText is null
            && statusOverride is BatchEpisodeStatusKind requestedStatus
            && requestedStatus == StatusKind)
        {
            statusText = GetCurrentStatusTextOverride();
        }

        ApplyArchiveState(statusOverride, preservePlanSummary: true, statusText: statusText);
    }

    /// <summary>
    /// Leitet aus Archivlage, Hauptquelle und offenen Planhinweisen den aktuellen Batch-Status ab.
    /// </summary>
    private void ApplyArchiveState(
        BatchEpisodeStatusKind? statusOverride = null,
        bool preservePlanSummary = false,
        bool refreshArchiveState = true,
        string? statusText = null)
    {
        if (refreshArchiveState)
        {
            RefreshArchiveState();
        }

        var outputExists = ArchiveState == EpisodeArchiveState.Existing;
        var hasArchiveComparisonTarget = HasArchiveComparisonTarget;
        var effectiveStatus = statusOverride ?? ResolveDefaultStatus(hasArchiveComparisonTarget, HasPrimaryVideoSource, HasActionablePlanNotes);
        var effectiveStatusText = string.IsNullOrWhiteSpace(statusText)
            && effectiveStatus == BatchEpisodeStatusKind.Warning
            && !HasPrimaryVideoSource
                ? BuildMissingPrimaryVideoStatusText()
                : statusText;
        SetStatus(effectiveStatus, effectiveStatusText);
        if (!preservePlanSummary)
        {
            SetPlanSummary(BuildPendingPlanSummary(outputExists, _isArchiveTargetPath, HasPrimaryVideoSource));
            SetUsageSummary(BuildPendingUsageSummary(outputExists, _isArchiveTargetPath, HasPrimaryVideoSource));
        }
    }

    /// <summary>
    /// Erzeugt eine erste textuelle Plan-Zusammenfassung, solange der eigentliche Archivvergleich
    /// noch nicht durchgelaufen ist.
    /// </summary>
    private static string BuildPendingPlanSummary(bool outputExists, bool isArchiveTargetPath, bool hasPrimaryVideoSource)
    {
        if (!hasPrimaryVideoSource)
        {
            return outputExists && isArchiveTargetPath
                ? "Es liegt nur Zusatzmaterial ohne frische Hauptvideoquelle vor. Für die Hauptspuren wird die vorhandene Bibliotheks-MKV geprüft."
                : "Es liegt nur Zusatzmaterial ohne frische Hauptvideoquelle vor. Ohne vorhandene Bibliotheks-MKV kann derzeit kein vollständiger Mux geplant werden.";
        }

        if (outputExists && isArchiveTargetPath)
        {
            return "Am Ziel liegt bereits eine MKV in der Serienbibliothek. Details wählen für den genauen Vergleich.";
        }

        if (outputExists)
        {
            return "Am Ziel liegt bereits eine MKV. Sie wird beim Mux überschrieben.";
        }

        return "Am Ziel liegt noch keine MKV. Neue Datei wird erstellt.";
    }

    /// <summary>
    /// Erzeugt die analoge Pending-Zusammenfassung für die sichtbare Verwendungsübersicht.
    /// </summary>
    private static EpisodeUsageSummary BuildPendingUsageSummary(bool outputExists, bool isArchiveTargetPath, bool hasPrimaryVideoSource)
    {
        if (!hasPrimaryVideoSource)
        {
            return outputExists && isArchiveTargetPath
                ? EpisodeUsageSummary.CreatePending(
                    "Nur Zusatzmaterial erkannt",
                    "Vorhandene Bibliotheks-MKV wird als Hauptquelle geprüft")
                : EpisodeUsageSummary.CreatePending(
                    "Nur Zusatzmaterial erkannt",
                    "Ohne vorhandene Bibliotheks-MKV aktuell nicht ausführbar");
        }

        if (outputExists && isArchiveTargetPath)
        {
            return EpisodeUsageSummary.CreatePending(
                "Ziel bereits vorhanden",
                "Vergleich wird berechnet");
        }

        if (outputExists)
        {
            return EpisodeUsageSummary.CreatePending(
                "Zieldatei bereits vorhanden",
                "Vorhandene Datei wird überschrieben");
        }

        return EpisodeUsageSummary.CreatePending(
            "Ziel noch frei",
            "Neue MKV wird erstellt");
    }

    /// <summary>
    /// Leitet den Standardstatus aus Archivkontext, Hauptquelle und offenen fachlichen Hinweisen ab.
    /// </summary>
    private static BatchEpisodeStatusKind ResolveDefaultStatus(
        bool hasArchiveComparisonTarget,
        bool hasPrimaryVideoSource,
        bool hasActionablePlanNotes)
    {
        if (hasArchiveComparisonTarget)
        {
            return BatchEpisodeStatusKind.ComparisonPending;
        }

        if (hasActionablePlanNotes)
        {
            return BatchEpisodeStatusKind.ReviewPending;
        }

        return hasPrimaryVideoSource
            ? BatchEpisodeStatusKind.Ready
            : BatchEpisodeStatusKind.Warning;
    }

    /// <summary>
    /// Formuliert den sichtbaren Warntext für Zusatzmaterial ohne frische Hauptquelle.
    /// </summary>
    private string BuildMissingPrimaryVideoStatusText()
    {
        return HasArchiveComparisonTarget
            ? EpisodeEditTextBuilder.BuildBatchStatusText(BatchEpisodeStatusKind.ComparisonPending)
            : "Warnung (nur Zusatzmaterial ohne vorhandene Bibliotheks-MKV)";
    }

    /// <summary>
    /// Liefert den aktuell sichtbaren Status-Override zurück, falls einer gesetzt ist.
    /// </summary>
    private string? GetCurrentStatusTextOverride()
    {
        var defaultStatusText = EpisodeEditTextBuilder.BuildBatchStatusText(StatusKind);
        return string.Equals(Status, defaultStatusText, StringComparison.Ordinal)
            ? null
            : Status;
    }

    /// <summary>
    /// Reagiert auf manuelle Metadatenänderungen und markiert diese als bewusste lokale Korrektur.
    /// Der <see cref="CallerMemberNameAttribute"/> ist hier entscheidend: Ohne ihn würden reine
    /// Auswahl-Toggles als <c>null</c>-PropertyChange hochlaufen und unnötige Plan-Refreshes auslösen.
    /// </summary>
    protected override void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (_isApplyingSharedMetadataState)
        {
            return;
        }

        if (propertyName is nameof(SeriesName) or nameof(SeasonNumber) or nameof(EpisodeNumber) or nameof(Title))
        {
            HandleManualMetadataOverride();
        }
    }

    /// <summary>
    /// Entfernt eine vorhandene TVDB-Bindung, sobald sichtbare Kerndaten der Episode manuell überschrieben wurden.
    /// </summary>
    private void HandleManualMetadataOverride()
    {
        if (!string.IsNullOrWhiteSpace(MetadataStatusText) || RequiresMetadataReview)
        {
            ClearTvdbSelection();
            ApproveMetadataReview("Metadaten manuell angepasst.");
        }
    }

    /// <summary>
    /// Bündelt Metadatenänderungen, bei denen keine rekursive Reaktion auf die eigenen Property-Changes
    /// ausgelöst werden soll.
    /// </summary>
    private void ApplySharedMetadataState(Action applyAction)
    {
        _isApplyingSharedMetadataState = true;
        try
        {
            applyAction();
            // Metadatenwechsel ändern Zielpfad und Archivvergleich. Alte Planhinweise
            // wie "Archiv prüfen" dürfen deshalb nicht sichtbar bleiben, bis der
            // nächste Vergleich neue, tatsächlich passende Hinweise berechnet.
            SetPlanNotes([]);
            MarkComparisonInputsChanged();
        }
        finally
        {
            _isApplyingSharedMetadataState = false;
        }
    }

    /// <summary>
    /// Hebt die Vergleichsversion an, sobald sich planrelevante Eingaben ändern.
    /// Bereits laufende Vergleiche erkennen darüber veraltete Ergebnisse und
    /// schreiben keine alten Archivhinweise mehr zurück.
    /// </summary>
    private void MarkComparisonInputsChanged()
    {
        Interlocked.Increment(ref _comparisonInputVersion);
    }

    private static bool IsOutputTargetCollisionNote(string? note)
    {
        return !string.IsNullOrWhiteSpace(note)
            && note.Contains(OutputCollisionNotePrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ReplaceOutputTargetCollisionNotes(
        IReadOnlyList<string> existingNotes,
        string? replacementNote)
    {
        var materialized = existingNotes
            .Where(note => !IsOutputTargetCollisionNote(note))
            .ToList();
        if (!string.IsNullOrWhiteSpace(replacementNote))
        {
            materialized.Add(replacementNote);
        }

        return materialized;
    }

    private static string BuildOutputTargetCollisionNote(string outputPath)
    {
        return $"{OutputCollisionNotePrefix} '{Path.GetFileName(outputPath)}'. Bitte Episodencode und Ausgabeziel prüfen.";
    }

}
