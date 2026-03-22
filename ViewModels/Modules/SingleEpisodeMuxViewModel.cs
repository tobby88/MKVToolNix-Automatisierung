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
    private List<string> _attachmentPaths = [];
    private string? _outputPath;
    private string _title = string.Empty;
    private string _previewText = string.Empty;
    private string _statusText = "Bereit";
    private int _progressValue;
    private bool _isBusy;
    private string _lastSuggestedTitle = string.Empty;
    private bool _requiresManualCheck;
    private string _manualCheckText = string.Empty;
    private List<string> _manualCheckFilePaths = [];
    private string? _detectionSeedPath;
    private readonly HashSet<string> _excludedSourcePaths = new(StringComparer.OrdinalIgnoreCase);
    private string? _approvedReviewPath;
    private SeriesEpisodeMuxPlan? _currentPlan;

    public SingleEpisodeMuxViewModel(AppServices services, UserDialogService dialogService)
    {
        _services = services;
        _dialogService = dialogService;

        SelectMainVideoCommand = new AsyncRelayCommand(SelectMainVideoAsync, () => !_isBusy);
        SelectAudioDescriptionCommand = new AsyncRelayCommand(SelectAudioDescriptionAsync, () => !_isBusy);
        SelectSubtitlesCommand = new RelayCommand(SelectSubtitles, () => !_isBusy && !string.IsNullOrWhiteSpace(_mainVideoPath));
        SelectAttachmentCommand = new RelayCommand(SelectAttachments, () => !_isBusy && !string.IsNullOrWhiteSpace(_mainVideoPath));
        SelectOutputCommand = new RelayCommand(SelectOutput, () => !_isBusy);
        RescanCommand = new AsyncRelayCommand(RescanFromMainVideoAsync, () => !_isBusy && !string.IsNullOrWhiteSpace(_mainVideoPath));
        TestSelectedSourcesCommand = new RelayCommand(TestSelectedSources, () => !_isBusy && _manualCheckFilePaths.Count > 0);
        CreatePreviewCommand = new AsyncRelayCommand(CreatePreviewAsync, () => !_isBusy);
        ExecuteMuxCommand = new AsyncRelayCommand(ExecuteMuxAsync, () => !_isBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncRelayCommand SelectMainVideoCommand { get; }
    public AsyncRelayCommand SelectAudioDescriptionCommand { get; }
    public RelayCommand SelectSubtitlesCommand { get; }
    public RelayCommand SelectAttachmentCommand { get; }
    public RelayCommand SelectOutputCommand { get; }
    public AsyncRelayCommand RescanCommand { get; }
    public RelayCommand TestSelectedSourcesCommand { get; }
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

    public string AttachmentDisplayText => _attachmentPaths.Count == 0
        ? string.Empty
        : string.Join(Environment.NewLine, _attachmentPaths.Select(Path.GetFileName));

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

    public bool RequiresManualCheck
    {
        get => _requiresManualCheck;
        private set
        {
            _requiresManualCheck = value;
            OnPropertyChanged();
        }
    }

    public string ManualCheckText
    {
        get => _manualCheckText;
        private set
        {
            _manualCheckText = value;
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

    private string? CurrentReviewTargetPath => _manualCheckFilePaths.FirstOrDefault();

    private bool IsManualCheckApproved => !RequiresManualCheck
        || string.Equals(_approvedReviewPath, CurrentReviewTargetPath, StringComparison.OrdinalIgnoreCase);

    private async Task<bool> ApplyAutoDetectedFilesAsync(
        string selectedVideoPath,
        IReadOnlyCollection<string>? excludedSourcePaths = null)
    {
        try
        {
            SetBusy(true);
            SetStatus("Dateien werden erkannt...", 0);
            PreviewText = "Erkennung laeuft...";

            var detected = await _services.SeriesEpisodeMux.DetectFromSelectedVideoAsync(
                selectedVideoPath,
                HandleDetectionUpdate,
                excludedSourcePaths);

            _detectionSeedPath = selectedVideoPath;
            MainVideoPath = detected.MainVideoPath;
            AudioDescriptionPath = detected.AudioDescriptionPath ?? string.Empty;
            _subtitlePaths = detected.SubtitlePaths.ToList();
            _attachmentPaths = detected.AttachmentPaths.ToList();
            OutputPath = detected.SuggestedOutputFilePath;
            _currentPlan = null;

            if (string.IsNullOrWhiteSpace(Title) || Title == _lastSuggestedTitle)
            {
                Title = detected.SuggestedTitle;
            }

            _lastSuggestedTitle = detected.SuggestedTitle;
            ApplyManualCheckState(detected);
            OnPropertyChanged(nameof(SubtitleDisplayText));
            OnPropertyChanged(nameof(AttachmentDisplayText));

            PreviewText = BuildDetectionPreview(detected);
            SetStatus("Dateien erkannt", 100);
            RefreshCommands();
            return true;
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(ex.Message);
            SetStatus("Fehler", 0);
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void HandleDetectionUpdate(DetectionProgressUpdate update)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            SetStatus(update.StatusText, update.ProgressPercent);
            PreviewText = $"{update.StatusText}{Environment.NewLine}{Environment.NewLine}Bitte warten...";
        });
    }

    private static string BuildDetectionPreview(AutoDetectedEpisodeFiles detected)
    {
        var lines = new List<string>
        {
            "Dateien wurden automatisch erkannt. Mit 'Vorschau erzeugen' kannst du den mkvmerge-Aufruf pruefen.",
            $"Hauptquelle: {Path.GetFileName(detected.MainVideoPath)}"
        };

        if (detected.AdditionalVideoPaths.Count > 0)
        {
            lines.Add("Weitere Videospuren: " + string.Join(", ", detected.AdditionalVideoPaths.Select(Path.GetFileName)));
        }

        if (detected.AttachmentPaths.Count > 0)
        {
            lines.Add("Anhaenge: " + string.Join(", ", detected.AttachmentPaths.Select(Path.GetFileName)));
        }

        if (detected.Notes.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Hinweise:");
            lines.AddRange(detected.Notes.Select(note => "- " + note));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void ApplyManualCheckState(AutoDetectedEpisodeFiles detected)
    {
        _manualCheckFilePaths = detected.ManualCheckFilePaths.ToList();
        RequiresManualCheck = detected.RequiresManualCheck;
        if (!RequiresManualCheck)
        {
            _approvedReviewPath = null;
        }
        else if (!string.Equals(_approvedReviewPath, CurrentReviewTargetPath, StringComparison.OrdinalIgnoreCase))
        {
            _approvedReviewPath = null;
        }

        ManualCheckText = detected.RequiresManualCheck
            ? "Die aktuell ausgewaehlte Quelle ist pruefpflichtig. Bitte vor dem Muxen kurz pruefen und freigeben."
            : string.Empty;
    }

    private async Task RescanFromMainVideoAsync()
    {
        if (string.IsNullOrWhiteSpace(_mainVideoPath))
        {
            _dialogService.ShowError("Bitte zuerst ein Hauptvideo auswaehlen.");
            return;
        }

        await ApplyAutoDetectedFilesAsync(_mainVideoPath, _excludedSourcePaths);
    }

    private void SetAudioDescription(string path)
    {
        AudioDescriptionPath = path;
        _currentPlan = null;
        _approvedReviewPath = null;
    }

    private void ClearAudioDescription()
    {
        AudioDescriptionPath = string.Empty;
        _currentPlan = null;
        _approvedReviewPath = null;
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

    private void SetAttachments(IEnumerable<string> paths)
    {
        _attachmentPaths = paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        _currentPlan = null;
        OnPropertyChanged(nameof(AttachmentDisplayText));
    }

    private void ClearAttachments()
    {
        _attachmentPaths = [];
        _currentPlan = null;
        OnPropertyChanged(nameof(AttachmentDisplayText));
    }

    private void SetOutputPath(string path)
    {
        OutputPath = path;
        _currentPlan = null;
    }

    private async Task SelectMainVideoAsync()
    {
        var path = _dialogService.SelectMainVideo(ResolveMainVideoInitialDirectory());
        if (!string.IsNullOrWhiteSpace(path))
        {
            _excludedSourcePaths.Clear();
            _approvedReviewPath = null;
            await ApplyAutoDetectedFilesAsync(path, _excludedSourcePaths);
        }
    }

    private async Task SelectAudioDescriptionAsync()
    {
        if (string.IsNullOrWhiteSpace(_mainVideoPath))
        {
            var selectedAudioDescriptionPath = _dialogService.SelectAudioDescription(ResolveMainVideoInitialDirectory());
            if (!string.IsNullOrWhiteSpace(selectedAudioDescriptionPath))
            {
                _excludedSourcePaths.Clear();
                _approvedReviewPath = null;
                await ApplyAutoDetectedFilesAsync(selectedAudioDescriptionPath, _excludedSourcePaths);
            }

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
        if (string.IsNullOrWhiteSpace(_mainVideoPath))
        {
            _dialogService.ShowError("Bitte zuerst ein Hauptvideo auswaehlen.");
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

    private void SelectAttachments()
    {
        if (string.IsNullOrWhiteSpace(_mainVideoPath))
        {
            _dialogService.ShowError("Bitte zuerst ein Hauptvideo auswaehlen.");
            return;
        }

        var choice = _dialogService.AskAttachmentChoice();
        if (choice == MessageBoxResult.No)
        {
            ClearAttachments();
            return;
        }

        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        var paths = _dialogService.SelectAttachments(ResolveComponentInitialDirectory());
        if (paths is not null)
        {
            SetAttachments(paths);
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

    private void TestSelectedSources()
    {
        _ = ReviewSourcesAsync();
    }

    private async Task ReviewSourcesAsync()
    {
        while (RequiresManualCheck && !string.IsNullOrWhiteSpace(CurrentReviewTargetPath))
        {
            var reviewTargetPath = CurrentReviewTargetPath!;
            SetStatus("Pruefe Quelle...", ProgressValue);
            _dialogService.OpenFilesWithDefaultApp([reviewTargetPath]);

            var result = _dialogService.AskSourceReviewResult(
                Path.GetFileName(reviewTargetPath),
                canTryAlternative: true);

            if (result == MessageBoxResult.Cancel)
            {
                SetStatus("Quellenpruefung abgebrochen", ProgressValue);
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                _approvedReviewPath = reviewTargetPath;
                SetStatus("Quelle freigegeben", 100);
                return;
            }

            var detectionSeedPath = _detectionSeedPath ?? _mainVideoPath;
            if (string.IsNullOrWhiteSpace(detectionSeedPath))
            {
                _dialogService.ShowWarning("Hinweis", "Es konnte keine alternative Quelle ermittelt werden.");
                return;
            }

            var tentativeExclusions = new HashSet<string>(_excludedSourcePaths, StringComparer.OrdinalIgnoreCase)
            {
                reviewTargetPath
            };

            _approvedReviewPath = null;
            var updated = await ApplyAutoDetectedFilesAsync(detectionSeedPath, tentativeExclusions);
            if (!updated)
            {
                return;
            }

            _excludedSourcePaths.Clear();
            foreach (var excludedPath in tentativeExclusions)
            {
                _excludedSourcePaths.Add(excludedPath);
            }

            if (!RequiresManualCheck)
            {
                SetStatus("Auf alternative Quelle umgestellt", 100);
                return;
            }
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

            if (RequiresManualCheck)
            {
                if (!IsManualCheckApproved)
                {
                    _dialogService.ShowWarning("Hinweis", "Diese Episode nutzt eine pruefpflichtige Quelle. Bitte zuerst 'Quelle pruefen' ausfuehren und die Quelle freigeben.");
                    SetStatus("Freigabe der Quelle fehlt", 0);
                    return;
                }
            }

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
            _attachmentPaths,
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
        TestSelectedSourcesCommand.RaiseCanExecuteChanged();
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
