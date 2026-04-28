using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Bindbare Tabellenzeile der Archivpflege. Die Zeile zeigt ausschließlich analysierte
/// Änderungen; geschrieben wird erst über die explizite Auswahl im ViewModel.
/// </summary>
internal sealed class ArchiveMaintenanceItemViewModel : INotifyPropertyChanged
{
    private ArchiveMaintenanceItemAnalysis _analysis;
    private bool _isSelected;
    private bool _wasApplied;
    private string _targetFileName;
    private string _targetContainerTitle;
    private bool _showAllHeaderCorrections;

    public ArchiveMaintenanceItemViewModel(ArchiveMaintenanceItemAnalysis analysis)
    {
        _analysis = analysis;
        _targetFileName = Path.GetFileName(analysis.RenameOperation?.TargetPath ?? analysis.FilePath);
        _targetContainerTitle = analysis.ContainerTitleEdit?.ExpectedTitle ?? analysis.ContainerTitle;
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
            OnPropertyChanged(nameof(VisibleHeaderCorrectionCount));
            OnPropertyChanged(nameof(HiddenHeaderCorrectionCount));
            OnPropertyChanged(nameof(HeaderCorrectionModeText));
        }
    }

    public int VisibleHeaderCorrectionCount => VisibleHeaderCorrections.Count();

    public int HiddenHeaderCorrectionCount => Math.Max(0, HeaderCorrections.Count - VisibleHeaderCorrectionCount);

    public string ManualCorrectionHeaderText => HasWritableChanges
        ? $"Manuelle Korrektur ({BuildCurrentChangeNotes().Count} Änderung(en))"
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
        || CreateCurrentTrackHeaderEdits().Count > 0;

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

            var changeNotes = BuildCurrentChangeNotes();
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

    public IReadOnlyList<string> WritableChangeNotes => BuildCurrentChangeNotes();

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
            CreateCurrentTrackHeaderEdits());
    }

    public void MarkApplied(string currentFilePath)
    {
        var currentContainerTitle = TargetContainerTitle.Trim();
        _analysis = _analysis with
        {
            FilePath = currentFilePath,
            ContainerTitle = currentContainerTitle,
            RenameOperation = null,
            ContainerTitleEdit = null,
            TrackHeaderEdits = [],
            TrackHeaderCorrectionCandidates = [],
            ChangeNotes = []
        };
        _wasApplied = true;
        _isSelected = false;
        _targetFileName = Path.GetFileName(currentFilePath);
        _targetContainerTitle = currentContainerTitle;
        HeaderCorrections.Clear();
        OnPropertyChanged(string.Empty);
    }

    private IReadOnlyList<string> BuildCurrentChangeNotes()
    {
        return ArchiveHeaderNormalizationService
            .BuildHeaderChangeNotes(CreateCurrentContainerTitleEdit(), CreateCurrentTrackHeaderEdits())
            .Concat(CreateCurrentRenameOperation() is ArchiveRenameOperation renameOperation
                ? [$"Dateiname: {Path.GetFileName(renameOperation.SourcePath)} -> {Path.GetFileName(renameOperation.TargetPath)}"]
                : [])
            .ToList();
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

        var targetPath = Path.Combine(DirectoryPath, fileName);
        if (!PathComparisonHelper.AreSamePath(FilePath, targetPath) && File.Exists(targetPath))
        {
            return "Die Ziel-MKV existiert bereits.";
        }

        var renameOperation = ArchiveMaintenanceService.BuildManualRenameOperation(_analysis.FilePath, fileName);
        var existingSidecarTarget = renameOperation?.Sidecars.FirstOrDefault(sidecar => File.Exists(sidecar.TargetPath));
        return existingSidecarTarget is null
            ? null
            : $"Die Ziel-Begleitdatei existiert bereits: {Path.GetFileName(existingSidecarTarget.TargetPath)}.";
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
        OnPropertyChanged(nameof(VisibleHeaderCorrections));
        OnPropertyChanged(nameof(VisibleHeaderCorrectionCount));
        OnPropertyChanged(nameof(HiddenHeaderCorrectionCount));
        OnPropertyChanged(nameof(ManualCorrectionHeaderText));
        OnPropertyChanged(nameof(HeaderCorrectionModeText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

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
