using MkvToolnixAutomatisierung.Composition;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.ViewModels;

namespace MkvToolnixAutomatisierung;

/// <summary>
/// Zentraler Composition Root der App: hier werden alle langlebigen Services und ViewModels einmalig verdrahtet.
/// </summary>
internal sealed class AppCompositionRoot
{
    /// <summary>
    /// Erstellt die komplette Objektstruktur der Anwendung in fachlich gruppierten Schritten.
    /// </summary>
    /// <returns>Fertig verdrahtete Anwendungskomposition für den Bootstrapper.</returns>
    public AppComposition Create()
    {
        IUserDialogService dialogService = new UserDialogService();
        var stores = AppStoreCompositionModule.Create();
        var settingsLoadResult = stores.Settings.LoadWithDiagnostics();
        var tooling = ToolingCompositionModule.Create(stores);
        var metadata = MetadataCompositionModule.Create(stores);
        var muxServices = MuxCompositionModule.Create(stores, tooling, metadata);
        var workflow = WorkflowCompositionModule.Create(muxServices);
        var sharedEpisodeServices = UiCompositionModule.CreateSharedEpisodeServices(muxServices, metadata);
        var singleEpisodeServices = UiCompositionModule.CreateSingleEpisodeServices(sharedEpisodeServices, workflow);
        var batchServices = UiCompositionModule.CreateBatchServices(sharedEpisodeServices, muxServices, workflow);
        var mainWindowServices = UiCompositionModule.CreateMainWindowServices(muxServices, stores, tooling);
        var mainWindowViewModel = UiCompositionModule.CreateMainWindowViewModel(
            singleEpisodeServices,
            batchServices,
            mainWindowServices,
            dialogService);

        return new AppComposition(dialogService, settingsLoadResult, mainWindowViewModel);
    }
}

/// <summary>
/// Bündelt das Ergebnis des Bootstrap-Vorgangs, damit Startcode und Fenstererzeugung nicht jede Abhängigkeit einzeln tragen müssen.
/// </summary>
internal sealed record AppComposition(
    IUserDialogService DialogService,
    AppSettingsLoadResult SettingsLoadResult,
    MainWindowViewModel MainWindowViewModel);
