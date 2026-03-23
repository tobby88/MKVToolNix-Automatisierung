using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels.Commands;
using MkvToolnixAutomatisierung.ViewModels.Modules;

namespace MkvToolnixAutomatisierung.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private ModuleNavigationItem _selectedModule;
    private readonly UserDialogService _dialogService;
    private readonly AppToolPathStore _toolPathStore;
    private readonly FfprobeLocator _ffprobeLocator;
    private readonly MkvToolNixLocator _mkvToolNixLocator;
    private bool _isFfprobeAvailable;
    private string? _ffprobePath;
    private bool _isMkvToolNixAvailable;
    private string? _mkvMergePath;

    public MainWindowViewModel(
        IReadOnlyList<ModuleNavigationItem> modules,
        UserDialogService dialogService,
        AppToolPathStore toolPathStore,
        FfprobeLocator ffprobeLocator,
        MkvToolNixLocator mkvToolNixLocator)
    {
        _dialogService = dialogService;
        _toolPathStore = toolPathStore;
        _ffprobeLocator = ffprobeLocator;
        _mkvToolNixLocator = mkvToolNixLocator;
        Modules = new ObservableCollection<ModuleNavigationItem>(modules);
        _selectedModule = Modules.First();
        SelectFfprobeCommand = new RelayCommand(SelectFfprobePath);
        SelectMkvToolNixCommand = new RelayCommand(SelectMkvToolNixDirectory);
        RefreshToolStatus();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ModuleNavigationItem> Modules { get; }

    public bool IsFfprobeAvailable
    {
        get => _isFfprobeAvailable;
        private set
        {
            if (_isFfprobeAvailable == value)
            {
                return;
            }

            _isFfprobeAvailable = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MediaProbeStatusText));
            OnPropertyChanged(nameof(MediaProbeStatusTooltip));
        }
    }

    public string? FfprobePath
    {
        get => _ffprobePath;
        private set
        {
            if (_ffprobePath == value)
            {
                return;
            }

            _ffprobePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MediaProbeStatusTooltip));
        }
    }

    public bool IsMkvToolNixAvailable
    {
        get => _isMkvToolNixAvailable;
        private set
        {
            if (_isMkvToolNixAvailable == value)
            {
                return;
            }

            _isMkvToolNixAvailable = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MkvToolNixStatusText));
            OnPropertyChanged(nameof(MkvToolNixStatusTooltip));
        }
    }

    public string? MkvMergePath
    {
        get => _mkvMergePath;
        private set
        {
            if (_mkvMergePath == value)
            {
                return;
            }

            _mkvMergePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MkvToolNixStatusTooltip));
        }
    }

    public string MediaProbeStatusText => IsFfprobeAvailable
        ? "Laufzeiten: ffprobe"
        : "Laufzeiten: Windows-Fallback";

    public string MediaProbeStatusTooltip => IsFfprobeAvailable
        ? $"ffprobe gefunden und aktiv:{Environment.NewLine}{FfprobePath}"
        : "ffprobe wurde nicht gefunden. Klicken, um ffprobe.exe auszuwählen. Bis dahin nutzt die App für Laufzeiten den Windows-Fallback.";

    public string MkvToolNixStatusText => IsMkvToolNixAvailable
        ? "mkvmerge: bereit"
        : "mkvmerge: fehlt";

    public string MkvToolNixStatusTooltip => IsMkvToolNixAvailable
        ? $"mkvmerge gefunden:{Environment.NewLine}{MkvMergePath}"
        : "mkvmerge wurde nicht gefunden. Klicken, um den MKVToolNix-Ordner auszuwählen.";

    public RelayCommand SelectFfprobeCommand { get; }

    public RelayCommand SelectMkvToolNixCommand { get; }

    public ModuleNavigationItem SelectedModule
    {
        get => _selectedModule;
        set
        {
            if (_selectedModule == value)
            {
                return;
            }

            _selectedModule = value;
            OnPropertyChanged();
        }
    }

    public static MainWindowViewModel CreateDefault()
    {
        var dialogService = new UserDialogService();
        var toolPathStore = new AppToolPathStore();
        var locator = new MkvToolNixLocator(toolPathStore);
        var ffprobeLocator = new FfprobeLocator(toolPathStore);
        var probeService = new MkvMergeProbeService();
        var archiveService = new SeriesArchiveService(probeService);
        var ffprobeDurationProbe = new FfprobeDurationProbe(ffprobeLocator);
        var durationProbe = new PreferredMediaDurationProbe(
            ffprobeDurationProbe,
            new WindowsMediaDurationProbe());
        var planner = new SeriesEpisodeMuxPlanner(locator, probeService, archiveService, durationProbe);
        var executionService = new MuxExecutionService();
        var outputParser = new MkvMergeOutputParser();
        var muxService = new SeriesEpisodeMuxService(planner, executionService, outputParser);
        var fileCopyService = new FileCopyService();
        var cleanupService = new EpisodeCleanupService();
        var metadataStore = new AppMetadataStore();
        var tvdbClient = new TvdbClient();
        var metadataLookupService = new EpisodeMetadataLookupService(metadataStore, tvdbClient);
        var appServices = new AppServices(muxService, metadataLookupService, fileCopyService, cleanupService);

        var singleEpisode = new SingleEpisodeMuxViewModel(appServices, dialogService);
        var batch = new BatchMuxViewModel(appServices, dialogService);

        return new MainWindowViewModel(
            [
                new ModuleNavigationItem(
                    "Einzelepisode",
                    "Erkennen, prüfen, muxen",
                    singleEpisode),
                new ModuleNavigationItem(
                    "Batch",
                    "Ordner scannen und gesammelt muxen",
                    batch)
            ],
            dialogService,
            toolPathStore,
            ffprobeLocator,
            locator);
    }

    private void SelectFfprobePath()
    {
        var initialDirectory = GetInitialDirectory(FfprobePath);
        var selectedPath = _dialogService.SelectExecutable(
            "ffprobe.exe auswählen",
            "ffprobe.exe|ffprobe.exe|Ausführbare Dateien (*.exe)|*.exe",
            initialDirectory);

        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        var settings = _toolPathStore.Load();
        settings.FfprobePath = selectedPath;
        _toolPathStore.Save(settings);
        RefreshToolStatus();
    }

    private void SelectMkvToolNixDirectory()
    {
        var initialDirectory = GetInitialDirectory(MkvMergePath);
        var selectedDirectory = _dialogService.SelectFolder(
            "MKVToolNix-Ordner auswählen",
            initialDirectory);

        if (string.IsNullOrWhiteSpace(selectedDirectory))
        {
            return;
        }

        var settings = _toolPathStore.Load();
        settings.MkvToolNixDirectoryPath = selectedDirectory;
        _toolPathStore.Save(settings);
        RefreshToolStatus();
    }

    private void RefreshToolStatus()
    {
        var settings = _toolPathStore.Load();
        var ffprobePath = _ffprobeLocator.TryFindFfprobePath();
        FfprobePath = ffprobePath;
        IsFfprobeAvailable = !string.IsNullOrWhiteSpace(ffprobePath) && File.Exists(ffprobePath);

        string? mkvMergePath;
        try
        {
            mkvMergePath = _mkvToolNixLocator.FindMkvMergePath();
        }
        catch
        {
            mkvMergePath = null;
        }

        MkvMergePath = mkvMergePath;
        IsMkvToolNixAvailable = !string.IsNullOrWhiteSpace(mkvMergePath) && File.Exists(mkvMergePath);

        var normalizedFfprobePath = ffprobePath ?? string.Empty;
        var normalizedMkvToolPath = string.IsNullOrWhiteSpace(mkvMergePath)
            ? string.Empty
            : Path.GetDirectoryName(mkvMergePath) ?? mkvMergePath;

        if (!string.Equals(settings.FfprobePath, normalizedFfprobePath, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(settings.MkvToolNixDirectoryPath, normalizedMkvToolPath, StringComparison.OrdinalIgnoreCase))
        {
            settings.FfprobePath = normalizedFfprobePath;
            settings.MkvToolNixDirectoryPath = normalizedMkvToolPath;
            _toolPathStore.Save(settings);
        }
    }

    private static string GetInitialDirectory(string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            if (Directory.Exists(filePath))
            {
                return filePath;
            }

            var parent = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            {
                return parent;
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record ModuleNavigationItem(string Title, string Description, object ContentViewModel);
