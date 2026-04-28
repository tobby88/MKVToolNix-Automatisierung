using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Emby;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Bindbare Tabellenzeile der Archivpflege. Die Zeile zeigt ausschließlich analysierte
/// Änderungen; geschrieben wird erst über die explizite Auswahl im ViewModel.
/// </summary>
internal sealed class ArchiveMaintenanceItemViewModel : INotifyPropertyChanged
{
    public const string FileNameChangeKind = "FileName";
    public const string ContainerTitleChangeKind = "ContainerTitle";

    private ArchiveMaintenanceItemAnalysis _analysis;
    private bool _isSelected;
    private bool _wasApplied;
    private string _targetFileName;
    private string _targetContainerTitle;
    private string _targetNfoTitle;
    private string _targetNfoSortTitle;
    private bool _targetNfoTitleLocked;
    private bool _targetNfoSortTitleLocked;
    private string _targetTvdbId;
    private string _targetImdbId;
    private bool _removeImdbId;
    private bool _showAllHeaderCorrections;
    private readonly HashSet<string> _suppressedChangeKinds = new(StringComparer.Ordinal);

    public ArchiveMaintenanceItemViewModel(ArchiveMaintenanceItemAnalysis analysis)
    {
        _analysis = analysis;
        _targetFileName = Path.GetFileName(analysis.RenameOperation?.TargetPath ?? analysis.FilePath);
        _targetContainerTitle = analysis.ContainerTitleEdit?.ExpectedTitle ?? analysis.ContainerTitle;
        _targetNfoTitle = analysis.NfoTitle ?? string.Empty;
        _targetNfoSortTitle = analysis.NfoSortTitle ?? string.Empty;
        _targetNfoTitleLocked = analysis.NfoTitleLocked;
        _targetNfoSortTitleLocked = analysis.NfoSortTitleLocked;
        _targetTvdbId = analysis.ProviderIds.TvdbId ?? string.Empty;
        _targetImdbId = analysis.ProviderIds.ImdbId ?? string.Empty;
        HeaderCorrections = new ObservableCollection<ArchiveMaintenanceHeaderCorrectionViewModel>(
            analysis.TrackHeaderCorrectionCandidates.SelectMany(candidate =>
                candidate.Values.Select(value => new ArchiveMaintenanceHeaderCorrectionViewModel(candidate, value))));
        foreach (var correction in HeaderCorrections)
        {
            correction.PropertyChanged += HeaderCorrection_OnPropertyChanged;
        }

        _isSelected = CanSelect;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ArchiveMaintenanceHeaderCorrectionViewModel> HeaderCorrections { get; }

    public IEnumerable<ArchiveMaintenanceHeaderCorrectionViewModel> VisibleHeaderCorrections => ShowAllHeaderCorrections
        ? HeaderCorrections
        : HeaderCorrections.Where(correction => correction.HasChange);

    public IEnumerable<ArchiveMaintenanceHeaderCorrectionGroupViewModel> VisibleHeaderCorrectionGroups => VisibleHeaderCorrections
        .GroupBy(correction => correction.DisplayLabel, StringComparer.Ordinal)
        .Select(group => new ArchiveMaintenanceHeaderCorrectionGroupViewModel(group.Key, group.ToList()));

    public bool ShowAllHeaderCorrections
    {
        get => _showAllHeaderCorrections;
        set
        {
            if (_showAllHeaderCorrections == value)
            {
                return;
            }

            _showAllHeaderCorrections = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisibleHeaderCorrections));
            OnPropertyChanged(nameof(VisibleHeaderCorrectionGroups));
            OnPropertyChanged(nameof(VisibleHeaderCorrectionCount));
            OnPropertyChanged(nameof(VisibleHeaderCorrectionGroupCount));
            OnPropertyChanged(nameof(HiddenHeaderCorrectionCount));
            OnPropertyChanged(nameof(HeaderCorrectionModeText));
        }
    }

    public int VisibleHeaderCorrectionCount => VisibleHeaderCorrections.Count();

    public int VisibleHeaderCorrectionGroupCount => VisibleHeaderCorrectionGroups.Count();

    public int HiddenHeaderCorrectionCount => Math.Max(0, HeaderCorrections.Count - VisibleHeaderCorrectionCount);

    public string ManualCorrectionHeaderText => HasWritableChanges
        ? $"Manuelle Korrektur ({BuildDetailedCurrentChangeNotes().Count} Änderung(en))"
        : "Manuelle Korrektur (optional)";

    public string HeaderCorrectionModeText
    {
        get
        {
            if (HeaderCorrections.Count == 0)
            {
                return "Für diese Datei wurden keine Track-Headerwerte gelesen.";
            }

            if (ShowAllHeaderCorrections)
            {
                return "Alle Track-Werte sind eingeblendet. Unveränderte Zielwerte werden nicht geschrieben.";
            }

            return HiddenHeaderCorrectionCount == 0
                ? "Es werden nur Track-Werte angezeigt, die beim Anwenden wirklich geändert würden."
                : $"Es werden nur geänderte Track-Werte angezeigt. {HiddenHeaderCorrectionCount} unveränderte Werte sind ausgeblendet.";
        }
    }

    public string FilePath => _analysis.FilePath;

    public string FileName => Path.GetFileName(FilePath);

    public string DirectoryPath => Path.GetDirectoryName(FilePath) ?? string.Empty;

    public bool CanSelect => HasWritableChanges && !_analysis.HasError && !_wasApplied && string.IsNullOrWhiteSpace(ManualValidationMessage);

    public bool CanEditManualCorrections => !_analysis.HasError && !_wasApplied;

    public string TargetFileName
    {
        get => _targetFileName;
        set
        {
            var normalizedValue = value ?? string.Empty;
            if (_targetFileName == normalizedValue)
            {
                return;
            }

            _targetFileName = normalizedValue;
            OnPropertyChanged();
            NotifyManualCorrectionChanged();
        }
    }

    public string TargetContainerTitle
    {
        get => _targetContainerTitle;
        set
        {
            var normalizedValue = value ?? string.Empty;
            if (_targetContainerTitle == normalizedValue)
            {
                return;
            }

            _targetContainerTitle = normalizedValue;
            OnPropertyChanged();
            NotifyManualCorrectionChanged();
        }
    }

    public string TargetNfoTitle
    {
        get => _targetNfoTitle;
        set
        {
            var normalizedValue = value ?? string.Empty;
            if (_targetNfoTitle == normalizedValue)
            {
                return;
            }

            _targetNfoTitle = normalizedValue;
            if (!string.Equals(CurrentNfoTitle.Trim(), normalizedValue.Trim(), StringComparison.Ordinal))
            {
                SetTargetNfoTitleLocked(true);
            }

            OnPropertyChanged();
            NotifyManualCorrectionChanged();
        }
    }

    public string TargetNfoSortTitle
    {
        get => _targetNfoSortTitle;
        set
        {
            var normalizedValue = value ?? string.Empty;
            if (_targetNfoSortTitle == normalizedValue)
            {
                return;
            }

            _targetNfoSortTitle = normalizedValue;
            if (!string.Equals(CurrentNfoSortTitle.Trim(), normalizedValue.Trim(), StringComparison.Ordinal))
            {
                SetTargetNfoSortTitleLocked(true);
            }

            OnPropertyChanged();
            NotifyManualCorrectionChanged();
        }
    }

    public bool TargetNfoTitleLocked
    {
        get => _targetNfoTitleLocked;
        set
        {
            if (_targetNfoTitleLocked == value)
            {
                return;
            }

            SetTargetNfoTitleLocked(value);
            NotifyManualCorrectionChanged();
        }
    }

    public bool TargetNfoSortTitleLocked
    {
        get => _targetNfoSortTitleLocked;
        set
        {
            if (_targetNfoSortTitleLocked == value)
            {
                return;
            }

            SetTargetNfoSortTitleLocked(value);
            NotifyManualCorrectionChanged();
        }
    }

    public string CurrentFileName => Path.GetFileName(_analysis.FilePath);

    public string CurrentContainerTitle => _analysis.ContainerTitle ?? string.Empty;

    public string CurrentNfoTitle => _analysis.NfoTitle ?? string.Empty;

    public string CurrentNfoSortTitle => _analysis.NfoSortTitle ?? string.Empty;

    public bool CurrentNfoTitleLocked => _analysis.NfoTitleLocked;

    public bool CurrentNfoSortTitleLocked => _analysis.NfoSortTitleLocked;

    public bool HasNfoTextSync => _analysis.NfoExists;

    public string NfoTitleLockButtonText => TargetNfoTitleLocked ? "Gesperrt" : "Offen";

    public string NfoSortTitleLockButtonText => TargetNfoSortTitleLocked ? "Gesperrt" : "Offen";

    public bool HasSuppressedFileNameChange => _suppressedChangeKinds.Contains(FileNameChangeKind);

    public bool HasSuppressedContainerTitleChange => _suppressedChangeKinds.Contains(ContainerTitleChangeKind);

    public bool CanSuppressFileNameChange => CanEditManualCorrections
        && !HasSuppressedFileNameChange
        && !string.Equals(CurrentFileName, SuggestedFileName, StringComparison.Ordinal);

    public bool CanRestoreFileNameSuggestion => CanEditManualCorrections && HasSuppressedFileNameChange;

    public bool CanSuppressContainerTitleChange => CanEditManualCorrections
        && !HasSuppressedContainerTitleChange
        && !string.Equals(CurrentContainerTitle.Trim(), SuggestedContainerTitle.Trim(), StringComparison.Ordinal);

    public bool CanRestoreContainerTitleSuggestion => CanEditManualCorrections && HasSuppressedContainerTitleChange;

    public string TargetTvdbId
    {
        get => _targetTvdbId;
        set
        {
            var normalizedValue = value?.Trim() ?? string.Empty;
            if (_targetTvdbId == normalizedValue)
            {
                return;
            }

            _targetTvdbId = normalizedValue;
            OnPropertyChanged();
            NotifyManualCorrectionChanged();
        }
    }

    public string TargetImdbId
    {
        get => _targetImdbId;
        set
        {
            var normalizedValue = value?.Trim() ?? string.Empty;
            if (_targetImdbId == normalizedValue)
            {
                return;
            }

            _targetImdbId = normalizedValue;
            _removeImdbId = string.IsNullOrWhiteSpace(normalizedValue) && !string.IsNullOrWhiteSpace(_analysis.ProviderIds.ImdbId);
            OnPropertyChanged();
            NotifyManualCorrectionChanged();
        }
    }

    public bool HasProviderIdSync => _analysis.NfoExists;

    public bool CanReviewTvdb => CanEditManualCorrections && TryBuildMetadataGuess(out _);

    public bool CanReviewImdb => CanEditManualCorrections && HasProviderIdSync;

    public string ProviderIdSummaryText => HasProviderIdSync
        ? "Provider-IDs aus der NFO. Änderungen werden erst beim Anwenden geschrieben."
        : "Keine NFO vorhanden. TVDB-/IMDb-IDs können erst nach Emby-NFO-Erzeugung geschrieben werden.";

    public string? ManualValidationMessage => BuildManualValidationMessage();

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            var normalizedValue = value && CanSelect;
            if (_isSelected == normalizedValue)
            {
                return;
            }

            _isSelected = normalizedValue;
            OnPropertyChanged();
        }
    }

    public bool HasWritableChanges => CreateCurrentRenameOperation() is not null
        || CreateCurrentContainerTitleEdit() is not null
        || CreateCurrentTrackHeaderEdits().Count > 0
        || CreateCurrentProviderIdEdit() is not null
        || CreateCurrentNfoTextEdit() is not null;

    public string StatusText
    {
        get
        {
            if (_wasApplied)
            {
                return "Erledigt";
            }

            if (_analysis.HasError)
            {
                return "Fehler";
            }

            if (!string.IsNullOrWhiteSpace(ManualValidationMessage))
            {
                return "Ungültig";
            }

            if (_analysis.RequiresRemux && HasWritableChanges)
            {
                return "Korr. + Remux";
            }

            if (_analysis.RequiresRemux)
            {
                return "Remux nötig";
            }

            return HasWritableChanges ? "Ändern" : "OK";
        }
    }

    public string StatusTone
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ManualValidationMessage))
            {
                return "Error";
            }

            if (_wasApplied || (!_analysis.HasError && !_analysis.RequiresRemux && !HasWritableChanges))
            {
                return "Done";
            }

            if (_analysis.HasError)
            {
                return "Error";
            }

            return _analysis.RequiresRemux ? "Warning" : "Ready";
        }
    }

    public string ChangeSummary
    {
        get
        {
            if (_wasApplied)
            {
                return "Freigegebene Änderungen wurden angewendet.";
            }

            if (!string.IsNullOrWhiteSpace(_analysis.ErrorMessage))
            {
                return _analysis.ErrorMessage!;
            }

            if (!string.IsNullOrWhiteSpace(ManualValidationMessage))
            {
                return "Ungültige manuelle Korrektur: " + ManualValidationMessage;
            }

            var changeNotes = BuildCurrentChangeNotes();
            if (changeNotes.Count > 0)
            {
                return string.Join("; ", changeNotes);
            }

            if (_analysis.Issues.Count > 0)
            {
                return string.Join("; ", _analysis.Issues.Select(issue => issue.Message));
            }

            return "Keine Änderung nötig.";
        }
    }

    public string DetailText
    {
        get
        {
            var lines = new List<string>
            {
                FilePath
            };

            if (!string.IsNullOrWhiteSpace(ManualValidationMessage))
            {
                lines.Add(string.Empty);
                lines.Add("Manuelle Korrektur prüfen:");
                lines.Add("- " + ManualValidationMessage);
            }

            var changeNotes = BuildDetailedCurrentChangeNotes();
            if (changeNotes.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Schreibbare Änderungen:");
                lines.AddRange(changeNotes.Select(note => "- " + note));
            }

            if (_analysis.Issues.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Nicht automatisch korrigierbar:");
                lines.AddRange(_analysis.Issues.Select(issue => "- " + issue.Message));
            }

            if (_analysis.RenameOperation is { Sidecars.Count: > 0 } renameOperation)
            {
                lines.Add(string.Empty);
                lines.Add("Begleitdateien werden mit umbenannt:");
                lines.AddRange(renameOperation.Sidecars.Select(sidecar =>
                    $"- {Path.GetFileName(sidecar.SourcePath)} -> {Path.GetFileName(sidecar.TargetPath)}"));
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    public string DetailFilePath => FilePath;

    public IReadOnlyList<string> WritableChangeNotes => BuildDetailedCurrentChangeNotes();

    public bool HasWritableDetailChanges => WritableChangeNotes.Count > 0;

    public IReadOnlyList<string> IssueMessages => _analysis.Issues
        .Select(issue => issue.Message)
        .ToList();

    public bool HasIssues => IssueMessages.Count > 0;

    public IReadOnlyList<string> SidecarRenameNotes => CreateCurrentRenameOperation() is { Sidecars.Count: > 0 } renameOperation
        ? renameOperation.Sidecars
            .Select(sidecar => $"{Path.GetFileName(sidecar.SourcePath)} -> {Path.GetFileName(sidecar.TargetPath)}")
            .ToList()
        : [];

    public bool HasSidecarRenameNotes => SidecarRenameNotes.Count > 0;

    public bool HasNoDetailFindings => !HasWritableDetailChanges && !HasIssues && !HasSidecarRenameNotes;

    public string DetailSummaryText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ManualValidationMessage))
            {
                return "Manuelle Korrektur prüfen: " + ManualValidationMessage;
            }

            if (HasNoDetailFindings)
            {
                return "Keine Änderung nötig.";
            }

            var parts = new List<string>();
            if (HasWritableDetailChanges)
            {
                parts.Add($"{WritableChangeNotes.Count} direkt schreibbare Änderung(en)");
            }

            if (HasIssues)
            {
                parts.Add($"{IssueMessages.Count} Remux-Hinweis(e)");
            }

            return string.Join(", ", parts);
        }
    }

    public ArchiveMaintenanceApplyRequest CreateApplyRequest()
    {
        return new ArchiveMaintenanceApplyRequest(
            _analysis.FilePath,
            CreateCurrentRenameOperation(),
            CreateCurrentContainerTitleEdit(),
            CreateCurrentTrackHeaderEdits(),
            CreateCurrentProviderIdEdit(),
            CreateCurrentNfoTextEdit());
    }

    public void MarkApplied(string currentFilePath)
    {
        var currentContainerTitle = TargetContainerTitle.Trim();
        _analysis = _analysis with
        {
            FilePath = currentFilePath,
            ContainerTitle = currentContainerTitle,
            ProviderIds = new EmbyProviderIds(
                string.IsNullOrWhiteSpace(TargetTvdbId) ? null : TargetTvdbId.Trim(),
                string.IsNullOrWhiteSpace(TargetImdbId) ? null : TargetImdbId.Trim()),
            RenameOperation = null,
            ContainerTitleEdit = null,
            TrackHeaderEdits = [],
            TrackHeaderCorrectionCandidates = [],
            ChangeNotes = [],
            NfoTitle = string.IsNullOrWhiteSpace(TargetNfoTitle) ? null : TargetNfoTitle.Trim(),
            NfoSortTitle = string.IsNullOrWhiteSpace(TargetNfoSortTitle) ? null : TargetNfoSortTitle.Trim(),
            NfoTitleLocked = TargetNfoTitleLocked,
            NfoSortTitleLocked = TargetNfoSortTitleLocked
        };
        _wasApplied = true;
        _isSelected = false;
        _targetFileName = Path.GetFileName(currentFilePath);
        _targetContainerTitle = currentContainerTitle;
        _targetNfoTitle = _analysis.NfoTitle ?? string.Empty;
        _targetNfoSortTitle = _analysis.NfoSortTitle ?? string.Empty;
        _targetNfoTitleLocked = _analysis.NfoTitleLocked;
        _targetNfoSortTitleLocked = _analysis.NfoSortTitleLocked;
        _targetTvdbId = _analysis.ProviderIds.TvdbId ?? string.Empty;
        _targetImdbId = _analysis.ProviderIds.ImdbId ?? string.Empty;
        _removeImdbId = false;
        _suppressedChangeKinds.Clear();
        HeaderCorrections.Clear();
        OnPropertyChanged(string.Empty);
    }

    public bool TryBuildMetadataGuess(out EpisodeMetadataGuess? guess)
    {
        var fileName = IsTargetFileNameUsable()
            ? TargetFileName.Trim()
            : Path.GetFileName(_analysis.FilePath);
        guess = ArchiveMaintenanceService.TryBuildMetadataGuess(fileName);
        return guess is not null;
    }

    public void ApplyTvdbSelection(TvdbEpisodeSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        TargetTvdbId = selection.TvdbEpisodeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        TargetContainerTitle = selection.EpisodeTitle;
        TargetFileName = EpisodeFileNameHelper.BuildEpisodeFileName(
            selection.TvdbSeriesName,
            selection.SeasonNumber,
            selection.EpisodeNumber,
            selection.EpisodeTitle);
        if (HasNfoTextSync)
        {
            TargetNfoTitle = selection.EpisodeTitle;
        }
    }

    public void ApplyImdbSelection(string imdbId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imdbId);

        _removeImdbId = false;
        TargetImdbId = imdbId.Trim();
    }

    public void MarkImdbUnavailable()
    {
        _targetImdbId = string.Empty;
        _removeImdbId = !string.IsNullOrWhiteSpace(_analysis.ProviderIds.ImdbId);
        OnPropertyChanged(nameof(TargetImdbId));
        NotifyManualCorrectionChanged();
    }

    public void ResetTargetFileNameToCurrent()
    {
        TargetFileName = CurrentFileName;
    }

    public void ResetTargetContainerTitleToCurrent()
    {
        TargetContainerTitle = CurrentContainerTitle;
    }

    public void ResetTargetNfoTitleToCurrent()
    {
        TargetNfoTitle = CurrentNfoTitle;
        TargetNfoTitleLocked = CurrentNfoTitleLocked;
    }

    public void ResetTargetNfoSortTitleToCurrent()
    {
        TargetNfoSortTitle = CurrentNfoSortTitle;
        TargetNfoSortTitleLocked = CurrentNfoSortTitleLocked;
    }

    public void ToggleTargetNfoTitleLock()
    {
        TargetNfoTitleLocked = !TargetNfoTitleLocked;
    }

    public void ToggleTargetNfoSortTitleLock()
    {
        TargetNfoSortTitleLocked = !TargetNfoSortTitleLocked;
    }

    public ArchiveMaintenanceSuppressedChange? SuppressFileNameChange()
    {
        return SuppressChange(FileNameChangeKind, CurrentFileName, SuggestedFileName, () => TargetFileName = CurrentFileName);
    }

    public ArchiveMaintenanceSuppressedChange? SuppressContainerTitleChange()
    {
        return SuppressChange(ContainerTitleChangeKind, CurrentContainerTitle, SuggestedContainerTitle, () => TargetContainerTitle = CurrentContainerTitle);
    }

    public void RestoreFileNameSuggestion()
    {
        _suppressedChangeKinds.Remove(FileNameChangeKind);
        TargetFileName = SuggestedFileName;
        NotifySuppressionStateChanged();
    }

    public void RestoreContainerTitleSuggestion()
    {
        _suppressedChangeKinds.Remove(ContainerTitleChangeKind);
        TargetContainerTitle = SuggestedContainerTitle;
        NotifySuppressionStateChanged();
    }

    public void ApplySuppressedChanges(IEnumerable<ArchiveMaintenanceSuppressedChange> suppressedChanges)
    {
        foreach (var suppressedChange in suppressedChanges)
        {
            if (MatchesSuppression(suppressedChange, FileNameChangeKind, CurrentFileName, SuggestedFileName))
            {
                _suppressedChangeKinds.Add(FileNameChangeKind);
                _targetFileName = CurrentFileName;
            }
            else if (MatchesSuppression(suppressedChange, ContainerTitleChangeKind, CurrentContainerTitle, SuggestedContainerTitle))
            {
                _suppressedChangeKinds.Add(ContainerTitleChangeKind);
                _targetContainerTitle = CurrentContainerTitle;
            }
        }

        if (_suppressedChangeKinds.Count > 0)
        {
            OnPropertyChanged(nameof(TargetFileName));
            OnPropertyChanged(nameof(TargetContainerTitle));
            NotifyManualCorrectionChanged();
            NotifySuppressionStateChanged();
        }
    }

    private string SuggestedFileName => Path.GetFileName(_analysis.RenameOperation?.TargetPath ?? _analysis.FilePath);

    private string SuggestedContainerTitle => _analysis.ContainerTitleEdit?.ExpectedTitle ?? CurrentContainerTitle;

    private ArchiveMaintenanceSuppressedChange? SuppressChange(
        string changeKind,
        string currentValue,
        string suggestedValue,
        Action applyCurrentValue)
    {
        if (string.Equals(currentValue.Trim(), suggestedValue.Trim(), StringComparison.Ordinal))
        {
            return null;
        }

        _suppressedChangeKinds.Add(changeKind);
        applyCurrentValue();
        NotifySuppressionStateChanged();
        return new ArchiveMaintenanceSuppressedChange
        {
            MediaFilePath = _analysis.FilePath,
            ChangeKind = changeKind,
            CurrentValue = currentValue.Trim(),
            SuggestedValue = suggestedValue.Trim()
        };
    }

    private bool MatchesSuppression(
        ArchiveMaintenanceSuppressedChange suppressedChange,
        string changeKind,
        string currentValue,
        string suggestedValue)
    {
        return PathComparisonHelper.AreSamePath(suppressedChange.MediaFilePath, _analysis.FilePath)
               && string.Equals(suppressedChange.ChangeKind, changeKind, StringComparison.Ordinal)
               && string.Equals(suppressedChange.CurrentValue, currentValue.Trim(), StringComparison.Ordinal)
               && string.Equals(suppressedChange.SuggestedValue, suggestedValue.Trim(), StringComparison.Ordinal);
    }

    private void NotifySuppressionStateChanged()
    {
        OnPropertyChanged(nameof(HasSuppressedFileNameChange));
        OnPropertyChanged(nameof(HasSuppressedContainerTitleChange));
        OnPropertyChanged(nameof(CanSuppressFileNameChange));
        OnPropertyChanged(nameof(CanRestoreFileNameSuggestion));
        OnPropertyChanged(nameof(CanSuppressContainerTitleChange));
        OnPropertyChanged(nameof(CanRestoreContainerTitleSuggestion));
    }

    private IReadOnlyList<string> BuildCurrentChangeNotes()
    {
        return ArchiveHeaderNormalizationService
            .BuildHeaderChangeNotes(CreateCurrentContainerTitleEdit(), CreateCurrentTrackHeaderEdits())
            .Concat(CreateCurrentRenameOperation() is ArchiveRenameOperation renameOperation
                ? [$"Dateiname: {Path.GetFileName(renameOperation.SourcePath)} -> {Path.GetFileName(renameOperation.TargetPath)}"]
                : [])
            .Concat(BuildNfoTextChangeNotes(CreateCurrentNfoTextEdit()))
            .Concat(BuildProviderIdChangeNotes())
            .ToList();
    }

    private IReadOnlyList<string> BuildDetailedCurrentChangeNotes()
    {
        return BuildContainerTitleChangeNotes(CreateCurrentContainerTitleEdit())
            .Concat(BuildTrackHeaderDetailChangeNotes(CreateCurrentTrackHeaderEdits()))
            .Concat(CreateCurrentRenameOperation() is ArchiveRenameOperation renameOperation
                ? [$"Dateiname: {Path.GetFileName(renameOperation.SourcePath)} -> {Path.GetFileName(renameOperation.TargetPath)}"]
                : [])
            .Concat(BuildNfoTextChangeNotes(CreateCurrentNfoTextEdit()))
            .Concat(BuildProviderIdChangeNotes())
            .ToList();
    }

    private static IEnumerable<string> BuildContainerTitleChangeNotes(ContainerTitleEditOperation? containerTitleEdit)
    {
        if (containerTitleEdit is not null)
        {
            yield return $"MKV-Titel: {ArchiveHeaderNormalizationService.FormatHeaderValue(containerTitleEdit.CurrentTitle)} -> {ArchiveHeaderNormalizationService.FormatHeaderValue(containerTitleEdit.ExpectedTitle)}";
        }
    }

    private static IEnumerable<string> BuildNfoTextChangeNotes(ArchiveNfoTextEditOperation? nfoTextEdit)
    {
        if (nfoTextEdit is null)
        {
            yield break;
        }

        if (!string.Equals(nfoTextEdit.CurrentTitle ?? string.Empty, nfoTextEdit.ExpectedTitle ?? string.Empty, StringComparison.Ordinal))
        {
            yield return $"NFO-Titel: {ArchiveHeaderNormalizationService.FormatHeaderValue(nfoTextEdit.CurrentTitle)} -> {ArchiveHeaderNormalizationService.FormatHeaderValue(nfoTextEdit.ExpectedTitle)}";
        }

        if (nfoTextEdit.CurrentTitleLocked != nfoTextEdit.ExpectedTitleLocked)
        {
            yield return $"NFO-Titel-Sperre: {FormatLockValue(nfoTextEdit.CurrentTitleLocked)} -> {FormatLockValue(nfoTextEdit.ExpectedTitleLocked)}";
        }

        if (!string.Equals(nfoTextEdit.CurrentSortTitle ?? string.Empty, nfoTextEdit.ExpectedSortTitle ?? string.Empty, StringComparison.Ordinal))
        {
            yield return $"NFO-Sortiertitel: {ArchiveHeaderNormalizationService.FormatHeaderValue(nfoTextEdit.CurrentSortTitle)} -> {ArchiveHeaderNormalizationService.FormatHeaderValue(nfoTextEdit.ExpectedSortTitle)}";
        }

        if (nfoTextEdit.CurrentSortTitleLocked != nfoTextEdit.ExpectedSortTitleLocked)
        {
            yield return $"NFO-Sortiertitel-Sperre: {FormatLockValue(nfoTextEdit.CurrentSortTitleLocked)} -> {FormatLockValue(nfoTextEdit.ExpectedSortTitleLocked)}";
        }
    }

    private static string FormatLockValue(bool value) => value ? "gesperrt" : "offen";

    private static IEnumerable<string> BuildTrackHeaderDetailChangeNotes(IReadOnlyList<TrackHeaderEditOperation> trackHeaderEdits)
    {
        foreach (var edit in trackHeaderEdits)
        {
            if (edit.ValueEdits is not { Count: > 0 } valueEdits)
            {
                yield return BuildTrackNameDetailChangeNote(edit);
                continue;
            }

            foreach (var valueEdit in valueEdits)
            {
                yield return string.Equals(valueEdit.PropertyName, "name", StringComparison.Ordinal)
                    ? BuildTrackNameDetailChangeNote(edit, valueEdit)
                    : $"{ArchiveHeaderNormalizationService.FormatHeaderValue(edit.DisplayLabel)}: {valueEdit.DisplayName}: {ArchiveHeaderNormalizationService.FormatHeaderValue(valueEdit.CurrentDisplayValue)} -> {ArchiveHeaderNormalizationService.FormatHeaderValue(valueEdit.ExpectedDisplayValue)}";
            }
        }
    }

    private static string BuildTrackNameDetailChangeNote(
        TrackHeaderEditOperation edit,
        TrackHeaderValueEdit? valueEdit = null)
    {
        var currentValue = ArchiveHeaderNormalizationService.FormatHeaderValue(valueEdit?.CurrentDisplayValue ?? edit.CurrentTrackName);
        var expectedValue = ArchiveHeaderNormalizationService.FormatHeaderValue(valueEdit?.ExpectedDisplayValue ?? edit.ExpectedTrackName);
        return string.Equals(ArchiveHeaderNormalizationService.FormatHeaderValue(edit.DisplayLabel), currentValue, StringComparison.Ordinal)
            ? $"{currentValue} -> {expectedValue}"
            : $"{ArchiveHeaderNormalizationService.FormatHeaderValue(edit.DisplayLabel)}: {currentValue} -> {expectedValue}";
    }

    private ArchiveRenameOperation? CreateCurrentRenameOperation()
    {
        if (_analysis.HasError || _wasApplied || !IsTargetFileNameUsable())
        {
            return null;
        }

        return ArchiveMaintenanceService.BuildManualRenameOperation(_analysis.FilePath, TargetFileName);
    }

    private ContainerTitleEditOperation? CreateCurrentContainerTitleEdit()
    {
        if (_analysis.HasError || _wasApplied)
        {
            return null;
        }

        var currentTitle = (_analysis.ContainerTitle ?? string.Empty).Trim();
        var targetTitle = TargetContainerTitle.Trim();
        return string.Equals(currentTitle, targetTitle, StringComparison.Ordinal)
            ? null
            : new ContainerTitleEditOperation(currentTitle, targetTitle);
    }

    private IReadOnlyList<TrackHeaderEditOperation> CreateCurrentTrackHeaderEdits()
    {
        if (_analysis.HasError || _wasApplied)
        {
            return [];
        }

        return HeaderCorrections
            .Select(correction => new
            {
                Correction = correction,
                ValueEdit = correction.CreateValueEdit()
            })
            .Where(entry => entry.ValueEdit is not null)
            .GroupBy(entry => entry.Correction.Selector)
            .Select(group =>
            {
                var first = group.First().Correction;
                var valueEdits = group
                    .Select(entry => entry.ValueEdit!)
                    .ToList();
                var expectedTrackName = valueEdits
                    .FirstOrDefault(value => string.Equals(value.PropertyName, "name", StringComparison.Ordinal))
                    ?.ExpectedDisplayValue
                    ?? first.CurrentTrackName;
                return new TrackHeaderEditOperation(
                    first.Selector,
                    first.DisplayLabel,
                    first.CurrentTrackName,
                    expectedTrackName,
                    valueEdits);
            })
            .ToList();
    }

    private ArchiveProviderIdEditOperation? CreateCurrentProviderIdEdit()
    {
        if (_analysis.HasError || _wasApplied || !_analysis.NfoExists || !AreProviderIdsUsable())
        {
            return null;
        }

        var targetTvdbId = string.IsNullOrWhiteSpace(TargetTvdbId) ? null : TargetTvdbId.Trim();
        var targetImdbId = string.IsNullOrWhiteSpace(TargetImdbId) ? null : TargetImdbId.Trim();
        var tvdbChanged = !string.Equals(_analysis.ProviderIds.TvdbId ?? string.Empty, targetTvdbId ?? string.Empty, StringComparison.Ordinal);
        var imdbChanged = !string.Equals(_analysis.ProviderIds.ImdbId ?? string.Empty, targetImdbId ?? string.Empty, StringComparison.Ordinal);
        var removeImdbId = _removeImdbId || (string.IsNullOrWhiteSpace(targetImdbId) && !string.IsNullOrWhiteSpace(_analysis.ProviderIds.ImdbId));
        return tvdbChanged || imdbChanged || removeImdbId
            ? new ArchiveProviderIdEditOperation(new EmbyProviderIds(targetTvdbId, targetImdbId), removeImdbId)
            : null;
    }

    private ArchiveNfoTextEditOperation? CreateCurrentNfoTextEdit()
    {
        if (_analysis.HasError || _wasApplied || !_analysis.NfoExists)
        {
            return null;
        }

        var currentTitle = CurrentNfoTitle.Trim();
        var currentSortTitle = CurrentNfoSortTitle.Trim();
        var targetTitle = TargetNfoTitle.Trim();
        var targetSortTitle = TargetNfoSortTitle.Trim();
        return string.Equals(currentTitle, targetTitle, StringComparison.Ordinal)
               && string.Equals(currentSortTitle, targetSortTitle, StringComparison.Ordinal)
               && CurrentNfoTitleLocked == TargetNfoTitleLocked
               && CurrentNfoSortTitleLocked == TargetNfoSortTitleLocked
            ? null
            : new ArchiveNfoTextEditOperation(
                currentTitle,
                targetTitle,
                currentSortTitle,
                targetSortTitle,
                CurrentNfoTitleLocked,
                TargetNfoTitleLocked,
                CurrentNfoSortTitleLocked,
                TargetNfoSortTitleLocked);
    }

    private IEnumerable<string> BuildProviderIdChangeNotes()
    {
        if (!_analysis.NfoExists || !AreProviderIdsUsable())
        {
            yield break;
        }

        if (!string.Equals(_analysis.ProviderIds.TvdbId ?? string.Empty, TargetTvdbId.Trim(), StringComparison.Ordinal))
        {
            yield return $"TVDB-ID: {FormatProviderId(_analysis.ProviderIds.TvdbId)} -> {FormatProviderId(TargetTvdbId)}";
        }

        if (_removeImdbId && string.IsNullOrWhiteSpace(TargetImdbId))
        {
            yield return $"IMDb-ID: {FormatProviderId(_analysis.ProviderIds.ImdbId)} -> keine IMDb-ID";
            yield break;
        }

        if (!string.Equals(_analysis.ProviderIds.ImdbId ?? string.Empty, TargetImdbId.Trim(), StringComparison.Ordinal))
        {
            yield return $"IMDb-ID: {FormatProviderId(_analysis.ProviderIds.ImdbId)} -> {FormatProviderId(TargetImdbId)}";
        }
    }

    private static string FormatProviderId(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<leer>" : value.Trim();
    }

    private string? BuildManualValidationMessage()
    {
        if (_analysis.HasError || _wasApplied)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(TargetFileName))
        {
            return "Der Ziel-Dateiname darf nicht leer sein.";
        }

        var fileName = TargetFileName.Trim();
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "Der Ziel-Dateiname enthält ungültige Zeichen.";
        }

        if (!string.Equals(Path.GetExtension(fileName), ".mkv", StringComparison.OrdinalIgnoreCase))
        {
            return "Der Ziel-Dateiname muss auf .mkv enden.";
        }

        if (!AreProviderIdsUsable())
        {
            return BuildProviderIdValidationMessage();
        }

        var renameOperation = ArchiveMaintenanceService.BuildManualRenameOperation(_analysis.FilePath, fileName);
        if (renameOperation is not null && File.Exists(renameOperation.TargetPath))
        {
            return "Die Ziel-MKV existiert bereits.";
        }

        var existingSidecarTarget = renameOperation?.Sidecars.FirstOrDefault(sidecar => File.Exists(sidecar.TargetPath));
        return existingSidecarTarget is null
            ? null
            : $"Die Ziel-Begleitdatei existiert bereits: {Path.GetFileName(existingSidecarTarget.TargetPath)}.";
    }

    private bool AreProviderIdsUsable()
    {
        return BuildProviderIdValidationMessage() is null;
    }

    private string? BuildProviderIdValidationMessage()
    {
        if (!string.IsNullOrWhiteSpace(TargetTvdbId) && !TargetTvdbId.Trim().All(char.IsDigit))
        {
            return "Die TVDB-ID darf nur Ziffern enthalten.";
        }

        if (string.IsNullOrWhiteSpace(TargetTvdbId) && !string.IsNullOrWhiteSpace(_analysis.ProviderIds.TvdbId))
        {
            return "Eine vorhandene TVDB-ID kann hier nicht entfernt werden. Bitte stattdessen eine korrekte TVDB-ID eintragen.";
        }

        var imdbId = TargetImdbId.Trim();
        if (!string.IsNullOrWhiteSpace(imdbId)
            && !System.Text.RegularExpressions.Regex.IsMatch(imdbId, "^tt\\d{7,10}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return "Die IMDb-ID muss im Format tt1234567 vorliegen.";
        }

        return null;
    }

    private bool IsTargetFileNameUsable()
    {
        if (string.IsNullOrWhiteSpace(TargetFileName))
        {
            return false;
        }

        var fileName = TargetFileName.Trim();
        return fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && string.Equals(Path.GetExtension(fileName), ".mkv", StringComparison.OrdinalIgnoreCase);
    }

    private void HeaderCorrection_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ArchiveMaintenanceHeaderCorrectionViewModel.TargetValue)
            or nameof(ArchiveMaintenanceHeaderCorrectionViewModel.HasChange))
        {
            NotifyManualCorrectionChanged();
        }
    }

    private void NotifyManualCorrectionChanged()
    {
        if (!CanSelect && _isSelected)
        {
            _isSelected = false;
            OnPropertyChanged(nameof(IsSelected));
        }

        OnPropertyChanged(nameof(CanSelect));
        OnPropertyChanged(nameof(HasWritableChanges));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusTone));
        OnPropertyChanged(nameof(ChangeSummary));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(WritableChangeNotes));
        OnPropertyChanged(nameof(HasWritableDetailChanges));
        OnPropertyChanged(nameof(SidecarRenameNotes));
        OnPropertyChanged(nameof(HasSidecarRenameNotes));
        OnPropertyChanged(nameof(HasNoDetailFindings));
        OnPropertyChanged(nameof(DetailSummaryText));
        OnPropertyChanged(nameof(ManualValidationMessage));
        OnPropertyChanged(nameof(HasProviderIdSync));
        OnPropertyChanged(nameof(HasNfoTextSync));
        OnPropertyChanged(nameof(TargetNfoTitleLocked));
        OnPropertyChanged(nameof(TargetNfoSortTitleLocked));
        OnPropertyChanged(nameof(NfoTitleLockButtonText));
        OnPropertyChanged(nameof(NfoSortTitleLockButtonText));
        OnPropertyChanged(nameof(CanReviewTvdb));
        OnPropertyChanged(nameof(CanReviewImdb));
        OnPropertyChanged(nameof(ProviderIdSummaryText));
        OnPropertyChanged(nameof(VisibleHeaderCorrections));
        OnPropertyChanged(nameof(VisibleHeaderCorrectionGroups));
        OnPropertyChanged(nameof(VisibleHeaderCorrectionCount));
        OnPropertyChanged(nameof(VisibleHeaderCorrectionGroupCount));
        OnPropertyChanged(nameof(HiddenHeaderCorrectionCount));
        OnPropertyChanged(nameof(ManualCorrectionHeaderText));
        OnPropertyChanged(nameof(HeaderCorrectionModeText));
        NotifySuppressionStateChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void SetTargetNfoTitleLocked(bool value)
    {
        if (_targetNfoTitleLocked == value)
        {
            return;
        }

        _targetNfoTitleLocked = value;
        OnPropertyChanged(nameof(TargetNfoTitleLocked));
        OnPropertyChanged(nameof(NfoTitleLockButtonText));
    }

    private void SetTargetNfoSortTitleLocked(bool value)
    {
        if (_targetNfoSortTitleLocked == value)
        {
            return;
        }

        _targetNfoSortTitleLocked = value;
        OnPropertyChanged(nameof(TargetNfoSortTitleLocked));
        OnPropertyChanged(nameof(NfoSortTitleLockButtonText));
    }
}

