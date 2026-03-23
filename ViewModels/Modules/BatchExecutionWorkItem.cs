using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.ViewModels.Modules;

internal sealed record BatchExecutionWorkItem(
    BatchEpisodeItemViewModel Item,
    SeriesEpisodeMuxPlan Plan,
    IReadOnlyList<string> CleanupFiles);
