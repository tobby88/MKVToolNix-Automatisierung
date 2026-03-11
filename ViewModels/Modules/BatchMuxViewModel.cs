using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.ViewModels.Commands;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

public sealed class BatchMuxViewModel : INotifyPropertyChanged
{
    private const string DefaultSourceDirectory = @"C:\Users\tobby\Downloads\MediathekView-latest-win\Downloads";

    private readonly AppServices _services;
    private readonly UserDialogService _dialogService;
    private readonly SeriesEpisodeMuxPlanner _planner;

    private string _sourceDirectory = string.Empty;
    private string _outputDirectory = string.Empty;
    private string _statusText = "Bereit";
    private string _logText = string.Empty;
    private int _progressValue;
    private bool _isBusy;

    public BatchMuxViewModel(AppServices services, UserDialogService dialogService)
    {
        _services = services;
        _dialogService = dialogService;
        _planner = new SeriesEpisodeMuxPlanner(_services.Locator, _services.ProbeService);

        SelectSourceDirectoryCommand = new RelayCommand(SelectSourceDirectory, () => !_isBusy);
        SelectOutputDirectoryCommand = new RelayCommand(SelectOutputDirectory, () => !_isBusy && !string.IsNullOrWhiteSpace(SourceDirectory));
        ScanDirectoryCommand = new AsyncRelayCommand(ScanDirectoryAsync, () => !_isBusy && !string.IsNullOrWhiteSpace(SourceDirectory));
        RunBatchCommand = new AsyncRelayCommand(RunBatchAsync, () => !_isBusy && EpisodeItems.Count > 0);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand SelectSourceDirectoryCommand { get; }
    public RelayCommand SelectOutputDirectoryCommand { get; }
    public AsyncRelayCommand ScanDirectoryCommand { get; }
    public AsyncRelayCommand RunBatchCommand { get; }

    public ObservableCollection<BatchEpisodeItemViewModel> EpisodeItems { get; } = [];

    public string SourceDirectory
    {
        get => _sourceDirectory;
        private set
        {
            _sourceDirectory = value;
            OnPropertyChanged();
        }
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        private set
        {
            _outputDirectory = value;
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

    public string LogText
    {
        get => _logText;
        private set
        {
            _logText = value;
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

    private void SelectSourceDirectory()
    {
        var initialDirectory = Directory.Exists(SourceDirectory) ? SourceDirectory : DefaultSourceDirectory;
        var path = _dialogService.SelectFolder("Quellordner fuer den Batch auswaehlen", initialDirectory);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        SourceDirectory = path;
        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            OutputDirectory = path;
        }

        LogText = string.Empty;
        EpisodeItems.Clear();
        StatusText = "Ordner gewaehlt";
        RefreshCommands();
    }

    private void SelectOutputDirectory()
    {
        var initialDirectory = Directory.Exists(OutputDirectory) ? OutputDirectory : SourceDirectory;
        var path = _dialogService.SelectFolder("Ausgabeordner fuer den Batch auswaehlen", initialDirectory);
        if (!string.IsNullOrWhiteSpace(path))
        {
            OutputDirectory = path;
        }
    }

    private async Task ScanDirectoryAsync()
    {
        try
        {
            SetBusy(true);
            EpisodeItems.Clear();
            LogText = string.Empty;
            SetStatus("Scanne Ordner...", 0);

            var mainVideoFiles = Directory.GetFiles(SourceDirectory, "*.mp4")
                .Where(file => !LooksLikeAudioDescription(file))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var total = mainVideoFiles.Count;
            for (var index = 0; index < total; index++)
            {
                var file = mainVideoFiles[index];
                try
                {
                    var detected = _planner.DetectFromMainVideo(file);
                    var outputPath = Path.Combine(OutputDirectory, Path.GetFileName(detected.SuggestedOutputFilePath));
                    EpisodeItems.Add(new BatchEpisodeItemViewModel(
                        detected.SuggestedTitle,
                        Path.GetFileName(file),
                        file,
                        detected.AudioDescriptionPath,
                        detected.SubtitlePaths.ToList(),
                        detected.AttachmentPath,
                        outputPath,
                        detected.SuggestedTitle,
                        "Bereit"));
                    AppendLog($"OK: {Path.GetFileName(file)}");
                }
                catch (Exception ex)
                {
                    EpisodeItems.Add(new BatchEpisodeItemViewModel(
                        Path.GetFileNameWithoutExtension(file),
                        Path.GetFileName(file),
                        file,
                        null,
                        [],
                        null,
                        string.Empty,
                        Path.GetFileNameWithoutExtension(file),
                        "Fehler"));
                    AppendLog($"FEHLER: {Path.GetFileName(file)} -> {ex.Message}");
                }

                SetStatus($"Scanne Ordner... {index + 1}/{total}", CalculatePercent(index + 1, total));
                await Task.Yield();
            }

            SetStatus($"Scan abgeschlossen: {EpisodeItems.Count} Eintraege", 100);
            RefreshCommands();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RunBatchAsync()
    {
        var readyItems = EpisodeItems.Where(item => item.Status != "Fehler").ToList();
        if (readyItems.Count == 0)
        {
            _dialogService.ShowWarning("Hinweis", "Es gibt keine gueltigen Episoden fuer den Batch.");
            return;
        }

        if (!_dialogService.ConfirmBatchStart(readyItems.Count))
        {
            SetStatus("Abgebrochen", 0);
            return;
        }

        try
        {
            SetBusy(true);
            LogText = string.Empty;

            for (var index = 0; index < readyItems.Count; index++)
            {
                var item = readyItems[index];
                item.Status = "Laeuft";
                AppendLog($"STARTE: {item.MainVideoFileName}");

                try
                {
                    var request = new SeriesEpisodeMuxRequest(
                        item.MainVideoPath,
                        item.AudioDescriptionPath,
                        item.SubtitlePaths,
                        item.AttachmentPath,
                        item.OutputPath,
                        item.TitleForMux);

                    var plan = await _planner.CreatePlanAsync(request);
                    var exitCode = await _services.ExecutionService.ExecuteAsync(plan.MkvMergePath, plan.BuildArguments(), line => AppendLog($"  {line}"));

                    if (exitCode == 0)
                    {
                        item.Status = "Erfolgreich";
                    }
                    else if (exitCode == 1 && File.Exists(item.OutputPath))
                    {
                        item.Status = "Warnung";
                    }
                    else
                    {
                        item.Status = $"Fehler ({exitCode})";
                    }
                }
                catch (Exception ex)
                {
                    item.Status = "Fehler";
                    AppendLog($"  FEHLER: {ex.Message}");
                }

                SetStatus($"Batch laeuft... {index + 1}/{readyItems.Count}", CalculatePercent(index + 1, readyItems.Count));
            }

            SetStatus("Batch abgeschlossen", 100);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static bool LooksLikeAudioDescription(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        return fileName.Contains("audiodeskrip", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(fileName, @"(?:^|[^a-z])AD(?:[^a-z]|$)", RegexOptions.IgnoreCase);
    }

    private static int CalculatePercent(int current, int total)
    {
        return total <= 0 ? 0 : (int)Math.Round(current * 100d / total);
    }

    private void AppendLog(string line)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            LogText += line + Environment.NewLine;
            return;
        }

        Application.Current.Dispatcher.Invoke(() => LogText += line + Environment.NewLine);
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        SelectSourceDirectoryCommand.RaiseCanExecuteChanged();
        SelectOutputDirectoryCommand.RaiseCanExecuteChanged();
        ScanDirectoryCommand.RaiseCanExecuteChanged();
        RunBatchCommand.RaiseCanExecuteChanged();
    }

    private void SetStatus(string text, int progress)
    {
        StatusText = text;
        ProgressValue = Math.Clamp(progress, 0, 100);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class BatchEpisodeItemViewModel : INotifyPropertyChanged
{
    private string _status;

    public BatchEpisodeItemViewModel(
        string title,
        string mainVideoFileName,
        string mainVideoPath,
        string? audioDescriptionPath,
        IReadOnlyList<string> subtitlePaths,
        string? attachmentPath,
        string outputPath,
        string titleForMux,
        string status)
    {
        Title = title;
        MainVideoFileName = mainVideoFileName;
        MainVideoPath = mainVideoPath;
        AudioDescriptionPath = audioDescriptionPath;
        SubtitlePaths = subtitlePaths;
        AttachmentPath = attachmentPath;
        OutputPath = outputPath;
        TitleForMux = titleForMux;
        _status = status;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }
    public string MainVideoFileName { get; }
    public string MainVideoPath { get; }
    public string? AudioDescriptionPath { get; }
    public IReadOnlyList<string> SubtitlePaths { get; }
    public string? AttachmentPath { get; }
    public string OutputPath { get; }
    public string TitleForMux { get; }

    public string Status
    {
        get => _status;
        set
        {
            if (_status == value)
            {
                return;
            }

            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }
}