/// <summary>
/// Gruppiert editierbare Headerwerte einer einzelnen Spur für die Archivpflege-Oberfläche.
/// </summary>
internal sealed record ArchiveMaintenanceHeaderCorrectionGroupViewModel(
    string DisplayLabel,
    IReadOnlyList<ArchiveMaintenanceHeaderCorrectionViewModel> Values);

/// <summary>
/// Eine editierbare Zielwert-Zeile für die manuelle Header-Korrektur der Archivpflege.
/// Jede Zeile beschreibt genau eine <c>mkvpropedit</c>-Property einer vorhandenen Spur.
/// </summary>
internal sealed class ArchiveMaintenanceHeaderCorrectionViewModel : INotifyPropertyChanged
{
    private string _targetValue;

    public ArchiveMaintenanceHeaderCorrectionViewModel(
        ArchiveTrackHeaderCorrectionCandidate track,
        ArchiveTrackHeaderValueCandidate value)
    {
        Selector = track.Selector;
        DisplayLabel = track.DisplayLabel;
        CurrentTrackName = track.CurrentTrackName;
        PropertyName = value.PropertyName;
        DisplayName = value.DisplayName;
        CurrentDisplayValue = value.CurrentDisplayValue;
        IsFlag = value.IsFlag;
        _targetValue = value.IsFlag
            ? NormalizeFlagDisplayValue(value.ExpectedDisplayValue)
            : value.ExpectedDisplayValue;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static IReadOnlyList<string> FlagOptions { get; } = ["ja", "nein"];

    public IReadOnlyList<string> AvailableFlagValues => FlagOptions;

    public string Selector { get; }

    public string DisplayLabel { get; }

    public string CurrentTrackName { get; }

    public string PropertyName { get; }

    public string DisplayName { get; }

    public string CurrentDisplayValue { get; }

    public bool IsFlag { get; }

    public bool IsTextValue => !IsFlag;

    public bool HasChange => CreateValueEdit() is not null;

    public string TargetValue
    {
        get => _targetValue;
        set
        {
            var normalizedValue = IsFlag
                ? NormalizeFlagDisplayValue(value)
                : value ?? string.Empty;
            if (_targetValue == normalizedValue)
            {
                return;
            }

            _targetValue = normalizedValue;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasChange));
        }
    }

    public TrackHeaderValueEdit? CreateValueEdit()
    {
        var currentValue = NormalizeDisplayValue(CurrentDisplayValue);
        var targetDisplayValue = IsFlag
            ? NormalizeFlagDisplayValue(TargetValue)
            : NormalizeDisplayValue(TargetValue);
        if (string.Equals(currentValue, targetDisplayValue, StringComparison.Ordinal))
        {
            return null;
        }

        return new TrackHeaderValueEdit(
            PropertyName,
            DisplayName,
            currentValue,
            targetDisplayValue,
            IsFlag ? ResolveFlagRawValue(targetDisplayValue) : targetDisplayValue);
    }

    private static string NormalizeDisplayValue(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static string NormalizeFlagDisplayValue(string? value)
    {
        var normalizedValue = NormalizeDisplayValue(value);
        if (normalizedValue.Equals("1", StringComparison.OrdinalIgnoreCase)
            || normalizedValue.Equals("true", StringComparison.OrdinalIgnoreCase)
            || normalizedValue.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || normalizedValue.Equals("ja", StringComparison.OrdinalIgnoreCase))
        {
            return "ja";
        }

        if (normalizedValue.Equals("0", StringComparison.OrdinalIgnoreCase)
            || normalizedValue.Equals("false", StringComparison.OrdinalIgnoreCase)
            || normalizedValue.Equals("no", StringComparison.OrdinalIgnoreCase)
            || normalizedValue.Equals("nein", StringComparison.OrdinalIgnoreCase))
        {
            return "nein";
        }

        return normalizedValue;
    }

    private static string ResolveFlagRawValue(string displayValue)
    {
        return NormalizeFlagDisplayValue(displayValue).Equals("ja", StringComparison.Ordinal)
            ? "1"
            : "0";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
