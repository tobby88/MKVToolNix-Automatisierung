namespace MkvToolnixAutomatisierung.Services;

public sealed record AppServices(
    MkvToolNixLocator Locator,
    MkvMergeProbeService ProbeService,
    MuxExecutionService ExecutionService);
