using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

public sealed class SeriesEpisodeMuxService
{
    private readonly SeriesEpisodeMuxPlanner _planner;
    private readonly MuxExecutionService _executionService;
    private readonly MkvMergeOutputParser _outputParser;

    public SeriesEpisodeMuxService(
        SeriesEpisodeMuxPlanner planner,
        MuxExecutionService executionService,
        MkvMergeOutputParser outputParser)
    {
        _planner = planner;
        _executionService = executionService;
        _outputParser = outputParser;
    }

    public AutoDetectedEpisodeFiles DetectFromMainVideo(string mainVideoPath)
    {
        return _planner.DetectFromMainVideo(mainVideoPath);
    }

    public Task<AutoDetectedEpisodeFiles> DetectFromSelectedVideoAsync(
        string selectedVideoPath,
        Action<DetectionProgressUpdate>? onProgress = null,
        IReadOnlyCollection<string>? excludedSourcePaths = null)
    {
        return RunOnStaThreadAsync(() => _planner.DetectFromMainVideo(selectedVideoPath, onProgress, excludedSourcePaths));
    }

    public Task<SeriesEpisodeMuxPlan> CreatePlanAsync(SeriesEpisodeMuxRequest request)
    {
        return _planner.CreatePlanAsync(request);
    }

    public string BuildPreviewText(SeriesEpisodeMuxPlan plan)
    {
        return plan.BuildPreviewText();
    }

    public void InvalidatePlanningCaches()
    {
        _planner.InvalidatePlanningCaches();
    }

    public void InvalidatePlanOutputs(SeriesEpisodeMuxPlan? plan)
    {
        if (plan is null)
        {
            return;
        }

        _planner.InvalidateProbeCaches(
        [
            plan.OutputFilePath,
            plan.WorkingCopy?.DestinationFilePath
        ]);
    }

    public async Task<MuxExecutionResult> ExecuteAsync(
        SeriesEpisodeMuxPlan plan,
        Action<string>? onOutput = null,
        Action<MuxExecutionUpdate>? onUpdate = null)
    {
        var hadWarning = false;
        int? latestProgressPercent = null;

        onUpdate?.Invoke(new MuxExecutionUpdate(0, false));

        var exitCode = await _executionService.ExecuteAsync(
            plan.MkvMergePath,
            plan.BuildArguments(),
            line =>
            {
                onOutput?.Invoke(line);

                var parsedOutput = _outputParser.Parse(line);
                if (parsedOutput.ProgressPercent is null && !parsedOutput.IsWarning)
                {
                    return;
                }

                if (parsedOutput.ProgressPercent is int progressPercent)
                {
                    latestProgressPercent = progressPercent;
                }

                if (parsedOutput.IsWarning)
                {
                    hadWarning = true;
                }

                onUpdate?.Invoke(new MuxExecutionUpdate(latestProgressPercent, hadWarning));
            });

        return new MuxExecutionResult(exitCode, hadWarning, latestProgressPercent);
    }

    private static Task<T> RunOnStaThreadAsync<T>(Func<T> action)
    {
        var completionSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                completionSource.SetResult(action());
            }
            catch (Exception ex)
            {
                completionSource.SetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        return completionSource.Task;
    }
}

public sealed record MuxExecutionUpdate(int? ProgressPercent, bool HasWarning);

public sealed record MuxExecutionResult(int ExitCode, bool HasWarning, int? LastProgressPercent);
