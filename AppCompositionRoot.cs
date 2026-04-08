using MkvToolnixAutomatisierung.Composition;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels;
using MkvToolnixAutomatisierung.ViewModels.Modules;

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
        var appServices = CreateAppServices(muxServices, metadata, workflow);
        var mainWindowViewModel = UiCompositionModule.CreateMainWindowViewModel(appServices, dialogService, stores, tooling);

        return new AppComposition(dialogService, settingsLoadResult, mainWindowViewModel, appServices);
    }

    private static AppServices CreateAppServices(
        MuxDomainServices muxServices,
        MetadataServices metadata,
        WorkflowServices workflow)
    {
        return new AppServices(
            muxServices.Mux,
            muxServices.EpisodePlans,
            muxServices.BatchScan,
            muxServices.Archive,
            muxServices.OutputPaths,
            muxServices.CleanupFiles,
            metadata.Lookup,
            workflow.FileCopy,
            workflow.Cleanup,
            workflow.MuxWorkflow,
            workflow.BatchLogs);
    }

}

/// <summary>
/// Bündelt das Ergebnis des Bootstrap-Vorgangs, damit Startcode und Fenstererzeugung nicht jede Abhängigkeit einzeln tragen müssen.
/// </summary>
internal sealed record AppComposition(
    IUserDialogService DialogService,
    AppSettingsLoadResult SettingsLoadResult,
    MainWindowViewModel MainWindowViewModel,
    AppServices Services);
