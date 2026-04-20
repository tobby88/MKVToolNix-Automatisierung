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
    private string _note;
    private bool _isSelected;

    public DownloadSortItemViewModel(DownloadSortCandidate candidate)
    {
        DisplayName = candidate.DisplayName;
        FilePaths = candidate.FilePaths;
        DefectiveFilePaths = candidate.DefectiveFilePaths ?? [];
        InitialTargetFolderName = candidate.SuggestedFolderName;
        _targetFolderName = candidate.SuggestedFolderName;
        _state = candidate.State;
        _note = candidate.Note;
        _isSelected = candidate.IsInitiallySelected;
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
    /// Ursprünglich automatisch erkannter Zielordner; bleibt stabil, damit ein manuell
    /// korrigierter Zielordner gezielt auf weitere Einträge derselben Erkennungsgruppe
    /// übertragen werden kann.
    /// </summary>
    public string InitialTargetFolderName { get; }

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
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBadgeBackground));
            OnPropertyChanged(nameof(StatusBadgeBorderBrush));
        }
    }

    public string StatusText => State switch
    {
        DownloadSortItemState.Ready => "Bereit",
        DownloadSortItemState.ReadyWithReplacement => "Ersetzen",
        DownloadSortItemState.Conflict => "Konflikt",
        DownloadSortItemState.Defective => "Defekt",
        _ => "Pruefen"
    };

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
        Note = evaluation.Note;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
