using System.ComponentModel;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.ViewModels.Commands;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Startseite für den externen Download-Schritt mit MediathekView.
/// </summary>
internal sealed class DownloadViewModel : INotifyPropertyChanged, IGlobalSettingsAwareModule
{
    private readonly DownloadModuleServices _services;
    private readonly IUserDialogService _dialogService;
    private ResolvedToolPath? _resolvedMediathekView;
    private string _statusText = "Bereit";

    public DownloadViewModel(DownloadModuleServices services, IUserDialogService dialogService)
    {
        _services = services;
        _dialogService = dialogService;
        StartMediathekViewCommand = new RelayCommand(StartMediathekView);
        OpenToolSettingsCommand = new RelayCommand(OpenToolSettings);
        RefreshCommand = new RelayCommand(RefreshStatus);
        RefreshStatus();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand StartMediathekViewCommand { get; }

    public RelayCommand OpenToolSettingsCommand { get; }

    public RelayCommand RefreshCommand { get; }

    public bool IsMediathekViewAvailable => _resolvedMediathekView is not null;

    public string MediathekViewStatusText => _resolvedMediathekView?.Source switch
    {
        ToolPathResolutionSource.ManualOverride => "MediathekView bereit (Override)",
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
        RefreshStatus();
    }

    private void StartMediathekView()
    {
        var result = _services.MediathekView.Launch();
        RefreshStatus();
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

    private void OpenToolSettings()
    {
        if (_services.SettingsDialog.ShowDialog(initialPage: AppSettingsPage.Tools))
        {
            RefreshStatus();
        }
    }

    private void RefreshStatus()
    {
        _resolvedMediathekView = _services.MediathekView.TryResolve();
        OnPropertyChanged(nameof(IsMediathekViewAvailable));
        OnPropertyChanged(nameof(MediathekViewStatusText));
        OnPropertyChanged(nameof(MediathekViewPathText));
        StatusText = IsMediathekViewAvailable
            ? "MediathekView kann gestartet werden."
            : "MediathekView ist noch nicht konfiguriert oder auffindbar.";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
