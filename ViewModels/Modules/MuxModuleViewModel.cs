namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Gruppiert Einzel- und Batch-Mux als einen gemeinsamen Workflow-Schritt.
/// </summary>
/// <remarks>
/// Die fachlichen ViewModels bleiben absichtlich getrennt. Der Wrapper ist nur die Shell-Schicht,
/// damit globale Archivänderungen weiterhin beide Mux-Tabs erreichen.
/// </remarks>
internal sealed class MuxModuleViewModel : IArchiveConfigurationAwareModule
{
    public MuxModuleViewModel(
        SingleEpisodeMuxViewModel singleMux,
        BatchMuxViewModel batchMux)
    {
        SingleMux = singleMux;
        BatchMux = batchMux;
    }

    /// <summary>
    /// ViewModel für den Einzel-Mux-Tab.
    /// </summary>
    public SingleEpisodeMuxViewModel SingleMux { get; }

    /// <summary>
    /// ViewModel für den Batch-Mux-Tab.
    /// </summary>
    public BatchMuxViewModel BatchMux { get; }

    /// <inheritdoc />
    public void HandleArchiveConfigurationChanged()
    {
        SingleMux.HandleArchiveConfigurationChanged();
        BatchMux.HandleArchiveConfigurationChanged();
    }
}
