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
        DetectedSeriesName = candidate.DetectedSeriesName ?? string.Empty;
        _targetFolderName = candidate.SuggestedFolderName;
        _state = candidate.State;
        _note = candidate.Note;
        _isSelected = candidate.State == DownloadSortItemState.Ready;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DisplayName { get; }

    public IReadOnlyList<string> FilePaths { get; }

    public string DetectedSeriesName { get; }

    public int FileCount => FilePaths.Count;

    public string FilesDisplayText => string.Join(Environment.NewLine, FilePaths.Select(Path.GetFileName));

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
        }
    }

    public string StatusText => State switch
    {
        DownloadSortItemState.Ready => "Bereit",
        DownloadSortItemState.Conflict => "Konflikt",
        _ => "Pruefen"
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
