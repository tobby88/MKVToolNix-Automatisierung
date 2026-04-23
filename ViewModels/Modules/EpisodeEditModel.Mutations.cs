using System.ComponentModel;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

// Dieser Partial enthält alle zustandsändernden Operationen der Episoden-Basisklasse.
internal partial class EpisodeEditModel
{
    public void AddRequestedSource(string requestedSourcePath)
    {
        if (_requestedSourcePaths.Contains(requestedSourcePath, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _requestedSourcePaths.Add(requestedSourcePath);
        _requestedSourcePaths = _requestedSourcePaths
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_notes.All(note => !note.Contains("Weitere gefundene Quelldateien", StringComparison.OrdinalIgnoreCase)))
        {
            _notes.Add("Weitere gefundene Quelldateien wurden dieser Episode automatisch zugeordnet.");
            OnPropertyChanged(nameof(Notes));
            OnPropertyChanged(nameof(HasNotes));
            OnPropertyChanged(nameof(NotesDisplayText));
        }

        OnPropertyChanged(nameof(RequestedSourcePaths));
        OnPropertyChanged(nameof(RequestedSourcesDisplayText));
    }

    protected void ApplyDetectedEpisodeState(
        string requestedMainVideoPath,
        EpisodeMetadataGuess localGuess,
        AutoDetectedEpisodeFiles detected,
        EpisodeMetadataResolutionResult metadataResolution,
        string outputPath,
        string? titleOverride = null)
    {
        SetRequestedMainVideoPath(requestedMainVideoPath);
        SetDetectionSeedPath(requestedMainVideoPath);
        SetRequestedSourcePaths([requestedMainVideoPath]);
        SetLocalMetadataGuess(localGuess);
        HasPrimaryVideoSource = detected.HasPrimaryVideoSource;
        MainVideoPath = detected.MainVideoPath;
        SeriesName = detected.SeriesName;
        SeasonNumber = detected.SeasonNumber;
        EpisodeNumber = detected.EpisodeNumber;
        SetAdditionalVideoPaths(detected.AdditionalVideoPaths);
        AudioDescriptionPath = detected.AudioDescriptionPath ?? string.Empty;
        SetSubtitles(detected.SubtitlePaths);
        SetDetectedAttachmentPaths(detected.AttachmentPaths);
        SetRelatedEpisodeFilePaths(detected.RelatedFilePaths);
        _outputPathWasManuallyChanged = false;
        OutputPath = outputPath;
        Title = string.IsNullOrWhiteSpace(titleOverride) ? detected.SuggestedTitle : titleOverride;
        SetMetadataResolutionState(metadataResolution);
        SetManualCheckFiles(detected.RequiresManualCheck, detected.ManualCheckFilePaths);
        SetNotes(detected.Notes);
        SetPlanNotes([]);
        OnPropertyChanged(nameof(UsesAutomaticOutputPath));
    }

    public virtual void SetAudioDescription(string? path)
    {
        var previousAudioDescriptionPath = AudioDescriptionPath;
        AudioDescriptionPath = string.IsNullOrWhiteSpace(path) ? string.Empty : path;
        RefreshManualCheckStateForAudioDescription(previousAudioDescriptionPath);
    }

    public virtual void SetSubtitles(IEnumerable<string> paths)
    {
        _subtitlePaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        OnPropertyChanged(nameof(SubtitlePaths));
        OnPropertyChanged(nameof(SubtitleDisplayText));
    }

    public virtual void SetAttachments(IEnumerable<string> paths)
    {
        _hasManualAttachmentOverride = true;
        SetAttachmentPaths(paths);
    }

    public virtual void SetOutputPath(string outputPath)
    {
        _outputPathWasManuallyChanged = true;
        OutputPath = outputPath;
    }

    public virtual void SetAutomaticOutputPath(string outputPath)
    {
        if (_outputPathWasManuallyChanged)
        {
            return;
        }

        OutputPath = outputPath;
    }

    public void SetPlanSummary(string summaryText)
    {
        PlanSummaryText = summaryText;
    }

    public void SetUsageSummary(EpisodeUsageSummary? usageSummary)
    {
        UsageSummary = usageSummary;
    }

    public virtual void SetVideoLanguageOverride(string? languageCode)
    {
        var normalized = NormalizeLanguageOverride(languageCode);
        if (_videoLanguageOverride == normalized)
        {
            return;
        }

        _videoLanguageOverride = normalized;
        OnPropertyChanged(nameof(VideoLanguageOverride));
        OnLanguageOverridesChanged();
    }

    public virtual void SetAudioLanguageOverride(string? languageCode)
    {
        var normalized = NormalizeLanguageOverride(languageCode);
        if (_audioLanguageOverride == normalized)
        {
            return;
        }

        _audioLanguageOverride = normalized;
        OnPropertyChanged(nameof(AudioLanguageOverride));
        OnLanguageOverridesChanged();
    }

    public virtual void SetOriginalLanguageOverride(string? languageCode)
    {
        var normalized = NormalizeLanguageOverride(languageCode);
        if (_originalLanguageOverride == normalized)
        {
            return;
        }

        _originalLanguageOverride = normalized;
        OnPropertyChanged(nameof(OriginalLanguageOverride));
        OnPropertyChanged(nameof(EffectiveOriginalLanguage));
        OnLanguageOverridesChanged();
    }

    public void ReplaceExcludedSourcePaths(IEnumerable<string> excludedSourcePaths)
    {
        _excludedSourcePaths.Clear();
        foreach (var excludedSourcePath in excludedSourcePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            _excludedSourcePaths.Add(excludedSourcePath);
        }
    }

    public virtual void ApproveCurrentReviewTarget()
    {
        if (!string.IsNullOrWhiteSpace(CurrentReviewTargetPath))
        {
            _approvedReviewPaths.Add(CurrentReviewTargetPath);
        }

        NotifyManualCheckStatePropertiesChanged(includeCurrentReviewTarget: true);
    }

    public virtual void ApplyTvdbSelection(TvdbEpisodeSelection selection)
    {
        SetTvdbSelection(selection);
        SeriesName = selection.TvdbSeriesName;
        SeasonNumber = selection.SeasonNumber;
        EpisodeNumber = selection.EpisodeNumber;
        Title = selection.EpisodeTitle;
    }

    public virtual void ApplyLocalMetadataGuess()
    {
        SetTvdbSelection(null);
        SeriesName = LocalSeriesName;
        SeasonNumber = LocalSeasonNumber;
        EpisodeNumber = LocalEpisodeNumber;
        Title = LocalTitle;
    }

    public virtual void ApproveMetadataReview(string statusText)
    {
        MetadataStatusText = statusText;
        RequiresMetadataReview = false;
        IsMetadataReviewApproved = true;
    }

    public virtual void ApprovePlanReview()
    {
        if (!HasPendingPlanReview)
        {
            return;
        }

        _isPlanReviewApproved = true;
        NotifyNotePropertiesChanged();
    }

    protected bool OutputPathWasManuallyChanged => _outputPathWasManuallyChanged;

    protected void MarkOutputPathAsAutomatic()
    {
        _outputPathWasManuallyChanged = false;
        OnPropertyChanged(nameof(UsesAutomaticOutputPath));
    }

    protected void SetMetadataResolutionState(EpisodeMetadataResolutionResult resolution)
    {
        SetTvdbSelection(resolution.Selection);
        MetadataStatusText = resolution.StatusText;
        RequiresMetadataReview = resolution.RequiresReview;
        IsMetadataReviewApproved = DetermineAutomaticMetadataApproval(resolution);
    }

    protected void ClearTvdbSelection()
    {
        SetTvdbSelection(null);
    }

    protected void SetNotes(IEnumerable<string> notes)
    {
        _notes = notes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        NotifyNotePropertiesChanged();
    }

    /// <summary>
    /// Aktualisiert nur die nicht-planerzeugten Basis-Hinweise eines Eintrags. Das wird für
    /// UI-nahe Zustände wie Batch-Ausgabezielkonflikte verwendet, die sich ohne neue Detection
    /// aus den bereits sichtbaren Einträgen neu ableiten lassen.
    /// </summary>
    protected void UpdateNotes(Func<IReadOnlyList<string>, IEnumerable<string>> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        _notes = update(_notes)
            .Where(note => !string.IsNullOrWhiteSpace(note))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        NotifyNotePropertiesChanged();
    }

    /// <summary>
    /// Aktualisiert die planerzeugten Hinweise gezielt, ohne den gesamten Plantext neu zu setzen.
    /// Das wird für UI-nahe Korrekturen wie Batch-Ausgabezielkonflikte verwendet, damit veraltete
    /// Prüfhinweise sofort verschwinden, sobald sich der zugrunde liegende Zielpfad ändert.
    /// </summary>
    protected void UpdatePlanNotes(Func<IReadOnlyList<string>, IEnumerable<string>> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        SetPlanNotes(update(_planNotes));
    }

    public void SetPlanNotes(IEnumerable<string> notes)
    {
        var previousPlanReviewKey = BuildPlanReviewKey(_planNotes);

        _planNotes = notes
            .Where(note => !string.IsNullOrWhiteSpace(note))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var currentPlanReviewKey = BuildPlanReviewKey(_planNotes);
        if (string.IsNullOrWhiteSpace(currentPlanReviewKey)
            || !string.Equals(previousPlanReviewKey, currentPlanReviewKey, StringComparison.Ordinal))
        {
            // Die Freigabe gilt nur für exakt denselben fachlichen Planhinweis. Sobald ein
            // Refresh neue Mehrfachfolgen-/Archivhinweise liefert, muss wieder sichtbar geprüft werden.
            _isPlanReviewApproved = false;
        }

        NotifyNotePropertiesChanged();
    }

    protected void SetRequestedMainVideoPath(string path)
    {
        _requestedMainVideoPath = path;
        OnPropertyChanged(nameof(RequestedMainVideoPath));
    }

    protected void SetDetectionSeedPath(string path)
    {
        _detectionSeedPath = path;
    }

    protected void SetLocalMetadataGuess(EpisodeMetadataGuess guess)
    {
        _localSeriesName = guess.SeriesName;
        _localSeasonNumber = guess.SeasonNumber;
        _localEpisodeNumber = guess.EpisodeNumber;
        _localTitle = guess.EpisodeTitle;
        OnPropertyChanged(nameof(LocalSeriesName));
        OnPropertyChanged(nameof(LocalSeasonNumber));
        OnPropertyChanged(nameof(LocalEpisodeNumber));
        OnPropertyChanged(nameof(LocalTitle));
    }

    protected void SetRelatedEpisodeFilePaths(IEnumerable<string> paths)
    {
        _relatedEpisodeFilePaths = paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        OnPropertyChanged(nameof(RelatedEpisodeFilePaths));
    }

    protected void SetAdditionalVideoPaths(IEnumerable<string> paths)
    {
        _additionalVideoPaths = paths.ToList();
        OnPropertyChanged(nameof(AdditionalVideoPaths));
        OnPropertyChanged(nameof(AdditionalVideosDisplayText));
        OnPropertyChanged(nameof(VideoAndAudioDescriptionDisplayText));
        OnPropertyChanged(nameof(SourceFilePaths));
    }

    protected void SetManualCheckFiles(bool requiresManualCheck, IEnumerable<string> filePaths)
    {
        RequiresManualCheck = requiresManualCheck;
        _manualCheckFilePaths = filePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!RequiresManualCheck)
        {
            _approvedReviewPaths.Clear();
        }
        else
        {
            _approvedReviewPaths.IntersectWith(_manualCheckFilePaths);
        }

        OnPropertyChanged(nameof(ManualCheckFilePaths));
        NotifyManualCheckStatePropertiesChanged(includeCurrentReviewTarget: true);
    }

