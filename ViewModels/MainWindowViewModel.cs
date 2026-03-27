using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.ViewModels.Commands;
using MkvToolnixAutomatisierung.ViewModels.Modules;

namespace MkvToolnixAutomatisierung.ViewModels;

/// <summary>
/// Verwaltet Modulnavigation und globale Status-/Konfigurationsaktionen im Hauptfenster.
/// </summary>
public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private ModuleNavigationItem _selectedModule;
    private readonly AppServices _services;
    private readonly UserDialogService _dialogService;
    private readonly AppToolPathStore _toolPathStore;
    private readonly FfprobeLocator _ffprobeLocator;
    private readonly MkvToolNixLocator _mkvToolNixLocator;
    private bool _isFfprobeAvailable;
    private string? _ffprobePath;
    private bool _isMkvToolNixAvailable;
    private string? _mkvMergePath;
    private bool _isArchiveAvailable;
    private string? _archiveRootDirectory;

    public MainWindowViewModel(
        IReadOnlyList<ModuleNavigationItem> modules,
        AppServices services,
        UserDialogService dialogService,
        AppToolPathStore toolPathStore,
        FfprobeLocator ffprobeLocator,
        MkvToolNixLocator mkvToolNixLocator)
    {
        _services = services;
        _dialogService = dialogService;
        _toolPathStore = toolPathStore;
        _ffprobeLocator = ffprobeLocator;
        _mkvToolNixLocator = mkvToolNixLocator;
        Modules = new ObservableCollection<ModuleNavigationItem>(modules);
        _selectedModule = Modules.First();
        SelectFfprobeCommand = new RelayCommand(SelectFfprobePath);
        SelectMkvToolNixCommand = new RelayCommand(SelectMkvToolNixDirectory);
        SelectArchiveRootCommand = new RelayCommand(SelectArchiveRootDirectory);
        RefreshToolStatus();
        RefreshArchiveStatus();
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

    public bool IsArchiveAvailable
    {
        get => _isArchiveAvailable;
        private set
        {
            if (_isArchiveAvailable == value)
            {
                return;
            }

            _isArchiveAvailable = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ArchiveStatusText));
            OnPropertyChanged(nameof(ArchiveStatusTooltip));
        }
    }

    public string? ArchiveRootDirectory
    {
        get => _archiveRootDirectory;
        private set
        {
            if (_archiveRootDirectory == value)
            {
                return;
            }

            _archiveRootDirectory = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ArchiveStatusTooltip));
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

    public string ArchiveStatusText => IsArchiveAvailable
        ? "Archiv: bereit"
        : "Archiv: fehlt";

    public string ArchiveStatusTooltip => IsArchiveAvailable
        ? $"Serienbibliothek gefunden:{Environment.NewLine}{ArchiveRootDirectory}{Environment.NewLine}{Environment.NewLine}Klicken, um den Standardpfad zu ändern."
        : $"Konfigurierte Serienbibliothek nicht erreichbar:{Environment.NewLine}{ArchiveRootDirectory}{Environment.NewLine}{Environment.NewLine}Klicken, um den Standardpfad zu ändern.";

    public string PortableModeText => "Portable: lokale Daten in .\\Data";

    public string PortableModeTooltip => $"Portable Modus aktiv:{Environment.NewLine}{PortableAppStorage.DataDirectory}";

    public string QuickHelpText
    {
        get
        {
            var moduleHint = string.Equals(SelectedModule.Title, "Batch", StringComparison.Ordinal)
                ? "Batch: Quellordner wählen, scannen, offene Pflichtchecks klären, dann Batch starten."
                : "Einzelepisode: Hauptvideo wählen, Erkennung prüfen, bei Bedarf TVDB öffnen, Vorschau erzeugen, dann muxen.";

            return string.Join(
                Environment.NewLine,
                "Erststart:",
                "1. Prüfen, ob mkvmerge links unten bereit ist.",
                "2. Optional ffprobe auswählen, falls genauere Laufzeiten gewünscht sind.",
                "3. Serienbibliothek links unten bei Bedarf anpassen.",
                "4. TVDB nur einrichten, wenn Metadaten geprüft oder verbessert werden sollen.",
                string.Empty,
                "Aktuelles Modul:",
                moduleHint);
        }
    }

    public RelayCommand SelectFfprobeCommand { get; }

    public RelayCommand SelectMkvToolNixCommand { get; }

    public RelayCommand SelectArchiveRootCommand { get; }

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
            OnPropertyChanged(nameof(QuickHelpText));
        }
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
        _services.SeriesEpisodeMux.InvalidatePlanningCaches();
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
        _services.SeriesEpisodeMux.InvalidatePlanningCaches();
        RefreshToolStatus();
    }

    private void SelectArchiveRootDirectory()
    {
        var initialDirectory = GetInitialDirectory(ArchiveRootDirectory);
        var selectedDirectory = _dialogService.SelectFolder(
            "Standard-Serienbibliothek auswählen",
            initialDirectory);

        if (string.IsNullOrWhiteSpace(selectedDirectory))
        {
            return;
        }

        _services.Archive.ConfigureArchiveRootDirectory(selectedDirectory);
        _services.SeriesEpisodeMux.InvalidatePlanningCaches();
        RefreshArchiveStatus();
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

    private void RefreshArchiveStatus()
    {
        ArchiveRootDirectory = _services.Archive.ArchiveRootDirectory;
        IsArchiveAvailable = _services.Archive.IsArchiveAvailable();
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

/// <summary>
/// Beschreibt einen auswählbaren Eintrag in der linken Modulnavigation.
/// </summary>
public sealed record ModuleNavigationItem(string Title, string Description, object ContentViewModel);
