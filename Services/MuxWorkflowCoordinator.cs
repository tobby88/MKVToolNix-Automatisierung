using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

public sealed class MuxWorkflowCoordinator
{
    private readonly SeriesEpisodeMuxService _muxService;
    private readonly FileCopyService _fileCopyService;
    private readonly EpisodeCleanupService _cleanupService;

    public MuxWorkflowCoordinator(
        SeriesEpisodeMuxService muxService,
        FileCopyService fileCopyService,
        EpisodeCleanupService cleanupService)
    {
        _muxService = muxService;
        _fileCopyService = fileCopyService;
        _cleanupService = cleanupService;
    }

    public bool NeedsWorkingCopyPreparation(SeriesEpisodeMuxPlan plan)
    {
        return plan.WorkingCopy is not null && _fileCopyService.NeedsCopy(plan.WorkingCopy);
    }

    public async Task PrepareWorkingCopyAsync(
        SeriesEpisodeMuxPlan plan,
        Action<WorkingCopyPreparationUpdate>? onUpdate = null,
        CancellationToken cancellationToken = default)
    {
        if (plan.WorkingCopy is null)
        {
            return;
        }

        if (!_fileCopyService.NeedsCopy(plan.WorkingCopy))
        {
            onUpdate?.Invoke(new WorkingCopyPreparationUpdate(100, ReusesExistingCopy: true));
            return;
        }

        onUpdate?.Invoke(new WorkingCopyPreparationUpdate(0, ReusesExistingCopy: false));

        await _fileCopyService.CopyAsync(
            plan.WorkingCopy,
            (copiedBytes, totalBytes) =>
            {
                var progress = totalBytes <= 0
                    ? 0
                    : (int)Math.Round(copiedBytes * 100d / totalBytes);
                onUpdate?.Invoke(new WorkingCopyPreparationUpdate(progress, ReusesExistingCopy: false));
            },
            cancellationToken);

        onUpdate?.Invoke(new WorkingCopyPreparationUpdate(100, ReusesExistingCopy: false));
    }

    public async Task<MuxExecutionResult> ExecuteMuxAsync(
        SeriesEpisodeMuxPlan plan,
        Action<string>? onOutput = null,
        Action<MuxExecutionUpdate>? onUpdate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await _muxService.ExecuteAsync(plan, onOutput, onUpdate);
        }
        finally
        {
            _muxService.InvalidatePlanOutputs(plan);
            _cleanupService.DeleteTemporaryFile(plan.WorkingCopy?.DestinationFilePath);
        }
    }
}

public sealed record WorkingCopyPreparationUpdate(int ProgressPercent, bool ReusesExistingCopy);
