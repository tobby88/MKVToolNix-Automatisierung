using System.Net.Http;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Emby;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels;
using MkvToolnixAutomatisierung.Windows;

namespace ReadmeScreenshotGenerator;

/// <summary>
/// Baut den produktiven Einstellungsdialog mit netzwerkfreien Diensten für den README-Screenshot auf.
/// </summary>
internal static class SettingsScreenshotFactory
{
    /// <summary>
    /// Erstellt den Metadaten-Tab mit sicheren Demowerten, ohne Downloads oder Provideranfragen auszulösen.
    /// </summary>
    /// <returns>Initialisierter produktiver Einstellungsdialog für den Offscreen-Renderer.</returns>
    public static AppSettingsWindow CreateMetadataSettingsWindow()
    {
        var settingsStore = new AppSettingsStore();
        var metadataStore = new AppMetadataStore(settingsStore);
        var services = new AppSettingsModuleServices(
            settingsStore,
            new SeriesArchiveService(new MkvMergeProbeService(), new AppArchiveSettingsStore(settingsStore)),
            new AppToolPathStore(settingsStore),
            new EpisodeMetadataLookupService(metadataStore, new TvdbClient()),
            new AppEmbySettingsStore(settingsStore),
            new EmbyMetadataSyncService(new EmbyClient(), new EmbyNfoProviderIdService()),
            new NoOpManagedToolInstaller(),
            new ImdbDatasetManager(
                metadataStore,
                new HttpClient(),
                new ImdbDatasetIndexBuilder(),
                new DecliningImdbDatasetConsent()));
        var viewModel = new AppSettingsWindowViewModel(
            services,
            new UserDialogService(),
            AppSettingsPage.Metadata)
        {
            AutoManageImdbDataset = true
        };

        return new AppSettingsWindow(viewModel);
    }

    private sealed class NoOpManagedToolInstaller : IManagedToolInstallerService
    {
        public Task<ManagedToolStartupResult> EnsureManagedToolsAsync(
            IProgress<ManagedToolStartupProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ManagedToolStartupResult([]));
        }
    }

    private sealed class DecliningImdbDatasetConsent : IImdbDatasetUpdateConsent
    {
        public bool ConfirmUpdate(ImdbDatasetUpdateOffer offer) => false;
    }
}
