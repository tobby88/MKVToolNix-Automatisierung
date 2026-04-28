using System.IO;
using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Tests.TestInfrastructure;

internal sealed class RecordingModuleLogService : IModuleLogService
{
    public List<RecordedModuleLog> Logs { get; } = [];

    public ModuleLogSaveResult SaveModuleLog(
        string moduleLabel,
        string operationLabel,
        string? context,
        string logText)
    {
        var path = Path.Combine(Path.GetTempPath(), "recorded-module-log-" + Logs.Count + ".log.txt");
        Logs.Add(new RecordedModuleLog(moduleLabel, operationLabel, context, logText, path));
        return new ModuleLogSaveResult(path);
    }
}

internal sealed record RecordedModuleLog(
    string ModuleLabel,
    string OperationLabel,
    string? Context,
    string LogText,
    string Path);
