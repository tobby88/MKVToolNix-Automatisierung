using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

public interface IEpisodePlanInput
{
    string MainVideoPath { get; }
    string? AudioDescriptionPath { get; }
    IReadOnlyList<string> SubtitlePaths { get; }
    IReadOnlyList<string> AttachmentPaths { get; }
    string OutputPath { get; }
    string TitleForMux { get; }
}

public sealed class EpisodePlanCoordinator
{
    private readonly SeriesEpisodeMuxService _muxService;

    public EpisodePlanCoordinator(SeriesEpisodeMuxService muxService)
    {
        _muxService = muxService;
    }

    public Task<SeriesEpisodeMuxPlan> BuildPlanAsync(IEpisodePlanInput input)
    {
        return _muxService.CreatePlanAsync(new SeriesEpisodeMuxRequest(
            input.MainVideoPath,
            input.AudioDescriptionPath,
            input.SubtitlePaths,
            input.AttachmentPaths,
            input.OutputPath,
            input.TitleForMux));
    }
}