    private void RefreshManualCheckStateForAudioDescription(string? previousAudioDescriptionPath)
    {
        var updatedFilePaths = _manualCheckFilePaths
            .Where(path => !PathComparisonHelper.AreSamePath(path, previousAudioDescriptionPath))
            .ToList();

        if (RequiresManualCheckForAudioDescription(AudioDescriptionPath))
        {
            updatedFilePaths.Add(AudioDescriptionPath);
        }

        SetManualCheckFiles(updatedFilePaths.Count > 0, updatedFilePaths);
    }

    private static bool RequiresManualCheckForAudioDescription(string? audioDescriptionPath)
    {
        return string.Equals(
            CompanionTextMetadataReader.ReadForMediaFile(audioDescriptionPath).Sender?.Trim(),
            "SRF",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Leitet aus dem Ergebnis der TVDB-Automatik ab, ob die Zuordnung bereits als wirklich freigegeben gilt.
    /// </summary>
    /// <param name="resolution">Automatisches TVDB-Ergebnis inklusive Ablaufzustand.</param>
    /// <returns>
    /// <see langword="true"/>, wenn eine Anfrage tatsächlich ausgeführt wurde und kein weiterer Review nötig ist;
    /// andernfalls <see langword="false"/>, damit übersprungene Automatikläufe im UI offen bleiben.
    /// </returns>
    protected static bool DetermineAutomaticMetadataApproval(EpisodeMetadataResolutionResult resolution)
    {
        // Eine übersprungene oder fehlgeschlagene Automatik soll im UI nicht wie eine freigegebene TVDB-Zuordnung wirken.
        return resolution.QueryWasAttempted && !resolution.RequiresReview;
    }

    protected void SetRequestedSourcePaths(IEnumerable<string> paths)
    {
        _requestedSourcePaths = paths.ToList();
        OnPropertyChanged(nameof(RequestedSourcePaths));
        OnPropertyChanged(nameof(RequestedSourcesDisplayText));
    }

    private void SetTvdbSelection(TvdbEpisodeSelection? selection)
    {
        if (Equals(_tvdbSelection, selection))
        {
            return;
        }

        _tvdbSelection = selection;
        OnPropertyChanged(nameof(TvdbSeriesId));
        OnPropertyChanged(nameof(TvdbSeriesName));
        OnPropertyChanged(nameof(TvdbEpisodeId));
        OnPropertyChanged(nameof(EffectiveOriginalLanguage));
    }

    protected void RefreshArchiveState()
    {
        SetArchiveState(ResolveArchiveState(OutputPath));
    }

    protected void SetArchiveState(EpisodeArchiveState archiveState)
    {
        if (_archiveState == archiveState)
        {
            return;
        }

        _archiveState = archiveState;
        NotifyArchiveStatePropertiesChanged();
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static EpisodeArchiveState ResolveArchiveState(string outputPath)
    {
        return !string.IsNullOrWhiteSpace(outputPath) && File.Exists(outputPath)
            ? EpisodeArchiveState.Existing
            : EpisodeArchiveState.New;
    }

    private IEnumerable<string> EnumerateSourceFilePaths()
    {
        if (HasPrimaryVideoSource && !string.IsNullOrWhiteSpace(MainVideoPath))
        {
            yield return MainVideoPath;
        }

        foreach (var additionalVideoPath in _additionalVideoPaths)
        {
            yield return additionalVideoPath;
        }

        if (!string.IsNullOrWhiteSpace(AudioDescriptionPath))
        {
            yield return AudioDescriptionPath;
        }
    }

    /// <summary>
    /// Setzt automatisch erkannte Anhänge und hebt dabei bewusst einen eventuell früheren manuellen Override auf.
    /// </summary>
    private void SetDetectedAttachmentPaths(IEnumerable<string> paths)
    {
        _hasManualAttachmentOverride = false;
        SetAttachmentPaths(paths);
    }

    private void SetAttachmentPaths(IEnumerable<string> paths)
    {
        _attachmentPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        OnPropertyChanged(nameof(AttachmentPaths));
        OnPropertyChanged(nameof(ManualAttachmentPaths));
        OnPropertyChanged(nameof(AttachmentDisplayText));
    }

    private void NotifyArchiveStatePropertiesChanged()
    {
        OnPropertyChanged(nameof(ArchiveState));
        OnPropertyChanged(nameof(ArchiveStateText));
        OnPropertyChanged(nameof(ArchiveStateTooltip));
        OnPropertyChanged(nameof(ArchiveBadgeBackground));
        OnPropertyChanged(nameof(ArchiveBadgeBorderBrush));
        OnPropertyChanged(nameof(ArchiveSortKey));
    }

    private void NotifyMetadataReviewStatePropertiesChanged()
    {
        OnPropertyChanged(nameof(HasPendingMetadataReview));
        NotifyReviewStatePropertiesChanged();
    }

    private void NotifyManualCheckStatePropertiesChanged(bool includeCurrentReviewTarget)
    {
        if (includeCurrentReviewTarget)
        {
            OnPropertyChanged(nameof(CurrentReviewTargetPath));
            OnPropertyChanged(nameof(IsManualCheckApproved));
        }

        OnPropertyChanged(nameof(ManualCheckText));
        OnPropertyChanged(nameof(HasPendingManualCheck));
        NotifyReviewStatePropertiesChanged();
    }

    private void NotifyReviewStatePropertiesChanged()
    {
        OnPropertyChanged(nameof(ReviewState));
        OnPropertyChanged(nameof(ReviewHint));
        OnPropertyChanged(nameof(ReviewHintTooltip));
        OnPropertyChanged(nameof(ReviewBadgeBackground));
        OnPropertyChanged(nameof(ReviewBadgeBorderBrush));
        OnPropertyChanged(nameof(HasPendingPlanReview));
        OnPropertyChanged(nameof(IsPlanReviewApproved));
        OnPropertyChanged(nameof(HasPendingChecks));
    }

    private void NotifyNotePropertiesChanged()
    {
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(NotesDisplayText));
        OnPropertyChanged(nameof(ActionablePlanNotes));
        OnPropertyChanged(nameof(HasActionablePlanNotes));
        OnPropertyChanged(nameof(PrimaryActionablePlanNote));
        OnPropertyChanged(nameof(ActionablePlanNotesDisplayText));
        NotifyReviewStatePropertiesChanged();
    }

    protected virtual void OnLanguageOverridesChanged()
    {
    }

    private static string BuildPlanReviewKey(IEnumerable<string> notes)
    {
        return string.Join(
            "\n",
            notes
                .Where(EpisodeEditTextBuilder.IsActionablePlanReviewNote)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(note => note, StringComparer.OrdinalIgnoreCase));
    }

    private static string NormalizeLanguageOverride(string? languageCode)
    {
        return string.IsNullOrWhiteSpace(languageCode)
            ? string.Empty
            : MediaLanguageHelper.NormalizeMuxLanguageCode(languageCode);
    }

}
