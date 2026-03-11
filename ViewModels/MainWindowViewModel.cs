using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MkvToolnixAutomatisierung.Services;
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
        var appServices = new AppServices(
            new MkvToolNixLocator(),
            new MkvMergeProbeService(),
            new MuxExecutionService());

        var dialogService = new UserDialogService();
        var singleEpisode = new SingleEpisodeMuxViewModel(appServices, dialogService);
        var batch = new BatchMuxViewModel(appServices, dialogService);

        return new MainWindowViewModel(
        [
            new ModuleNavigationItem(
                "Einzelepisode",
                "Muxe eine einzelne Episode mit automatischer Dateisuche und manueller Korrektur.",
                singleEpisode),
            new ModuleNavigationItem(
                "Batch-Verarbeitung",
                "Suche mehrere Episoden in einem Ordner und muxe sie nacheinander.",
                batch)
        ]);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record ModuleNavigationItem(string Title, string Description, object ContentViewModel);