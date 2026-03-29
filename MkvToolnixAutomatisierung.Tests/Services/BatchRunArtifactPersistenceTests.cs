using System.IO;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

[Collection("PortableStorage")]
public sealed class BatchRunArtifactPersistenceTests
{
    private readonly PortableStorageFixture _storageFixture;

    public BatchRunArtifactPersistenceTests(PortableStorageFixture storageFixture)
    {
        _storageFixture = storageFixture;
        _storageFixture.Reset();
    }

    [Fact]
    public void Persist_UsesCurrentBatchRunBufferInsteadOfWholeUiLog()
    {
        var outputDirectory = CreateDirectory("output");
        var sourceDirectory = CreateDirectory("source");
        var createdOutputFile = Path.Combine(outputDirectory, "Episode.mkv");
        File.WriteAllText(createdOutputFile, "video");

        var uiLogBuffer = new BufferedTextStore(static flush => flush(), static _ => { });
        uiLogBuffer.AppendLine("SCAN: Episode erkannt");

        var batchRunLogBuffer = new BufferedTextStore(static flush => flush(), static _ => { });
        batchRunLogBuffer.AppendLine("KOPIEREN: Arbeitskopie vorbereitet");
        batchRunLogBuffer.AppendLine("DONE: Episode");

        var result = BatchRunArtifactPersistence.Persist(
            new BatchRunLogService(),
            sourceDirectory,
            outputDirectory,
            [createdOutputFile],
            successCount: 1,
            warningCount: 0,
            errorCount: 0,
            batchRunLogBuffer,
            uiLogBuffer.AppendLine);

        var savedLog = File.ReadAllText(result.BatchLogPath);

        Assert.Contains("KOPIEREN: Arbeitskopie vorbereitet", savedLog);
        Assert.Contains("DONE: Episode", savedLog);
        Assert.DoesNotContain("SCAN: Episode erkannt", savedLog);
    }

    private static string CreateDirectory(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "mkv-auto-batch-log-tests", Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(path);
        return path;
    }
}
