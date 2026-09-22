using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Gruppiert Einzel- und Batch-Mux als einen gemeinsamen Workflow-Schritt.
/// </summary>
/// <remarks>
/// Die fachlichen ViewModels bleiben getrennt. Der Wrapper koordiniert ihre Interaktionssperre
/// und reicht globale Archivänderungen an beide Mux-Tabs weiter.
/// </remarks>
internal sealed class MuxModuleViewModel : IModuleInteractionState, IArchiveConfigurationAwareModule
{
    private int _selectedTabIndex;
    private bool _isUpdatingInteractionState;

    public MuxModuleViewModel(
        SingleEpisodeMuxViewModel singleMux,
        BatchMuxViewModel batchMux)
    {
        SingleMux = singleMux;
        BatchMux = batchMux;
        SingleMux.PropertyChanged += HandleChildPropertyChanged;
        BatchMux.PropertyChanged += HandleChildPropertyChanged;
        UpdateInteractionState();
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

    public bool IsInteractive => SingleMux.IsInteractive && BatchMux.IsInteractive;

    // Der aktive Tab bleibt bedienbar, insbesondere sein Abbruchknopf.
    public bool IsSingleTabEnabled => IsInteractive || SelectedTabIndex == 0;
    public bool IsBatchTabEnabled => IsInteractive || SelectedTabIndex == 1;

    /// <summary>
    /// Aktuell sichtbarer Mux-Tab. Die Auswahl bleibt erhalten, solange das Hauptfenster läuft.
    /// </summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (_selectedTabIndex == value || value is < 0 or > 1)
            {
                return;
            }

            if (!IsInteractive)
            {
                OnPropertyChanged();
                return;
            }

            _selectedTabIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSingleTabEnabled));
            OnPropertyChanged(nameof(IsBatchTabEnabled));
        }
    }

    /// <inheritdoc />
    public void HandleArchiveConfigurationChanged()
    {
        SingleMux.HandleArchiveConfigurationChanged();
        BatchMux.HandleArchiveConfigurationChanged();
    }

    private void HandleChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(IsInteractive))
        {
            UpdateInteractionState();
        }
    }

    private void UpdateInteractionState()
    {
        if (_isUpdatingInteractionState)
        {
            return;
        }

        _isUpdatingInteractionState = true;
        try
        {
            // Nur eigene Operationen weiterreichen, nicht die vom Geschwister geerbte Sperre.
            // UI-Commands starten synchron bis SetBusy; deshalb bleibt auch ExecuteAsync geschützt.
            SingleMux.SetSiblingOperationActive(BatchMux.IsOperationActive);
            BatchMux.SetSiblingOperationActive(SingleMux.IsOperationActive);
        }
        finally
        {
            _isUpdatingInteractionState = false;
        }

        OnPropertyChanged(nameof(IsInteractive));
        OnPropertyChanged(nameof(IsSingleTabEnabled));
        OnPropertyChanged(nameof(IsBatchTabEnabled));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
