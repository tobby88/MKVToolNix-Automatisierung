using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.ViewModels.Commands;
using MkvToolnixAutomatisierung.ViewModels.Modules;

namespace MkvToolnixAutomatisierung.ViewModels;

/// <summary>
/// Verwaltet Modulnavigation und kompakte globale Statusanzeige des Hauptfensters.
/// </summary>
internal sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private ModuleNavigationItem _selectedModule;
    private readonly MainWindowModuleServices _services;
    private bool _isFfprobeAvailable;
    private string? _ffprobePath;
    private string? _ffprobeStatusDetail;
    private bool _isMkvToolNixAvailable;
    private string? _mkvMergePath;
    private string? _mkvPropEditPath;
    private string? _mkvToolNixStatusDetail;
    private bool _isArchiveAvailable;
    private string? _archiveRootDirectory;

    public MainWindowViewModel(
        IReadOnlyList<ModuleNavigationItem> modules,
        MainWindowModuleServices services)
    {
        _services = services;
        Modules = new ObservableCollection<ModuleNavigationItem>(modules);
        _selectedModule = Modules.First();
        OpenSettingsCommand = new RelayCommand(OpenSettingsDialog);
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
            OnPropertyChanged(nameof(SystemStatusSummary));
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
            OnPropertyChanged(nameof(SystemStatusSummary));
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
            OnPropertyChanged(nameof(HasArchiveNotice));
            OnPropertyChanged(nameof(ArchiveNoticeText));
            OnPropertyChanged(nameof(SystemStatusSummary));
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
            OnPropertyChanged(nameof(ArchiveNoticeText));
        }
    }

    public string MediaProbeStatusText => IsFfprobeAvailable
        ? "Laufzeiten: ffprobe"
        : "Laufzeiten: Windows-Fallback";

    public string MediaProbeStatusTooltip => IsFfprobeAvailable
        ? $"ffprobe gefunden und aktiv:{Environment.NewLine}{FfprobePath}"
        : string.IsNullOrWhiteSpace(FfprobePath)
            ? "ffprobe wurde nicht gefunden. Im Einstellungsdialog kann bei Bedarf ein expliziter Pfad gesetzt werden."
            : $"ffprobe aktuell nicht verfügbar:{Environment.NewLine}{FfprobePath}{Environment.NewLine}{Environment.NewLine}{_ffprobeStatusDetail}";

    public string MkvToolNixStatusText => IsMkvToolNixAvailable
        ? "MKVToolNix: bereit"
        : "MKVToolNix: fehlt";

    public string MkvToolNixStatusTooltip => IsMkvToolNixAvailable
        ? $"MKVToolNix gefunden:{Environment.NewLine}{Path.GetDirectoryName(MkvMergePath ?? string.Empty) ?? MkvMergePath}{Environment.NewLine}{Environment.NewLine}mkvmerge.exe:{Environment.NewLine}{MkvMergePath}{Environment.NewLine}{Environment.NewLine}mkvpropedit.exe:{Environment.NewLine}{_mkvPropEditPath}"
        : string.IsNullOrWhiteSpace(MkvMergePath)
            ? "MKVToolNix wurde nicht gefunden. Im Einstellungsdialog kann bei Bedarf ein Ordner gesetzt werden."
            : $"MKVToolNix aktuell nicht vollständig verfügbar:{Environment.NewLine}{MkvMergePath}{Environment.NewLine}{Environment.NewLine}{_mkvToolNixStatusDetail}";

    public string ArchiveStatusText => IsArchiveAvailable
        ? "Archiv: bereit"
        : "Archiv: fehlt";

    public string ArchiveStatusTooltip => IsArchiveAvailable
        ? $"Serienbibliothek gefunden:{Environment.NewLine}{ArchiveRootDirectory}"
        : $"Konfigurierte Serienbibliothek nicht erreichbar:{Environment.NewLine}{ArchiveRootDirectory}";

    public bool HasArchiveNotice => !IsArchiveAvailable;

    public string ArchiveNoticeText => IsArchiveAvailable
        ? string.Empty
        : "Serienbibliothek aktuell nicht erreichbar."
            + Environment.NewLine
            + "Automatische Ausgabepfade nutzen deshalb vorerst den jeweiligen Quellordner.";

    public string PortableModeText => "Portable: lokale Daten in .\\Data";

    public string PortableModeTooltip => $"Portable Modus aktiv:{Environment.NewLine}{PortableAppStorage.DataDirectory}";

    public string SystemStatusSummary => string.Join(
        Environment.NewLine,
        ArchiveStatusText,
        MkvToolNixStatusText,
        MediaProbeStatusText);

    public string QuickHelpText
    {
        get
        {
            var moduleHint = SelectedModule.Title switch
            {
                "Batch-Mux" => "Batch-Mux: Quellordner wählen, scannen, offene Pflichtchecks klären, dann Batch starten.",
                "Einsortieren" => "Einsortieren: MediathekView-Ordner scannen, Zielordner prüfen und lose Dateien gesammelt einsortieren.",
                "Emby-Abgleich" => "Emby-Abgleich: Reports wählen, automatische NFO-/Emby-Prüfung abwarten, Emby bei Bedarf scannen, TVDB je Zeile korrigieren und erst danach die Änderungen nach Emby schreiben.",
                _ => "Einzel-Mux: Hauptvideo wählen, Erkennung prüfen, bei Bedarf TVDB öffnen, Vorschau erzeugen, dann muxen."
            };

            return string.Join(
                Environment.NewLine,
                "Erststart:",
                "1. Selten geänderte Pfade und API-Schlüssel über 'Einstellungen' hinterlegen.",
                "2. Archiv- und Toolstatus darunter kurz prüfen.",
                "3. TVDB nur einrichten, wenn Metadaten geprüft oder verbessert werden sollen.",
                string.Empty,
                "Aktuelles Modul:",
                moduleHint);
        }
    }

    public RelayCommand OpenSettingsCommand { get; }

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

    /// <summary>
    /// Übernimmt eine neue globale Archivwurzel und stößt alle betroffenen Module zu einer Aktualisierung an.
    /// </summary>
    /// <param name="archiveRootDirectory">Neu ausgewählter Standardpfad der Serienbibliothek.</param>
    public void UpdateArchiveRootDirectory(string archiveRootDirectory)
    {
        if (string.IsNullOrWhiteSpace(archiveRootDirectory))
        {
            return;
        }

        _services.Archive.ConfigureArchiveRootDirectory(archiveRootDirectory);
        RefreshArchiveStatus();
        NotifyArchiveConfigurationChanged();
    }

    private void OpenSettingsDialog()
    {
        var previousArchiveRoot = ArchiveRootDirectory;
        var accepted = _services.SettingsDialog.ShowDialog(
            TryGetSettingsDialogOwner(),
            AppSettingsPage.Archive);
        if (!accepted)
        {
            return;
        }

        RefreshToolStatus();
        RefreshArchiveStatus();
        NotifyGlobalSettingsChanged();
        if (!string.Equals(previousArchiveRoot, ArchiveRootDirectory, StringComparison.OrdinalIgnoreCase))
        {
            NotifyArchiveConfigurationChanged();
        }

        OnPropertyChanged(nameof(SystemStatusSummary));
    }

    private void RefreshToolStatus()
    {
        var settings = _services.ToolPaths.Load();
        var detectedFfprobePath = _services.FfprobeLocator.TryFindFfprobePath();
        var resolvedFfprobePath = !string.IsNullOrWhiteSpace(detectedFfprobePath)
            ? detectedFfprobePath
            : NormalizeConfiguredExecutablePath(settings.FfprobePath, "ffprobe.exe");
        FfprobePath = resolvedFfprobePath;
        IsFfprobeAvailable = !string.IsNullOrWhiteSpace(detectedFfprobePath) && File.Exists(detectedFfprobePath);
        SetFfprobeStatusDetail(BuildFfprobeStatusDetail(settings, detectedFfprobePath));

        string? detectedMkvMergePath = null;
        string? detectedMkvPropEditPath = null;
        Exception? mkvToolNixError = null;
        try
        {
            detectedMkvMergePath = _services.MkvToolNixLocator.FindMkvMergePath();
            detectedMkvPropEditPath = _services.MkvToolNixLocator.FindMkvPropEditPath();
        }
        catch (Exception ex)
        {
            mkvToolNixError = ex;
        }

        var resolvedMkvMergePath = !string.IsNullOrWhiteSpace(detectedMkvMergePath)
            ? detectedMkvMergePath
            : NormalizeConfiguredExecutablePath(settings.MkvToolNixDirectoryPath, "mkvmerge.exe");
        var resolvedMkvPropEditPath = !string.IsNullOrWhiteSpace(detectedMkvPropEditPath)
            ? detectedMkvPropEditPath
            : NormalizeConfiguredExecutablePath(settings.MkvToolNixDirectoryPath, "mkvpropedit.exe");
        MkvMergePath = resolvedMkvMergePath;
        _mkvPropEditPath = resolvedMkvPropEditPath;
        OnPropertyChanged(nameof(MkvToolNixStatusTooltip));
        IsMkvToolNixAvailable = !string.IsNullOrWhiteSpace(detectedMkvMergePath)
            && !string.IsNullOrWhiteSpace(detectedMkvPropEditPath)
            && File.Exists(detectedMkvMergePath)
            && File.Exists(detectedMkvPropEditPath);
        SetMkvToolNixStatusDetail(BuildMkvToolNixStatusDetail(settings, detectedMkvMergePath, mkvToolNixError));
    }

    private void RefreshArchiveStatus()
    {
        ArchiveRootDirectory = _services.Archive.ArchiveRootDirectory;
        IsArchiveAvailable = _services.Archive.IsArchiveAvailable();
    }

    private void NotifyArchiveConfigurationChanged()
    {
        foreach (var module in Modules)
        {
            if (module.ContentViewModel is IArchiveConfigurationAwareModule archiveAwareModule)
            {
                archiveAwareModule.HandleArchiveConfigurationChanged();
            }
        }
    }

    private void NotifyGlobalSettingsChanged()
    {
        foreach (var module in Modules)
        {
            if (module.ContentViewModel is IGlobalSettingsAwareModule settingsAwareModule)
            {
                settingsAwareModule.HandleGlobalSettingsChanged();
            }
        }
    }

    private void SetFfprobeStatusDetail(string detail)
    {
        if (string.Equals(_ffprobeStatusDetail, detail, StringComparison.Ordinal))
        {
            return;
        }

        _ffprobeStatusDetail = detail;
        OnPropertyChanged(nameof(MediaProbeStatusTooltip));
    }

    private void SetMkvToolNixStatusDetail(string detail)
    {
        if (string.Equals(_mkvToolNixStatusDetail, detail, StringComparison.Ordinal))
        {
            return;
        }

        _mkvToolNixStatusDetail = detail;
        OnPropertyChanged(nameof(MkvToolNixStatusTooltip));
    }

    private static string? NormalizeConfiguredExecutablePath(string? configuredPath, string executableName)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        return configuredPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? configuredPath
            : Path.Combine(configuredPath, executableName);
    }

    private static string BuildFfprobeStatusDetail(AppToolPathSettings settings, string? detectedFfprobePath)
    {
        if (!string.IsNullOrWhiteSpace(detectedFfprobePath))
        {
            if (string.Equals(settings.FfprobePath, detectedFfprobePath, StringComparison.OrdinalIgnoreCase))
            {
                return "Aktiv über manuellen Override aus dem Einstellungsdialog.";
            }

            if (string.Equals(settings.ManagedFfprobe.InstalledPath, detectedFfprobePath, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(settings.ManagedFfprobe.InstalledVersion)
                    ? "Automatisch verwaltete ffprobe aktiv."
                    : $"Automatisch verwaltete ffprobe aktiv ({settings.ManagedFfprobe.InstalledVersion}).";
            }

            return "Aktiv über Windows-PATH oder den bisherigen Legacy-Fallback.";
        }

        return string.IsNullOrWhiteSpace(settings.FfprobePath)
            ? "Im Einstellungsdialog kann bei Bedarf ein manueller ffprobe-Override gesetzt werden."
            : "Der manuelle ffprobe-Override ist aktuell nicht verwendbar.";
    }

    private static string BuildMkvToolNixStatusDetail(AppToolPathSettings settings, string? detectedMkvMergePath, Exception? mkvToolNixError)
    {
        if (!string.IsNullOrWhiteSpace(detectedMkvMergePath))
        {
            var resolvedDirectory = Path.GetDirectoryName(detectedMkvMergePath) ?? detectedMkvMergePath;
            if (string.Equals(settings.MkvToolNixDirectoryPath, resolvedDirectory, StringComparison.OrdinalIgnoreCase)
                || string.Equals(settings.MkvToolNixDirectoryPath, detectedMkvMergePath, StringComparison.OrdinalIgnoreCase))
            {
                return "Aktiv über manuellen Override aus dem Einstellungsdialog.";
            }

            if (string.Equals(settings.ManagedMkvToolNix.InstalledPath, resolvedDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(settings.ManagedMkvToolNix.InstalledVersion)
                    ? "Automatisch verwaltetes MKVToolNix aktiv."
                    : $"Automatisch verwaltetes MKVToolNix aktiv ({settings.ManagedMkvToolNix.InstalledVersion}).";
            }

            return "Aktiv über bisherigen Legacy-Fallback.";
        }

        return string.IsNullOrWhiteSpace(settings.MkvToolNixDirectoryPath)
            ? $"Im Einstellungsdialog kann bei Bedarf ein manueller Override gesetzt werden. {mkvToolNixError?.Message ?? string.Empty}".Trim()
            : $"Der manuelle MKVToolNix-Override ist aktuell nicht verwendbar. {mkvToolNixError?.Message ?? string.Empty}".Trim();
    }

    /// <summary>
    /// Liest das aktuelle Hauptfenster nur dann als Dialog-Owner aus, wenn der aufrufende Thread
    /// direkten Zugriff auf das WPF-Application-Objekt hat. In Unit-Tests oder anderen
    /// Neben-Threads ist der Owner optional; ein erzwungener Cross-Thread-Zugriff würde dort
    /// stattdessen eine Ausnahme auslösen.
    /// </summary>
    /// <returns>Aktuelles Hauptfenster oder <see langword="null"/>, wenn kein sicherer Zugriff möglich ist.</returns>
    private static System.Windows.Window? TryGetSettingsDialogOwner()
    {
        var application = System.Windows.Application.Current;
        if (application is null || !application.Dispatcher.CheckAccess())
        {
            return null;
        }

        return application.MainWindow;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Beschreibt einen auswählbaren Eintrag in der linken Modulnavigation.
/// </summary>
internal sealed record ModuleNavigationItem(string Title, string Description, object ContentViewModel);
