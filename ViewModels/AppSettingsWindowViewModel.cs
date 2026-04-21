using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Emby;
using MkvToolnixAutomatisierung.Services.Metadata;

namespace MkvToolnixAutomatisierung.ViewModels;

/// <summary>
/// Zentrales ViewModel für selten geänderte App-Konfiguration wie Archivpfad, Toolpfade, TVDB und Emby.
/// </summary>
internal sealed class AppSettingsWindowViewModel : INotifyPropertyChanged
{
    private readonly AppSettingsModuleServices _services;
    private readonly IUserDialogService _dialogService;
    private string _archiveRootDirectory;
    private string _ffprobePath;
    private string _mkvToolNixDirectoryPath;
    private string _tvdbApiKey;
    private string _tvdbPin;
    private string _embyServerUrl;
    private string _embyApiKey;
    private int _embyScanWaitTimeoutSeconds;
    private string _statusText = "Bereit";
    private bool _isBusy;

    public AppSettingsWindowViewModel(
        AppSettingsModuleServices services,
        IUserDialogService dialogService,
        AppSettingsPage initialPage)
    {
        _services = services;
        _dialogService = dialogService;

        var archiveSettings = _services.Archive.ArchiveRootDirectory;
        var toolSettings = _services.ToolPaths.Load();
        var metadataSettings = _services.EpisodeMetadata.LoadSettings();
        var embySettings = _services.EmbySettings.Load();

        _archiveRootDirectory = archiveSettings;
        _ffprobePath = toolSettings.FfprobePath;
        _mkvToolNixDirectoryPath = toolSettings.MkvToolNixDirectoryPath;
        _tvdbApiKey = metadataSettings.TvdbApiKey;
        _tvdbPin = metadataSettings.TvdbPin;
        _embyServerUrl = embySettings.ServerUrl;
        _embyApiKey = embySettings.ApiKey;
        _embyScanWaitTimeoutSeconds = embySettings.ScanWaitTimeoutSeconds;
        SelectedPage = initialPage;
        RefreshDerivedState();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Meldet dem Hostfenster, dass der Dialog mit Übernehmen geschlossen werden soll.
    /// </summary>
    public event EventHandler<bool>? CloseRequested;

    public AppSettingsPage SelectedPage { get; set; }

    public string ArchiveRootDirectory
    {
        get => _archiveRootDirectory;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            if (_archiveRootDirectory == normalized)
            {
                return;
            }

            _archiveRootDirectory = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsArchiveAvailable));
            OnPropertyChanged(nameof(ArchiveStatusText));
            OnPropertyChanged(nameof(ArchiveStatusTooltip));
        }
    }

    public string FfprobePath
    {
        get => _ffprobePath;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            if (_ffprobePath == normalized)
            {
                return;
            }

            _ffprobePath = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFfprobeAvailable));
            OnPropertyChanged(nameof(FfprobeStatusText));
            OnPropertyChanged(nameof(FfprobeStatusTooltip));
        }
    }

    public string MkvToolNixDirectoryPath
    {
        get => _mkvToolNixDirectoryPath;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            if (_mkvToolNixDirectoryPath == normalized)
            {
                return;
            }

            _mkvToolNixDirectoryPath = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMkvToolNixAvailable));
            OnPropertyChanged(nameof(MkvToolNixStatusText));
            OnPropertyChanged(nameof(MkvToolNixStatusTooltip));
        }
    }

    public string TvdbApiKey
    {
        get => _tvdbApiKey;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            if (_tvdbApiKey == normalized)
            {
                return;
            }

            _tvdbApiKey = normalized;
            OnPropertyChanged();
        }
    }

    public string TvdbPin
    {
        get => _tvdbPin;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            if (_tvdbPin == normalized)
            {
                return;
            }

            _tvdbPin = normalized;
            OnPropertyChanged();
        }
    }

    public string EmbyServerUrl
    {
        get => _embyServerUrl;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            if (_embyServerUrl == normalized)
            {
                return;
            }

            _embyServerUrl = normalized;
            OnPropertyChanged();
        }
    }

    public string EmbyApiKey
    {
        get => _embyApiKey;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            if (_embyApiKey == normalized)
            {
                return;
            }

            _embyApiKey = normalized;
            OnPropertyChanged();
        }
    }

    public int EmbyScanWaitTimeoutSeconds
    {
        get => _embyScanWaitTimeoutSeconds;
        set
        {
            var normalized = Math.Clamp(value, 5, 600);
            if (_embyScanWaitTimeoutSeconds == normalized)
            {
                return;
            }

            _embyScanWaitTimeoutSeconds = normalized;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged();
        }
    }

    public bool IsInteractive => !_isBusy;

    public bool IsArchiveAvailable => !string.IsNullOrWhiteSpace(ArchiveRootDirectory) && Directory.Exists(ArchiveRootDirectory);

    public string ArchiveStatusText => IsArchiveAvailable ? "Archiv bereit" : "Archiv fehlt";

    public string ArchiveStatusTooltip => IsArchiveAvailable
        ? $"Serienbibliothek erreichbar:{Environment.NewLine}{ArchiveRootDirectory}"
        : $"Serienbibliothek aktuell nicht erreichbar:{Environment.NewLine}{ArchiveRootDirectory}";

    public bool IsFfprobeAvailable => File.Exists(FfprobePath);

    public string FfprobeStatusText => IsFfprobeAvailable ? "ffprobe bereit" : "ffprobe fehlt";

    public string FfprobeStatusTooltip => IsFfprobeAvailable
        ? $"ffprobe gefunden:{Environment.NewLine}{FfprobePath}"
        : string.IsNullOrWhiteSpace(FfprobePath)
            ? "Optional. Bleibt das Feld leer, nutzt die App weiterhin den Windows-Fallback."
            : $"ffprobe aktuell nicht verwendbar:{Environment.NewLine}{FfprobePath}";

    public bool IsMkvToolNixAvailable => File.Exists(GetMkvMergePath()) && File.Exists(GetMkvPropEditPath());

    public string MkvToolNixStatusText => IsMkvToolNixAvailable ? "MKVToolNix bereit" : "MKVToolNix unvollständig";

    public string MkvToolNixStatusTooltip => IsMkvToolNixAvailable
        ? $"mkvmerge.exe:{Environment.NewLine}{GetMkvMergePath()}{Environment.NewLine}{Environment.NewLine}mkvpropedit.exe:{Environment.NewLine}{GetMkvPropEditPath()}"
        : string.IsNullOrWhiteSpace(MkvToolNixDirectoryPath)
            ? "Bitte den MKVToolNix-Ordner oder eine mkvmerge.exe angeben."
            : $"Aus dem gewählten Pfad fehlen mkvmerge.exe und/oder mkvpropedit.exe:{Environment.NewLine}{MkvToolNixDirectoryPath}";

    public string SettingsFilePath => PortableAppStorage.SettingsFilePath;

    public void SelectArchiveRootDirectory()
    {
        var selectedDirectory = _dialogService.SelectFolder(
            "Standard-Serienbibliothek auswählen",
            ResolveInitialDirectory(ArchiveRootDirectory));
        if (!string.IsNullOrWhiteSpace(selectedDirectory))
        {
            ArchiveRootDirectory = selectedDirectory;
        }
    }

    public void SelectFfprobePath()
    {
        var selectedPath = _dialogService.SelectExecutable(
            "ffprobe.exe auswählen",
            "ffprobe.exe|ffprobe.exe|Ausführbare Dateien (*.exe)|*.exe",
            ResolveInitialDirectory(FfprobePath));
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            FfprobePath = selectedPath;
        }
    }

    public void SelectMkvToolNixDirectory()
    {
        var selectedDirectory = _dialogService.SelectFolder(
            "MKVToolNix-Ordner auswählen",
            ResolveInitialDirectory(MkvToolNixDirectoryPath));
        if (!string.IsNullOrWhiteSpace(selectedDirectory))
        {
            MkvToolNixDirectoryPath = selectedDirectory;
        }
    }

    public async Task TestEmbyConnectionAsync()
    {
        try
        {
            SetBusy(true, "Prüfe Emby-Verbindung...");
            var serverInfo = await _services.EmbySync.TestConnectionAsync(BuildEmbySettings());
            StatusText = $"Emby verbunden: {serverInfo.ServerName} ({serverInfo.Version})";
        }
        catch
        {
            StatusText = "Emby-Verbindung fehlgeschlagen";
            throw;
        }
        finally
        {
            SetBusy(false, StatusText);
        }
    }

    public void SaveAndClose()
    {
        SaveSettings();
        CloseRequested?.Invoke(this, true);
    }

    public void Cancel()
    {
        CloseRequested?.Invoke(this, false);
    }

    public void SaveSettings()
    {
        _services.Archive.ConfigureArchiveRootDirectory(ArchiveRootDirectory);
        _services.ToolPaths.Save(new AppToolPathSettings
        {
            FfprobePath = FfprobePath,
            MkvToolNixDirectoryPath = MkvToolNixDirectoryPath
        });

        var metadataSettings = _services.EpisodeMetadata.LoadSettings();
        metadataSettings.TvdbApiKey = TvdbApiKey;
        metadataSettings.TvdbPin = TvdbPin;
        _services.EpisodeMetadata.SaveSettings(metadataSettings);

        _services.EmbySettings.Save(BuildEmbySettings());

        RefreshDerivedState();
        StatusText = $"Einstellungen gespeichert: {SettingsFilePath}";
    }

    private void RefreshDerivedState()
    {
        OnPropertyChanged(nameof(IsArchiveAvailable));
        OnPropertyChanged(nameof(ArchiveStatusText));
        OnPropertyChanged(nameof(ArchiveStatusTooltip));
        OnPropertyChanged(nameof(IsFfprobeAvailable));
        OnPropertyChanged(nameof(FfprobeStatusText));
        OnPropertyChanged(nameof(FfprobeStatusTooltip));
        OnPropertyChanged(nameof(IsMkvToolNixAvailable));
        OnPropertyChanged(nameof(MkvToolNixStatusText));
        OnPropertyChanged(nameof(MkvToolNixStatusTooltip));
        OnPropertyChanged(nameof(SettingsFilePath));
    }

    private AppEmbySettings BuildEmbySettings()
    {
        return new AppEmbySettings
        {
            ServerUrl = string.IsNullOrWhiteSpace(EmbyServerUrl) ? AppEmbySettings.DefaultServerUrl : EmbyServerUrl,
            ApiKey = EmbyApiKey,
            ScanWaitTimeoutSeconds = EmbyScanWaitTimeoutSeconds
        }.Clone();
    }

    private string GetMkvMergePath()
    {
        return MkvToolNixDirectoryPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? MkvToolNixDirectoryPath
            : Path.Combine(MkvToolNixDirectoryPath, "mkvmerge.exe");
    }

    private string GetMkvPropEditPath()
    {
        var baseDirectory = MkvToolNixDirectoryPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(MkvToolNixDirectoryPath) ?? string.Empty
            : MkvToolNixDirectoryPath;
        return Path.Combine(baseDirectory, "mkvpropedit.exe");
    }

    private static string ResolveInitialDirectory(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (Directory.Exists(configuredPath))
            {
                return configuredPath;
            }

            var parent = Path.GetDirectoryName(configuredPath);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            {
                return parent;
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
    }

    private void SetBusy(bool isBusy, string statusText)
    {
        if (_isBusy == isBusy && StatusText == statusText)
        {
            return;
        }

        _isBusy = isBusy;
        StatusText = statusText;
        OnPropertyChanged(nameof(IsInteractive));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
