using System.ComponentModel;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.ViewModels.Commands;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Startseite für den externen Download-Schritt mit MediathekView.
/// </summary>
internal sealed class DownloadViewModel : IModuleInteractionState, IGlobalSettingsAwareModule
{
    private readonly DownloadModuleServices _services;
    private readonly IUserDialogService _dialogService;
    private ResolvedToolPath? _resolvedMediathekView;
    private string _statusText = "Bereit";
    private bool _isBusy;
    private bool _refreshPending;

    public DownloadViewModel(DownloadModuleServices services, IUserDialogService dialogService)
    {
        _services = services;
        _dialogService = dialogService;
        StartMediathekViewCommand = new AsyncRelayCommand(StartMediathekViewAsync, () => !_isBusy, HandleError);
        OpenToolSettingsCommand = new AsyncRelayCommand(OpenToolSettingsAsync, () => !_isBusy, HandleError);
        RefreshCommand = new AsyncRelayCommand(RefreshStatusAsync, () => !_isBusy, HandleError);
        Initialization = RefreshCommand.ExecuteAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncRelayCommand StartMediathekViewCommand { get; }

    public AsyncRelayCommand OpenToolSettingsCommand { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Erste Pfadsuche, ohne den aufrufenden UI-Thread zu blockieren.</summary>
    internal Task Initialization { get; }

    public bool IsInteractive => !_isBusy;

    public bool IsMediathekViewAvailable => _resolvedMediathekView is not null;

    public string MediathekViewStatusText => _resolvedMediathekView?.Source switch
    {
        ToolPathResolutionSource.ManualOverride => "MediathekView bereit (Override)",
        ToolPathResolutionSource.ManagedSettings => "MediathekView bereit (verwaltet)",
        ToolPathResolutionSource.PortableToolsFallback => "MediathekView bereit (Tools)",
        ToolPathResolutionSource.SystemPath => "MediathekView bereit (PATH)",
        ToolPathResolutionSource.InstalledApplication => "MediathekView bereit (installiert)",
        ToolPathResolutionSource.DownloadsFallback => "MediathekView bereit (portable)",
        _ => "MediathekView nicht gefunden"
    };

    public string MediathekViewPathText => _resolvedMediathekView?.Path
        ?? "In Einstellungen einen Pfad setzen oder MediathekView installieren/ins Downloadverzeichnis legen.";

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

    public void HandleGlobalSettingsChanged()
    {
        _refreshPending = true;
        _ = RefreshCommand.ExecuteAsync();
    }

    private async Task StartMediathekViewAsync()
    {
        MediathekViewLaunchResult result;
        SetBusy(true);
        try
        {
            StatusText = "MediathekView wird gestartet...";
            result = await Task.Run(_services.MediathekView.Launch);
        }
        finally
        {
            SetBusy(false);
        }

        if (result.IsSuccess)
        {
            ApplyResolvedPath(new ResolvedToolPath(result.ExecutablePath!, result.Source));
        }
        else if (string.IsNullOrWhiteSpace(result.ExecutablePath))
        {
            ApplyResolvedPath(null);
        }

        if (_refreshPending)
        {
            await RefreshStatusAsync();
        }

        if (result.IsSuccess)
        {
            StatusText = $"MediathekView gestartet: {result.ExecutablePath}";
            return;
        }

        StatusText = result.ErrorMessage ?? "MediathekView konnte nicht gestartet werden.";
        if (string.IsNullOrWhiteSpace(result.ExecutablePath))
        {
            _dialogService.ShowWarning(
                "MediathekView nicht gefunden",
                "MediathekView wurde nicht gefunden. Lege in den Einstellungen einen Pfad zur installierten oder portablen MediathekView.exe fest.");
        }
        else
        {
            _dialogService.ShowError($"MediathekView konnte nicht gestartet werden:{Environment.NewLine}{result.ExecutablePath}{Environment.NewLine}{Environment.NewLine}{result.ErrorMessage}");
        }
    }

    private async Task OpenToolSettingsAsync()
    {
        SetBusy(true);
        try
        {
            _services.SettingsDialog.ShowDialog(initialPage: AppSettingsPage.Tools);
        }
        finally
        {
            SetBusy(false);
        }

        // Settings can already have been saved when a subsequent resource update is cancelled.
        await RefreshStatusAsync();
    }

    private async Task RefreshStatusAsync()
    {
        SetBusy(true);
        try
        {
            do
            {
                _refreshPending = false;
                StatusText = "MediathekView wird gesucht...";
                ApplyResolvedPath(await Task.Run(_services.MediathekView.TryResolve));
            }
            while (_refreshPending);

            StatusText = IsMediathekViewAvailable
                ? "MediathekView kann gestartet werden."
                : "MediathekView ist noch nicht konfiguriert oder auffindbar.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyResolvedPath(ResolvedToolPath? path)
    {
        _resolvedMediathekView = path;
        OnPropertyChanged(nameof(IsMediathekViewAvailable));
        OnPropertyChanged(nameof(MediathekViewStatusText));
        OnPropertyChanged(nameof(MediathekViewPathText));
    }

    private void SetBusy(bool value)
    {
        _isBusy = value;
        OnPropertyChanged(nameof(IsInteractive));
        StartMediathekViewCommand.RaiseCanExecuteChanged();
        OpenToolSettingsCommand.RaiseCanExecuteChanged();
        RefreshCommand.RaiseCanExecuteChanged();
    }

    private void HandleError(Exception exception)
    {
        StatusText = "MediathekView konnte nicht vorbereitet werden.";
        _dialogService.ShowError($"{StatusText}{Environment.NewLine}{exception.Message}");
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
