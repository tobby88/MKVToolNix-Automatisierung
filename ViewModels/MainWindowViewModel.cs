using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels.Modules;

namespace MkvToolnixAutomatisierung.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private ModuleNavigationItem _selectedModule;

    public MainWindowViewModel(IReadOnlyList<ModuleNavigationItem> modules)
    {
        Modules = new ObservableCollection<ModuleNavigationItem>(modules);
        _selectedModule = Modules.First();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ModuleNavigationItem> Modules { get; }

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
        }
    }

    public static MainWindowViewModel CreateDefault()
    {
        var locator = new MkvToolNixLocator();
        var probeService = new MkvMergeProbeService();
        var archiveService = new SeriesArchiveService(probeService);
        var planner = new SeriesEpisodeMuxPlanner(locator, probeService, archiveService);
        var executionService = new MuxExecutionService();
        var outputParser = new MkvMergeOutputParser();
        var muxService = new SeriesEpisodeMuxService(planner, executionService, outputParser);
        var fileCopyService = new FileCopyService();
        var cleanupService = new EpisodeCleanupService();
        var metadataStore = new AppMetadataStore();
        var tvdbClient = new TvdbClient();
        var metadataLookupService = new EpisodeMetadataLookupService(metadataStore, tvdbClient);
        var appServices = new AppServices(muxService, metadataLookupService, fileCopyService, cleanupService);

        var dialogService = new UserDialogService();
        var singleEpisode = new SingleEpisodeMuxViewModel(appServices, dialogService);
        var batch = new BatchMuxViewModel(appServices, dialogService);

        return new MainWindowViewModel(
        [
            new ModuleNavigationItem(
                "Einzelepisode",
                "Auto erkennen,\npruefen, muxen.",
                singleEpisode),
            new ModuleNavigationItem(
                "Batch-Verarbeitung",
                "Ordner scannen,\nalles gesammelt muxen.",
                batch)
        ]);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record ModuleNavigationItem(string Title, string Description, object ContentViewModel);
