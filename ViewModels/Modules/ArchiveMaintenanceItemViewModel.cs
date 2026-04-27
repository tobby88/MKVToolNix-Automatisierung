using System.ComponentModel;
using System.Runtime.CompilerServices;
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

    public ArchiveMaintenanceItemViewModel(ArchiveMaintenanceItemAnalysis analysis)
    {
        _analysis = analysis;
        _isSelected = analysis.HasWritableChanges && !analysis.HasError;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FilePath => _analysis.FilePath;

    public string FileName => Path.GetFileName(FilePath);

    public string DirectoryPath => Path.GetDirectoryName(FilePath) ?? string.Empty;

    public bool CanSelect => HasWritableChanges && !_analysis.HasError && !_wasApplied;

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

    public bool HasWritableChanges => _analysis.HasWritableChanges;

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

            if (_analysis.RequiresRemux && _analysis.HasWritableChanges)
            {
                return "Korr. + Remux";
            }

            if (_analysis.RequiresRemux)
            {
                return "Remux nötig";
            }

            return _analysis.HasWritableChanges ? "Ändern" : "OK";
        }
    }

    public string StatusTone
    {
        get
        {
            if (_wasApplied || (!_analysis.HasError && !_analysis.RequiresRemux && !_analysis.HasWritableChanges))
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

            if (_analysis.ChangeNotes.Count > 0)
            {
                return string.Join("; ", _analysis.ChangeNotes);
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

            if (_analysis.ChangeNotes.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Schreibbare Änderungen:");
                lines.AddRange(_analysis.ChangeNotes.Select(note => "- " + note));
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

    public ArchiveMaintenanceApplyRequest CreateApplyRequest()
    {
        return new ArchiveMaintenanceApplyRequest(
            _analysis.FilePath,
            _analysis.RenameOperation,
            _analysis.ContainerTitleEdit,
            _analysis.TrackHeaderEdits);
    }

    public void MarkApplied(string currentFilePath)
    {
        _analysis = _analysis with
        {
            FilePath = currentFilePath,
            RenameOperation = null,
            ContainerTitleEdit = null,
            TrackHeaderEdits = [],
            ChangeNotes = []
        };
        _wasApplied = true;
        _isSelected = false;
        OnPropertyChanged(string.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
