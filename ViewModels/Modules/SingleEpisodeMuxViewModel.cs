using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.ViewModels.Commands;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

public sealed class SingleEpisodeMuxViewModel : INotifyPropertyChanged
{
    private const string DefaultMainVideoDirectory = @"C:\Users\tobby\Downloads\MediathekView-latest-win\Downloads";

    private readonly AppServices _services;
    private readonly UserDialogService _dialogService;

    private string? _mainVideoPath;
    private string? _audioDescriptionPath;
    private List<string> _subtitlePaths = [];
    private string? _attachmentPath;
    private string? _outputPath;
    private string _title = string.Empty;
    private string _previewText = string.Empty;
    private string _statusText = "Bereit";
    private int _progressValue;
    private bool _isBusy;
    private string _lastSuggestedTitle = string.Empty;
    private SeriesEpisodeMuxPlan? _currentPlan;

    public SingleEpisodeMuxViewModel(AppServices services, UserDialogService dialogService)
    {
        _services = services;
        _dialogService = dialogService;

        SelectMainVideoCommand = new RelayCommand(SelectMainVideo, () => !_isBusy);
        SelectAudioDescriptionCommand = new RelayCommand(SelectAudioDescription, () => !_isBusy && !string.IsNullOrWhiteSpace(_mainVideoPath));
        SelectSubtitlesCommand = new RelayCommand(SelectSubtitles, () => !_isBusy && !string.IsNullOrWhiteSpace(_mainVideoPath));
        SelectAttachmentCommand = new RelayCommand(SelectAttachment, () => !_isBusy && !string.IsNullOrWhiteSpace(_mainVideoPath));
        SelectOutputCommand = new RelayCommand(SelectOutput, () => !_isBusy);
        RescanCommand = new RelayCommand(RescanFromMainVideo, () => !_isBusy && !string.IsNullOrWhiteSpace(_mainVideoPath));
        CreatePreviewCommand = new AsyncRelayCommand(CreatePreviewAsync, () => !_isBusy);
        ExecuteMuxCommand = new AsyncRelayCommand(ExecuteMuxAsync, () => !_isBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand SelectMainVideoCommand { get; }
    public RelayCommand SelectAudioDescriptionCommand { get; }
    public RelayCommand SelectSubtitlesCommand { get; }
    public RelayCommand SelectAttachmentCommand { get; }
    public RelayCommand SelectOutputCommand { get; }
    public RelayCommand RescanCommand { get; }
    public AsyncRelayCommand CreatePreviewCommand { get; }
    public AsyncRelayCommand ExecuteMuxCommand { get; }

    public string MainVideoPath
    {
        get => _mainVideoPath ?? string.Empty;
        private set
        {
            _mainVideoPath = string.IsNullOrWhiteSpace(value) ? null : value;
            OnPropertyChanged();
        }
    }

    public string AudioDescriptionPath
    {
        get => _audioDescriptionPath ?? string.Empty;
        private set
        {
            _audioDescriptionPath = string.IsNullOrWhiteSpace(value) ? null : value;
            OnPropertyChanged();
        }
    }

    public string AttachmentPath
    {
        get => _attachmentPath ?? string.Empty;
        private set
        {
            _attachmentPath = string.IsNullOrWhiteSpace(value) ? null : value;
            OnPropertyChanged();
        }
    }

    public string OutputPath
    {
        get => _outputPath ?? string.Empty;
        private set
        {
            _outputPath = string.IsNullOrWhiteSpace(value) ? null : value;
            OnPropertyChanged();
        }
    }

    public string SubtitleDisplayText => _subtitlePaths.Count == 0
        ? string.Empty
        : string.Join(Environment.NewLine, _subtitlePaths.Select(Path.GetFileName));

    public string Title
    {
        get => _title;
        set
        {
            if (_title == value)
            {
                return;
            }

            _title = value;
            _currentPlan = null;
            OnPropertyChanged();
        }
    }

    public string PreviewText
    {
        get => _previewText;
        private set
        {
            _previewText = value;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public int ProgressValue
    {
        get => _progressValue;
        private set
        {
            _progressValue = value;
            OnPropertyChanged();
        }
    }

    private string ResolveMainVideoInitialDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_mainVideoPath))
        {
            var existingDirectory = Path.GetDirectoryName(_mainVideoPath);
            if (!string.IsNullOrWhiteSpace(existingDirectory) && Directory.Exists(existingDirectory))
            {
                return existingDirectory;
            }
        }

        return Directory.Exists(DefaultMainVideoDirectory)
            ? DefaultMainVideoDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private string ResolveComponentInitialDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_mainVideoPath))
        {
            var directory = Path.GetDirectoryName(_mainVideoPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                return directory;
            }
        }

