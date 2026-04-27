using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Gruppiert Einzel- und Batch-Mux als einen gemeinsamen Workflow-Schritt.
/// </summary>
/// <remarks>
/// Die fachlichen ViewModels bleiben absichtlich getrennt. Der Wrapper ist nur die Shell-Schicht,
/// damit globale Archivänderungen weiterhin beide Mux-Tabs erreichen.
/// </remarks>
internal sealed class MuxModuleViewModel : INotifyPropertyChanged, IArchiveConfigurationAwareModule
{
    private int _selectedTabIndex;

    public MuxModuleViewModel(
        SingleEpisodeMuxViewModel singleMux,
        BatchMuxViewModel batchMux)
    {
        SingleMux = singleMux;
        BatchMux = batchMux;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// ViewModel für den Einzel-Mux-Tab.
    /// </summary>
    public SingleEpisodeMuxViewModel SingleMux { get; }

    /// <summary>
    /// ViewModel für den Batch-Mux-Tab.
    /// </summary>
    public BatchMuxViewModel BatchMux { get; }

    /// <summary>
    /// Aktuell sichtbarer Mux-Tab. Die Auswahl bleibt erhalten, solange das Hauptfenster läuft.
    /// </summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (_selectedTabIndex == value)
            {
                return;
            }

            _selectedTabIndex = value;
            OnPropertyChanged();
        }
    }

    /// <inheritdoc />
    public void HandleArchiveConfigurationChanged()
    {
        SingleMux.HandleArchiveConfigurationChanged();
        BatchMux.HandleArchiveConfigurationChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
