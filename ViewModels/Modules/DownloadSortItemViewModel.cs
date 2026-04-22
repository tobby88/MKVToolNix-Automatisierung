using System.ComponentModel;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// UI-Abbild eines einzelnen losen Download-Pakets, das in einen Serienordner einsortiert werden soll.
/// </summary>
internal sealed class DownloadSortItemViewModel : INotifyPropertyChanged
{
    private string _targetFolderName;
    private DownloadSortItemState _state;
    private readonly string _persistentNote;
    private string _note;
    private bool _isSelected;

    public DownloadSortItemViewModel(DownloadSortCandidate candidate)
    {
        DisplayName = candidate.DisplayName;
        FilePaths = candidate.FilePaths;
        DefectiveFilePaths = candidate.DefectiveFilePaths ?? [];
        ContainsDefectiveFiles = candidate.ContainsDefectiveFiles || DefectiveFilePaths.Count > 0;
        InitialTargetFolderName = candidate.SuggestedFolderName;
        _targetFolderName = candidate.SuggestedFolderName;
        _state = candidate.State;
        _persistentNote = candidate.PersistentNote;
        _note = candidate.Note;
        _isSelected = candidate.IsInitiallySelected && DownloadSortItemStates.IsSortable(candidate.State);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DisplayName { get; }

    public IReadOnlyList<string> FilePaths { get; }

    /// <summary>
    /// Teilmenge von <see cref="FilePaths"/>, die beim Einsortieren bewusst in den
    /// Defekt-Ordner statt in den gewählten Serienordner verschoben wird.
    /// </summary>
    public IReadOnlyList<string> DefectiveFilePaths { get; }

    /// <summary>
    /// Kennzeichnet Pakete, bei denen mindestens eine Datei separat in den Defekt-Ordner geht,
    /// auch wenn weitere Begleiter regulär einsortiert werden können.
    /// </summary>
    public bool ContainsDefectiveFiles { get; }

    /// <summary>
    /// Ursprünglich automatisch erkannter Zielordner; bleibt stabil, damit ein manuell
    /// korrigierter Zielordner gezielt auf weitere Einträge derselben Erkennungsgruppe
    /// übertragen werden kann.
    /// </summary>
    public string InitialTargetFolderName { get; }

    /// <summary>
    /// Gibt an, ob der Eintrag aktuell als echte Auswahleingabe für den Sortierlauf dienen darf.
    /// Nicht einsortierbare Zustände werden automatisch abgewählt.
    /// </summary>
    public bool CanSelect => DownloadSortItemStates.IsSortable(State);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            var normalizedValue = value && !CanSelect ? false : value;
            if (_isSelected == normalizedValue)
            {
                return;
            }

            _isSelected = normalizedValue;
            OnPropertyChanged();
        }
    }

    public string TargetFolderName
    {
        get => _targetFolderName;
        set
        {
            var normalizedValue = string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : EpisodeFileNameHelper.SanitizePathSegment(value.Trim());
            if (_targetFolderName == normalizedValue)
            {
                return;
            }

            _targetFolderName = normalizedValue;
            OnPropertyChanged();
        }
    }

    public DownloadSortItemState State
    {
        get => _state;
        private set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            if (_isSelected && !CanSelect)
            {
                _isSelected = false;
                OnPropertyChanged(nameof(IsSelected));
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSelect));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBadgeBackground));
            OnPropertyChanged(nameof(StatusBadgeBorderBrush));
        }
    }

    public string StatusText
    {
        get
        {
            var baseText = State switch
            {
                DownloadSortItemState.Ready => "Bereit",
                DownloadSortItemState.ReadyWithReplacement => "Ersetzen",
                DownloadSortItemState.Conflict => "Konflikt",
                DownloadSortItemState.Defective => "Defekt",
                _ => "Pruefen"
            };

            return ContainsDefectiveFiles && State != DownloadSortItemState.Defective
                ? baseText + " + Defekt"
                : baseText;
        }
    }

    public string StatusBadgeBackground => State switch
    {
        DownloadSortItemState.Ready => "#E7F5EA",
        DownloadSortItemState.ReadyWithReplacement => "#E5F0FF",
        DownloadSortItemState.Conflict => "#FDE7E7",
        DownloadSortItemState.Defective => "#FFF1D6",
        _ => "#FFF7E0"
    };

    public string StatusBadgeBorderBrush => State switch
    {
        DownloadSortItemState.Ready => "#66A875",
        DownloadSortItemState.ReadyWithReplacement => "#5A8DEE",
        DownloadSortItemState.Conflict => "#CC4B4B",
        DownloadSortItemState.Defective => "#D8902F",
        _ => "#D0A34B"
    };

    public string Note
    {
        get => _note;
        private set
        {
            if (_note == value)
            {
                return;
            }

            _note = value;
            OnPropertyChanged();
        }
    }

    public void ApplyEvaluation(DownloadSortTargetEvaluation evaluation)
    {
        State = evaluation.State;
        Note = MergeNotes(_persistentNote, evaluation.Note);
    }

    private static string MergeNotes(string left, string right)
    {
        return string.IsNullOrWhiteSpace(left)
            ? right
            : string.IsNullOrWhiteSpace(right)
                ? left
                : left == right
                    ? left
                    : $"{left} {right}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
