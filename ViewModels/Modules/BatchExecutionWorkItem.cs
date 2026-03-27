using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Verknüpft UI-Zeile, fertigen Mux-Plan und aufzuräumende Quelldateien für einen Batch-Lauf.
/// </summary>
internal sealed record BatchExecutionWorkItem(
    BatchEpisodeItemViewModel Item,
    SeriesEpisodeMuxPlan Plan,
    IReadOnlyList<string> CleanupFiles);
