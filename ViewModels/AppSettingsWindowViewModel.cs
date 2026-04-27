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
    private readonly ManagedToolSettings _managedMkvToolNixSettings;
    private readonly ManagedToolSettings _managedFfprobeSettings;
    private readonly ManagedToolSettings _managedMediathekViewSettings;
    private string _archiveRootDirectory;
    private string _ffprobePath;
    private string _mkvToolNixDirectoryPath;
    private string _mediathekViewPath;
    private bool _autoManageMkvToolNix;
    private bool _autoManageFfprobe;
    private bool _autoManageMediathekView;
    private string _tvdbApiKey;
    private string _tvdbPin;
    private ImdbLookupMode _imdbLookupMode;
    private string _embyServerUrl;
    private string _embyApiKey;
    private int _embyScanWaitTimeoutSeconds;
    private string _statusText = "Bereit";
    private bool _isBusy;
    private ResolvedToolPath? _resolvedFfprobePath;
    private ResolvedMkvToolNixPaths? _resolvedMkvToolNixPaths;
    private ResolvedToolPath? _resolvedMediathekViewPath;

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

        _managedMkvToolNixSettings = toolSettings.ManagedMkvToolNix.Clone();
        _managedFfprobeSettings = toolSettings.ManagedFfprobe.Clone();
        _managedMediathekViewSettings = toolSettings.ManagedMediathekView.Clone();
        _archiveRootDirectory = archiveSettings;
        _ffprobePath = toolSettings.FfprobePath;
        _mkvToolNixDirectoryPath = toolSettings.MkvToolNixDirectoryPath;
        _mediathekViewPath = toolSettings.MediathekViewPath;
        _autoManageMkvToolNix = _managedMkvToolNixSettings.AutoManageEnabled;
        _autoManageFfprobe = _managedFfprobeSettings.AutoManageEnabled;
        _autoManageMediathekView = _managedMediathekViewSettings.AutoManageEnabled;
        _tvdbApiKey = metadataSettings.TvdbApiKey;
        _tvdbPin = metadataSettings.TvdbPin;
        _imdbLookupMode = metadataSettings.ImdbLookupMode;
        _embyServerUrl = embySettings.ServerUrl;
        _embyApiKey = embySettings.ApiKey;
        _embyScanWaitTimeoutSeconds = embySettings.ScanWaitTimeoutSeconds;
        SelectedPage = initialPage;
        RefreshToolResolutionState();
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
            RefreshToolResolutionState();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFfprobeAvailable));
            OnPropertyChanged(nameof(FfprobeStatusText));
            OnPropertyChanged(nameof(FfprobeStatusTooltip));
        }
    }

    public bool AutoManageFfprobe
    {
        get => _autoManageFfprobe;
        set
        {
            if (_autoManageFfprobe == value)
            {
                return;
            }

            _autoManageFfprobe = value;
            _managedFfprobeSettings.AutoManageEnabled = value;
            RefreshToolResolutionState();
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
            RefreshToolResolutionState();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMkvToolNixAvailable));
            OnPropertyChanged(nameof(MkvToolNixStatusText));
            OnPropertyChanged(nameof(MkvToolNixStatusTooltip));
        }
    }

    public string MediathekViewPath
    {
        get => _mediathekViewPath;
        set
        {
            var normalized = (value ?? string.Empty).Trim();
            if (_mediathekViewPath == normalized)
            {
                return;
            }

            _mediathekViewPath = normalized;
            RefreshToolResolutionState();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMediathekViewAvailable));
            OnPropertyChanged(nameof(MediathekViewStatusText));
            OnPropertyChanged(nameof(MediathekViewStatusTooltip));
        }
    }

    public bool AutoManageMediathekView
    {
        get => _autoManageMediathekView;
        set
        {
            if (_autoManageMediathekView == value)
            {
                return;
            }

            _autoManageMediathekView = value;
            _managedMediathekViewSettings.AutoManageEnabled = value;
            RefreshToolResolutionState();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMediathekViewAvailable));
            OnPropertyChanged(nameof(MediathekViewStatusText));
            OnPropertyChanged(nameof(MediathekViewStatusTooltip));
        }
    }

    public bool AutoManageMkvToolNix
    {
        get => _autoManageMkvToolNix;
        set
        {
            if (_autoManageMkvToolNix == value)
            {
                return;
            }

            _autoManageMkvToolNix = value;
            _managedMkvToolNixSettings.AutoManageEnabled = value;
            RefreshToolResolutionState();
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

    public IReadOnlyList<ImdbLookupModeOption> ImdbLookupModeOptions { get; } =
    [
        new(ImdbLookupMode.Auto, "Automatisch (API, sonst Browser)", "Versucht zuerst imdbapi.dev. Nur wenn der Dienst insgesamt nicht erreichbar ist, fällt der Dialog auf die Browserhilfe zurück."),
        new(ImdbLookupMode.ApiOnly, "Nur imdbapi.dev", "Erzwingt die freie API. Kein automatischer Browser-Fallback."),
        new(ImdbLookupMode.BrowserOnly, "Nur Browserhilfe", "Nutzen der bisherigen browsergestützten IMDb-Suche ohne API-Abfrage.")
    ];

    public ImdbLookupMode SelectedImdbLookupMode
    {
        get => _imdbLookupMode;
        set
        {
            if (_imdbLookupMode == value)
            {
                return;
            }

            _imdbLookupMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ImdbLookupModeDescription));
        }
    }

    public string ImdbLookupModeDescription => ImdbLookupModeOptions
        .FirstOrDefault(option => option.Value == SelectedImdbLookupMode)?.Description
        ?? string.Empty;

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

    public bool IsFfprobeAvailable => _resolvedFfprobePath is not null;

    public string FfprobeStatusText => _resolvedFfprobePath?.Source switch
    {
        ToolPathResolutionSource.ManualOverride => "ffprobe bereit (Override)",
        ToolPathResolutionSource.ManagedSettings or ToolPathResolutionSource.PortableToolsFallback
            => AutoManageFfprobe ? "ffprobe bereit (automatisch)" : "ffprobe bereit (verwaltet)",
        ToolPathResolutionSource.SystemPath => "ffprobe bereit (PATH)",
        ToolPathResolutionSource.DownloadsFallback => "ffprobe bereit (Fallback)",
        _ when AutoManageFfprobe => "ffprobe wird automatisch verwaltet",
        _ => "ffprobe fehlt"
    };

    public string FfprobeStatusTooltip => BuildFfprobeTooltip();

    public bool IsMkvToolNixAvailable => _resolvedMkvToolNixPaths is not null;

    public string MkvToolNixStatusText => _resolvedMkvToolNixPaths?.Source switch
    {
        ToolPathResolutionSource.ManualOverride => "MKVToolNix bereit (Override)",
        ToolPathResolutionSource.ManagedSettings or ToolPathResolutionSource.PortableToolsFallback
            => AutoManageMkvToolNix ? "MKVToolNix bereit (automatisch)" : "MKVToolNix bereit (verwaltet)",
        ToolPathResolutionSource.DownloadsFallback => "MKVToolNix bereit (Fallback)",
        _ when AutoManageMkvToolNix => "MKVToolNix wird automatisch verwaltet",
        _ => "MKVToolNix unvollständig"
    };

    public string MkvToolNixStatusTooltip => BuildMkvToolNixTooltip();

    public bool IsMediathekViewAvailable => _resolvedMediathekViewPath is not null;

    public string MediathekViewStatusText => _resolvedMediathekViewPath?.Source switch
    {
        ToolPathResolutionSource.ManualOverride => "MediathekView bereit (Override)",
        ToolPathResolutionSource.ManagedSettings => "MediathekView bereit (verwaltet)",
        ToolPathResolutionSource.PortableToolsFallback => "MediathekView bereit (Tools)",
        ToolPathResolutionSource.SystemPath => "MediathekView bereit (PATH)",
        ToolPathResolutionSource.InstalledApplication => "MediathekView bereit (installiert)",
        ToolPathResolutionSource.DownloadsFallback => "MediathekView bereit (portable)",
        _ => AutoManageMediathekView ? "MediathekView wird automatisch bereitgestellt" : "MediathekView fehlt"
    };

    public string MediathekViewStatusTooltip => BuildMediathekViewTooltip();

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

    public void SelectMediathekViewPath()
    {
        var selectedPath = _dialogService.SelectExecutable(
            "MediathekView.exe auswählen",
            "MediathekView.exe|MediathekView.exe|Ausführbare Dateien (*.exe)|*.exe",
            ResolveInitialDirectory(MediathekViewPath));
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            MediathekViewPath = selectedPath;
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

    public async Task SaveAndCloseAsync(CancellationToken cancellationToken = default)
    {
        await SaveSettingsAndEnsureManagedToolsAsync(cancellationToken);
        CloseRequested?.Invoke(this, true);
    }

    public void Cancel()
    {
        CloseRequested?.Invoke(this, false);
    }

    public void SaveSettings()
    {
        var normalizedArchiveRootDirectory = SeriesArchiveService.NormalizeArchiveRootDirectoryForSettings(ArchiveRootDirectory);
        _services.Settings.Update(settings =>
        {
            settings.Archive ??= new AppArchiveSettings();
            settings.Archive.DefaultSeriesArchiveRootPath = normalizedArchiveRootDirectory;

            settings.ToolPaths ??= new AppToolPathSettings();
            settings.ToolPaths.FfprobePath = FfprobePath;
            settings.ToolPaths.MkvToolNixDirectoryPath = MkvToolNixDirectoryPath;
            settings.ToolPaths.MediathekViewPath = MediathekViewPath;
            settings.ToolPaths.ManagedFfprobe = _managedFfprobeSettings.Clone();
            settings.ToolPaths.ManagedFfprobe.AutoManageEnabled = AutoManageFfprobe;
            settings.ToolPaths.ManagedMkvToolNix = _managedMkvToolNixSettings.Clone();
            settings.ToolPaths.ManagedMkvToolNix.AutoManageEnabled = AutoManageMkvToolNix;
            settings.ToolPaths.ManagedMediathekView = _managedMediathekViewSettings.Clone();
            settings.ToolPaths.ManagedMediathekView.AutoManageEnabled = AutoManageMediathekView;

            settings.Metadata ??= new AppMetadataSettings();
            settings.Metadata.TvdbApiKey = TvdbApiKey;
            settings.Metadata.TvdbPin = TvdbPin;
            settings.Metadata.ImdbLookupMode = SelectedImdbLookupMode;

            settings.Emby = BuildEmbySettings();
        });
        _services.Archive.ApplyArchiveRootDirectoryForCurrentSession(normalizedArchiveRootDirectory);

        RefreshDerivedState();
        StatusText = $"Einstellungen gespeichert: {SettingsFilePath}";
    }

    /// <summary>
    /// Speichert die Dialogwerte und startet danach direkt die automatische Tool-Bereitstellung.
    /// </summary>
    /// <remarks>
    /// Dadurch ist nach dem Aktivieren von MKVToolNix, ffprobe oder MediathekView kein App-Neustart mehr
    /// nötig. Der Dialog bleibt währenddessen offen, damit Download- und Entpackstatus sichtbar bleiben.
    /// </remarks>
    public async Task SaveSettingsAndEnsureManagedToolsAsync(CancellationToken cancellationToken = default)
    {
        var settingsWereSaved = false;

        try
        {
            SetBusy(true, "Einstellungen werden gespeichert...");
            SaveSettings();
            settingsWereSaved = true;
            StatusText = "Werkzeuge werden geprüft...";

            var result = await _services.ManagedTools.EnsureManagedToolsAsync(
                new Progress<ManagedToolStartupProgress>(UpdateManagedToolProgress),
                cancellationToken);
            RefreshDerivedState();

            if (result.HasWarning)
            {
                StatusText = "Einstellungen gespeichert; Werkzeugprüfung mit Warnungen.";
                _dialogService.ShowWarning("Werkzeugverwaltung", result.WarningMessage!);
            }
            else
            {
                StatusText = "Einstellungen gespeichert; Werkzeuge bereit.";
            }
        }
        catch
        {
            StatusText = settingsWereSaved
                ? "Einstellungen gespeichert; Werkzeugprüfung fehlgeschlagen."
                : "Einstellungen konnten nicht gespeichert werden.";
            throw;
        }
        finally
        {
            SetBusy(false, StatusText);
        }
    }

    private void RefreshDerivedState()
    {
        RefreshToolResolutionState();
        OnPropertyChanged(nameof(IsArchiveAvailable));
        OnPropertyChanged(nameof(ArchiveStatusText));
        OnPropertyChanged(nameof(ArchiveStatusTooltip));
        OnPropertyChanged(nameof(IsFfprobeAvailable));
        OnPropertyChanged(nameof(FfprobeStatusText));
        OnPropertyChanged(nameof(FfprobeStatusTooltip));
        OnPropertyChanged(nameof(IsMkvToolNixAvailable));
        OnPropertyChanged(nameof(MkvToolNixStatusText));
        OnPropertyChanged(nameof(MkvToolNixStatusTooltip));
        OnPropertyChanged(nameof(IsMediathekViewAvailable));
        OnPropertyChanged(nameof(MediathekViewStatusText));
        OnPropertyChanged(nameof(MediathekViewStatusTooltip));
        OnPropertyChanged(nameof(AutoManageMediathekView));
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

    /// <summary>
    /// Baut einen flüchtigen Settings-Schnappschuss aus dem aktuellen Dialogzustand.
    /// </summary>
    /// <remarks>
    /// Die Settings-UI arbeitet bewusst mit noch nicht gespeicherten Werten. Für Status und Tooltips
    /// wird daher dieselbe Resolver-Logik wie im Produktivcode auf diesen flüchtigen Zustand angewendet.
    /// </remarks>
    private AppToolPathSettings BuildCurrentToolSettings()
    {
        return new AppToolPathSettings
        {
            FfprobePath = FfprobePath,
            MkvToolNixDirectoryPath = MkvToolNixDirectoryPath,
            MediathekViewPath = MediathekViewPath,
            ManagedFfprobe = _managedFfprobeSettings.Clone(),
            ManagedMkvToolNix = _managedMkvToolNixSettings.Clone(),
            ManagedMediathekView = _managedMediathekViewSettings.Clone()
        };
    }

    private void RefreshToolResolutionState()
    {
        var currentSettings = BuildCurrentToolSettings();
        _resolvedFfprobePath = ManagedToolResolution.TryResolveFfprobe(currentSettings);
        _resolvedMkvToolNixPaths = ManagedToolResolution.TryResolveMkvToolNix(currentSettings);
        _resolvedMediathekViewPath = MediathekViewPathResolver.TryResolve(currentSettings);
    }

    private string BuildFfprobeTooltip()
    {
        if (_resolvedFfprobePath is not null)
        {
            return _resolvedFfprobePath.Source switch
            {
                ToolPathResolutionSource.ManagedSettings or ToolPathResolutionSource.PortableToolsFallback
                    => BuildManagedToolTooltip("ffprobe", _managedFfprobeSettings, _resolvedFfprobePath.Path, AutoManageFfprobe),
                ToolPathResolutionSource.ManualOverride
                    => BuildOverrideTooltip("Manueller Override aktiv", _resolvedFfprobePath.Path, AutoManageFfprobe),
                ToolPathResolutionSource.SystemPath
                    => BuildExternalSourceTooltip("ffprobe wird aus dem Windows-PATH verwendet", _resolvedFfprobePath.Path, AutoManageFfprobe),
                ToolPathResolutionSource.DownloadsFallback
                    => BuildExternalSourceTooltip("ffprobe wurde aus einem Download-Ordner erkannt", _resolvedFfprobePath.Path, AutoManageFfprobe),
                _ => BuildExternalSourceTooltip("ffprobe wurde erkannt", _resolvedFfprobePath.Path, AutoManageFfprobe)
            };
        }

        if (AutoManageFfprobe)
        {
            return BuildManagedPendingTooltip("ffprobe", _managedFfprobeSettings, FfprobePath);
        }

        return string.IsNullOrWhiteSpace(FfprobePath)
            ? "Optional. Bleibt Auto-Verwaltung deaktiviert und das Feld leer, nutzt die App nur noch Windows-PATH oder Fallback-Suchen."
            : $"Der manuelle ffprobe-Override ist aktuell nicht verwendbar:{Environment.NewLine}{FfprobePath}";
    }

    private string BuildMkvToolNixTooltip()
    {
        if (_resolvedMkvToolNixPaths is not null)
        {
            var installationPath = Path.GetDirectoryName(_resolvedMkvToolNixPaths.MkvMergePath) ?? _resolvedMkvToolNixPaths.MkvMergePath;
            var detail = $"{installationPath}{Environment.NewLine}{Environment.NewLine}mkvmerge.exe:{Environment.NewLine}{_resolvedMkvToolNixPaths.MkvMergePath}{Environment.NewLine}{Environment.NewLine}mkvpropedit.exe:{Environment.NewLine}{_resolvedMkvToolNixPaths.MkvPropEditPath}";

            return _resolvedMkvToolNixPaths.Source switch
            {
                ToolPathResolutionSource.ManagedSettings or ToolPathResolutionSource.PortableToolsFallback
                    => BuildManagedToolTooltip("MKVToolNix", _managedMkvToolNixSettings, detail, AutoManageMkvToolNix),
                ToolPathResolutionSource.ManualOverride
                    => BuildOverrideTooltip("Manueller Override aktiv", detail, AutoManageMkvToolNix),
                ToolPathResolutionSource.DownloadsFallback
                    => BuildExternalSourceTooltip("MKVToolNix wurde aus einem Download-Ordner erkannt", detail, AutoManageMkvToolNix),
                _ => BuildExternalSourceTooltip("MKVToolNix wurde erkannt", detail, AutoManageMkvToolNix)
            };
        }

        if (AutoManageMkvToolNix)
        {
            return BuildManagedPendingTooltip("MKVToolNix", _managedMkvToolNixSettings, MkvToolNixDirectoryPath);
        }

        return string.IsNullOrWhiteSpace(MkvToolNixDirectoryPath)
            ? "Auto-Verwaltung deaktiviert. Optional kann hier ein manueller Override gesetzt werden."
            : $"Aus dem manuellen Override fehlen mkvmerge.exe und/oder mkvpropedit.exe:{Environment.NewLine}{MkvToolNixDirectoryPath}";
    }

    private string BuildMediathekViewTooltip()
    {
        if (_resolvedMediathekViewPath is not null)
        {
            return _resolvedMediathekViewPath.Source switch
            {
                ToolPathResolutionSource.ManagedSettings or ToolPathResolutionSource.PortableToolsFallback
                    => BuildManagedToolTooltip("MediathekView", _managedMediathekViewSettings, _resolvedMediathekViewPath.Path, AutoManageMediathekView),
                ToolPathResolutionSource.ManualOverride
                    => BuildOverrideTooltip("Manueller MediathekView-Pfad", _resolvedMediathekViewPath.Path, AutoManageMediathekView),
                ToolPathResolutionSource.SystemPath
                    => BuildExternalSourceTooltip("MediathekView wurde im Windows-PATH gefunden", _resolvedMediathekViewPath.Path, AutoManageMediathekView),
                ToolPathResolutionSource.InstalledApplication
                    => BuildExternalSourceTooltip("Installierte MediathekView-Version gefunden", _resolvedMediathekViewPath.Path, AutoManageMediathekView),
                ToolPathResolutionSource.DownloadsFallback
                    => BuildExternalSourceTooltip("Portable MediathekView-Version im Downloadordner gefunden", _resolvedMediathekViewPath.Path, AutoManageMediathekView),
                _ => BuildExternalSourceTooltip("MediathekView wurde gefunden", _resolvedMediathekViewPath.Path, AutoManageMediathekView)
            };
        }

        if (AutoManageMediathekView)
        {
            return BuildManagedPendingTooltip("MediathekView", _managedMediathekViewSettings, MediathekViewPath);
        }

        return string.IsNullOrWhiteSpace(MediathekViewPath)
            ? "Optional. Die App sucht zusätzlich nach installierten Versionen, PATH-Einträgen und portablen MediathekView-Ordnern im Downloadverzeichnis. Automatischer Download kann hier bei Bedarf aktiviert werden."
            : $"Der manuelle MediathekView-Pfad ist aktuell nicht verwendbar:{Environment.NewLine}{MediathekViewPath}";
    }

    private static string BuildManagedToolTooltip(
        string toolName,
        ManagedToolSettings settings,
        string installedPath,
        bool autoManageEnabled)
    {
        var lines = new List<string>
        {
            string.IsNullOrWhiteSpace(settings.InstalledVersion)
                ? $"Verwaltetes {toolName}:{Environment.NewLine}{installedPath}"
                : $"Verwaltetes {toolName} ({settings.InstalledVersion}):{Environment.NewLine}{installedPath}"
        };

        lines.Add(string.Empty);
        lines.Add(autoManageEnabled
            ? "Automatische Updates sind aktiv."
            : "Automatische Updates sind deaktiviert; die vorhandene verwaltete Installation bleibt nutzbar.");

        if (settings.LastCheckedUtc is not null)
        {
            lines.Add(string.Empty);
            lines.Add($"Zuletzt erfolgreich geprüft:{Environment.NewLine}{settings.LastCheckedUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm:ss}");
        }

        if (settings.LastFailedCheckUtc is not null)
        {
            lines.Add(string.Empty);
            lines.Add($"Zuletzt fehlgeschlagen:{Environment.NewLine}{settings.LastFailedCheckUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm:ss}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildOverrideTooltip(string headline, string path, bool autoManageEnabled)
    {
        var lines = new List<string>
        {
            $"{headline}:{Environment.NewLine}{path}"
        };

        if (autoManageEnabled)
        {
            lines.Add(string.Empty);
            lines.Add("Die automatische Verwaltung bleibt gespeichert, wird aber durch den Override derzeit übersteuert.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildExternalSourceTooltip(string headline, string path, bool autoManageEnabled)
    {
        var lines = new List<string>
        {
            $"{headline}:{Environment.NewLine}{path}"
        };

        if (autoManageEnabled)
        {
            lines.Add(string.Empty);
            lines.Add("Solange diese externe Quelle verfügbar ist, wird kein zusätzlicher Download erzwungen.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildManagedPendingTooltip(string toolName, ManagedToolSettings settings, string manualOverridePath)
    {
        var lines = new List<string>
        {
            $"{toolName} wird nach dem Speichern sowie beim Start automatisch heruntergeladen und aktualisiert."
        };

        if (!string.IsNullOrWhiteSpace(settings.InstalledPath))
        {
            lines.Add(string.Empty);
            lines.Add(string.IsNullOrWhiteSpace(settings.InstalledVersion)
                ? $"Zuletzt bekannte verwaltete Installation:{Environment.NewLine}{settings.InstalledPath}"
                : $"Zuletzt bekannte verwaltete Installation ({settings.InstalledVersion}):{Environment.NewLine}{settings.InstalledPath}");
        }

        if (settings.LastCheckedUtc is not null)
        {
            lines.Add(string.Empty);
            lines.Add($"Zuletzt erfolgreich geprüft:{Environment.NewLine}{settings.LastCheckedUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm:ss}");
        }

        if (settings.LastFailedCheckUtc is not null)
        {
            lines.Add(string.Empty);
            lines.Add($"Zuletzt fehlgeschlagen:{Environment.NewLine}{settings.LastFailedCheckUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm:ss}");
        }

        if (!string.IsNullOrWhiteSpace(manualOverridePath))
        {
            lines.Add(string.Empty);
            lines.Add($"Optionaler manueller Override:{Environment.NewLine}{manualOverridePath}");
        }

        return string.Join(Environment.NewLine, lines);
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

        return PreferredDownloadDirectoryHelper.TryGetDownloadsDirectory()
               ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
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

    private void UpdateManagedToolProgress(ManagedToolStartupProgress progress)
    {
        StatusText = string.IsNullOrWhiteSpace(progress.DetailText)
            ? progress.StatusText
            : $"{progress.StatusText} {progress.DetailText}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed record ImdbLookupModeOption(
        ImdbLookupMode Value,
        string DisplayText,
        string Description);
}
