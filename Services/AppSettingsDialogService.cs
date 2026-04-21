using System.Windows;
using MkvToolnixAutomatisierung.ViewModels;
using MkvToolnixAutomatisierung.Windows;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Fachlich gewünschte Startseite des zentralen Einstellungsdialogs.
/// </summary>
internal enum AppSettingsPage
{
    Archive,
    Tools,
    Tvdb,
    Emby
}

/// <summary>
/// Öffnet den zentralen Einstellungsdialog aus Hauptfenster, Emby-Modul oder TVDB-Dialog.
/// </summary>
internal interface IAppSettingsDialogService
{
    /// <summary>
    /// Öffnet den Einstellungsdialog modal.
    /// </summary>
    /// <param name="owner">Besitzendes Fenster, sofern vorhanden.</param>
    /// <param name="initialPage">Direkt sichtbarer Einstellungsbereich beim Öffnen.</param>
    /// <returns><see langword="true"/>, wenn Einstellungen übernommen wurden.</returns>
    bool ShowDialog(Window? owner = null, AppSettingsPage initialPage = AppSettingsPage.Archive);
}

/// <summary>
/// Erzeugt pro Aufruf ein frisches Settings-ViewModel und öffnet daraus den zentralen Dialog.
/// </summary>
internal sealed class AppSettingsDialogService : IAppSettingsDialogService
{
    private readonly AppSettingsModuleServices _services;
    private readonly IUserDialogService _dialogService;

    public AppSettingsDialogService(AppSettingsModuleServices services, IUserDialogService dialogService)
    {
        _services = services;
        _dialogService = dialogService;
    }

    public bool ShowDialog(Window? owner = null, AppSettingsPage initialPage = AppSettingsPage.Archive)
    {
        var viewModel = new AppSettingsWindowViewModel(_services, _dialogService, initialPage);
        var window = new AppSettingsWindow(viewModel)
        {
            Owner = owner
        };

        return window.ShowDialog() == true;
    }
}