        return ResolveMainVideoInitialDirectory();
    }

    private string BuildFallbackOutputName()
    {
        if (!string.IsNullOrWhiteSpace(_outputPath))
        {
            return Path.GetFileName(_outputPath);
        }

        if (!string.IsNullOrWhiteSpace(_title))
        {
            return _title.Trim() + ".mkv";
        }

        return "Ausgabe.mkv";
    }

    private bool EnsureMainVideoSelected()
    {
        if (!string.IsNullOrWhiteSpace(_mainVideoPath))
        {
            return true;
        }

        _dialogService.ShowError("Bitte zuerst ein Hauptvideo auswaehlen.");
        return false;
    }

    private void ApplyAutoDetectedFiles(string mainVideoPath)
    {
        try
        {
            var detected = _services.SeriesEpisodeMux.DetectFromMainVideo(mainVideoPath);

            MainVideoPath = detected.MainVideoPath;
            AudioDescriptionPath = detected.AudioDescriptionPath ?? string.Empty;
            _subtitlePaths = detected.SubtitlePaths.ToList();
            AttachmentPath = detected.AttachmentPath ?? string.Empty;
            OutputPath = detected.SuggestedOutputFilePath;
            _currentPlan = null;

            if (string.IsNullOrWhiteSpace(Title) || Title == _lastSuggestedTitle)
            {
                Title = detected.SuggestedTitle;
            }

            _lastSuggestedTitle = detected.SuggestedTitle;
            OnPropertyChanged(nameof(SubtitleDisplayText));

            PreviewText = "Dateien wurden automatisch erkannt. Mit 'Vorschau erzeugen' kannst du den mkvmerge-Aufruf pruefen.";
            SetStatus("Dateien erkannt", 0);
            RefreshCommands();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(ex.Message);
        }
    }

    private void RescanFromMainVideo()
    {
        if (string.IsNullOrWhiteSpace(_mainVideoPath))
        {
            _dialogService.ShowError("Bitte zuerst ein Hauptvideo auswaehlen.");
            return;
        }

        ApplyAutoDetectedFiles(_mainVideoPath);
    }

    private void SetAudioDescription(string path)
    {
        AudioDescriptionPath = path;
        _currentPlan = null;
    }

    private void ClearAudioDescription()
    {
        AudioDescriptionPath = string.Empty;
        _currentPlan = null;
    }

    private void SetSubtitles(IEnumerable<string> paths)
    {
        _subtitlePaths = paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        _currentPlan = null;
        OnPropertyChanged(nameof(SubtitleDisplayText));
    }

    private void ClearSubtitles()
    {
        _subtitlePaths = [];
        _currentPlan = null;
        OnPropertyChanged(nameof(SubtitleDisplayText));
    }

    private void SetAttachment(string path)
    {
        AttachmentPath = path;
        _currentPlan = null;
    }

    private void ClearAttachment()
    {
        AttachmentPath = string.Empty;
        _currentPlan = null;
    }

    private void SetOutputPath(string path)
    {
        OutputPath = path;
        _currentPlan = null;
    }

    private void SelectMainVideo()
    {
        var path = _dialogService.SelectMainVideo(ResolveMainVideoInitialDirectory());
        if (!string.IsNullOrWhiteSpace(path))
        {
            ApplyAutoDetectedFiles(path);
        }
    }

    private void SelectAudioDescription()
    {
        if (!EnsureMainVideoSelected())
        {
            return;
        }

        var choice = _dialogService.AskAudioDescriptionChoice();
        if (choice == MessageBoxResult.No)
        {
            ClearAudioDescription();
            return;
        }

        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        var path = _dialogService.SelectAudioDescription(ResolveComponentInitialDirectory());
        if (!string.IsNullOrWhiteSpace(path))
        {
            SetAudioDescription(path);
        }
    }

    private void SelectSubtitles()
    {
        if (!EnsureMainVideoSelected())
        {
            return;
        }

        var choice = _dialogService.AskSubtitlesChoice();
        if (choice == MessageBoxResult.No)
        {
            ClearSubtitles();
            return;
        }

        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        var paths = _dialogService.SelectSubtitles(ResolveComponentInitialDirectory());
        if (paths is not null)
        {
            SetSubtitles(paths);
        }
    }

    private void SelectAttachment()
    {
        if (!EnsureMainVideoSelected())
        {
            return;
        }

        var choice = _dialogService.AskAttachmentChoice();
        if (choice == MessageBoxResult.No)
        {
            ClearAttachment();
            return;
        }

        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        var path = _dialogService.SelectAttachment(ResolveComponentInitialDirectory());
        if (!string.IsNullOrWhiteSpace(path))
        {
            SetAttachment(path);
        }
    }

    private void SelectOutput()
    {
        var path = _dialogService.SelectOutput(ResolveComponentInitialDirectory(), BuildFallbackOutputName());
        if (!string.IsNullOrWhiteSpace(path))
        {
            SetOutputPath(path);
        }
    }

    private async Task CreatePreviewAsync()
    {
        try
        {
            SetBusy(true);
            SetStatus("Erzeuge Vorschau...", 0);
            _currentPlan = await BuildPlanAsync();
            PreviewText = _services.SeriesEpisodeMux.BuildPreviewText(_currentPlan);
            SetStatus("Vorschau bereit", 0);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(ex.Message);
            SetStatus("Fehler", 0);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ExecuteMuxAsync()
    {
        try
        {
            SetBusy(true);
            _currentPlan ??= await BuildPlanAsync();
            PreviewText = _services.SeriesEpisodeMux.BuildPreviewText(_currentPlan)
                + Environment.NewLine
                + Environment.NewLine
                + "mkvmerge-Ausgabe:"
                + Environment.NewLine;

            if (!_dialogService.ConfirmMuxStart())
            {
                SetStatus("Abgebrochen", 0);
                return;
            }

            SetStatus("Muxing laeuft...", 0);
            var result = await _services.SeriesEpisodeMux.ExecuteAsync(_currentPlan, HandleMuxOutput, HandleMuxUpdate);

            if (result.ExitCode == 0 && !result.HasWarning)
            {
                SetStatus("Muxing erfolgreich abgeschlossen", 100);
                _dialogService.ShowInfo("Erfolg", $"MKV erfolgreich erstellt:\n{_currentPlan.OutputFilePath}");
            }
            else if ((result.ExitCode == 0 && result.HasWarning)
                || (result.ExitCode == 1 && File.Exists(_currentPlan.OutputFilePath)))
            {
                SetStatus("Muxing mit Warnungen abgeschlossen", 100);
                _dialogService.ShowWarning("Warnung", $"Die MKV wurde erstellt, aber mkvmerge hat Warnungen gemeldet.\n\n{_currentPlan.OutputFilePath}");
            }
            else
            {
                SetStatus($"Muxing fehlgeschlagen (Exit-Code {result.ExitCode})", 0);
                _dialogService.ShowWarning("Hinweis", $"mkvmerge wurde mit Exit-Code {result.ExitCode} beendet.");
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(ex.Message);
            SetStatus("Fehler", 0);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<SeriesEpisodeMuxPlan> BuildPlanAsync()
    {
        var request = new SeriesEpisodeMuxRequest(
            RequireValue(_mainVideoPath, "Bitte ein Hauptvideo auswaehlen."),
            _audioDescriptionPath,
            _subtitlePaths,
            _attachmentPath,
            RequireValue(_outputPath, "Bitte eine Ausgabedatei waehlen."),
            RequireValue(_title.Trim(), "Bitte einen Dateititel eingeben."));

        return await _services.SeriesEpisodeMux.CreatePlanAsync(request);
    }

    private void HandleMuxOutput(string line)
    {
        Application.Current.Dispatcher.Invoke(() => PreviewText += line + Environment.NewLine);
    }

    private void HandleMuxUpdate(MuxExecutionUpdate update)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var progressValue = update.ProgressPercent ?? ProgressValue;
            var statusText = update.ProgressPercent is int progressPercent
                ? $"Muxing laeuft... {progressPercent}%"
                : "Muxing laeuft...";

            if (update.HasWarning)
            {
                statusText += " - Warnung erkannt";
            }

            SetStatus(statusText, progressValue);
        });
    }

    private static string RequireValue(string? value, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return value;
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        SelectMainVideoCommand.RaiseCanExecuteChanged();
        SelectAudioDescriptionCommand.RaiseCanExecuteChanged();
        SelectSubtitlesCommand.RaiseCanExecuteChanged();
        SelectAttachmentCommand.RaiseCanExecuteChanged();
        SelectOutputCommand.RaiseCanExecuteChanged();
        RescanCommand.RaiseCanExecuteChanged();
        CreatePreviewCommand.RaiseCanExecuteChanged();
        ExecuteMuxCommand.RaiseCanExecuteChanged();
    }

    private void SetStatus(string text, int progressValue)
    {
        StatusText = text;
        ProgressValue = Math.Clamp(progressValue, 0, 100);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
