using System.IO;
using System.Reflection;
using System.Windows;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

[Collection("PortableStorage")]
public sealed class BatchMuxViewModelLoggingTests
{
    private readonly PortableStorageFixture _storageFixture;

    public BatchMuxViewModelLoggingTests(PortableStorageFixture storageFixture)
    {
        _storageFixture = storageFixture;
        _storageFixture.Reset();

        if (Application.Current is null)
        {
            _ = new Application();
        }
    }

    [Fact]
    public void PersistBatchRunArtifacts_UsesCurrentBatchRunBufferInsteadOfWholeUiLog()
    {
        var viewModel = new BatchMuxViewModel(
            new AppServices(
                SeriesEpisodeMux: null!,
                EpisodePlans: null!,
                BatchScan: null!,
                Archive: null!,
                OutputPaths: null!,
                CleanupFiles: null!,
                EpisodeMetadata: null!,
                FileCopy: null!,
                Cleanup: null!,
                MuxWorkflow: null!,
                BatchLogs: new BatchRunLogService()),
            new UserDialogService());
        var outputDirectory = CreateDirectory("output");
        var sourceDirectory = CreateDirectory("source");
        var createdOutputFile = Path.Combine(outputDirectory, "Episode.mkv");
        File.WriteAllText(createdOutputFile, "video");

        SetPrivateField(viewModel, "_sourceDirectory", sourceDirectory);
        SetPrivateField(viewModel, "_outputDirectory", outputDirectory);

        InvokePrivateMethod(viewModel, "AppendLog", "SCAN: Episode erkannt");

        var batchRunLogBuffer = new BufferedTextStore(static flush => flush(), static _ => { });
        batchRunLogBuffer.AppendLine("KOPIEREN: Arbeitskopie vorbereitet");
        batchRunLogBuffer.AppendLine("DONE: Episode");

        var appendBatchRunLog = new Action<string>(batchRunLogBuffer.AppendLine);
        var result = InvokePersistBatchRunArtifacts(
            viewModel,
            [createdOutputFile],
            successCount: 1,
            warningCount: 0,
            errorCount: 0,
            batchRunLogBuffer,
            appendBatchRunLog);

        var savedLog = File.ReadAllText(result.BatchLogPath);

        Assert.Contains("KOPIEREN: Arbeitskopie vorbereitet", savedLog);
        Assert.Contains("DONE: Episode", savedLog);
        Assert.DoesNotContain("SCAN: Episode erkannt", savedLog);
    }

    private static BatchRunLogSaveResult InvokePersistBatchRunArtifacts(
        BatchMuxViewModel viewModel,
        IReadOnlyList<string> newOutputFiles,
        int successCount,
        int warningCount,
        int errorCount,
        BufferedTextStore batchRunLogBuffer,
        Action<string> appendBatchRunLog)
    {
        var method = typeof(BatchMuxViewModel).GetMethod(
            "PersistBatchRunArtifacts",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        return Assert.IsType<BatchRunLogSaveResult>(method!.Invoke(viewModel,
        [
            newOutputFiles,
            successCount,
            warningCount,
            errorCount,
            batchRunLogBuffer,
            appendBatchRunLog
        ]));
    }

    private static void InvokePrivateMethod(BatchMuxViewModel viewModel, string methodName, params object[] arguments)
    {
        var method = typeof(BatchMuxViewModel).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        _ = method!.Invoke(viewModel, arguments);
    }

    private static void SetPrivateField<T>(BatchMuxViewModel viewModel, string fieldName, T value)
    {
        var field = typeof(BatchMuxViewModel).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(viewModel, value);
    }

    private static string CreateDirectory(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "mkv-auto-batch-log-tests", Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(path);
        return path;
    }
}
