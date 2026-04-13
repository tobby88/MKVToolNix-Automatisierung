using System.IO;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

[Collection("PortableStorage")]
public sealed class BatchRunLogServiceTests
{
    private readonly PortableStorageFixture _storageFixture;

    public BatchRunLogServiceTests(PortableStorageFixture storageFixture)
    {
        _storageFixture = storageFixture;
        _storageFixture.Reset();
    }

    [Fact]
    public void SaveBatchRunArtifacts_WritesBatchLogAndNewOutputListForExistingFiles()
    {
        var service = new BatchRunLogService();
        var outputDirectory = CreateDirectory("output");
        var sourceDirectory = CreateDirectory("source");
        var createdOutputFile = Path.Combine(outputDirectory, "Episode.mkv");
        File.WriteAllText(createdOutputFile, "video");

        var result = service.SaveBatchRunArtifacts(
            sourceDirectory,
            outputDirectory,
            "STARTE: Episode",
            [createdOutputFile, createdOutputFile, Path.Combine(outputDirectory, "missing.mkv")],
            successCount: 1,
            warningCount: 0,
            errorCount: 0);

        Assert.StartsWith(PortableAppStorage.LogsDirectory, result.BatchLogPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(result.BatchLogPath));
        Assert.Single(result.NewOutputFiles);
        Assert.Equal(createdOutputFile, result.NewOutputFiles[0]);
        Assert.NotNull(result.NewOutputListPath);
        Assert.True(File.Exists(result.NewOutputListPath));

        var batchLogText = File.ReadAllText(result.BatchLogPath);
        Assert.Contains("MKVToolNix-Automatisierung - Batch-Log", batchLogText);
        Assert.Contains($"Quellordner: {sourceDirectory}", batchLogText);
        Assert.Contains($"Ausgabeordner: {outputDirectory}", batchLogText);
        Assert.Contains("Ergebnis: 1 erfolgreich, 0 Warnung(en), 0 Fehler", batchLogText);
        Assert.Contains(createdOutputFile, batchLogText);
        Assert.Contains("Batch-Protokoll:", batchLogText);
        Assert.Contains("STARTE: Episode", batchLogText);

        var newFilesReportText = File.ReadAllText(result.NewOutputListPath!);
        Assert.Contains("Neu erzeugte Ausgabedateien", newFilesReportText);
        Assert.Contains(createdOutputFile, newFilesReportText);
        Assert.Equal(result.NewOutputListPath, result.PreferredOpenPath);
    }

    [Fact]
    public void SaveBatchRunArtifacts_DoesNotCreateNewOutputListWhenNoExistingOutputFilesRemain()
    {
        var service = new BatchRunLogService();
        var outputDirectory = CreateDirectory("output");
        var sourceDirectory = CreateDirectory("source");

        var result = service.SaveBatchRunArtifacts(
            sourceDirectory,
            outputDirectory,
            string.Empty,
            [Path.Combine(outputDirectory, "missing.mkv")],
            successCount: 0,
            warningCount: 1,
            errorCount: 2);

        Assert.True(File.Exists(result.BatchLogPath));
        Assert.Null(result.NewOutputListPath);
        Assert.Empty(result.NewOutputFiles);
        Assert.Null(result.PreferredOpenPath);

        var batchLogText = File.ReadAllText(result.BatchLogPath);
        Assert.Contains("Neu erzeugte Ausgabedateien:", batchLogText);
        Assert.Contains("(keine)", batchLogText);
        Assert.Contains("Batch-Protokoll:", batchLogText);
        Assert.Contains("(leer)", batchLogText);
    }

    [Fact]
    public void SaveBatchRunArtifacts_RepairsMojibakeInPersistedLogText()
    {
        var service = new BatchRunLogService();
        var outputDirectory = CreateDirectory("output");
        var sourceDirectory = CreateDirectory("source");

        var result = service.SaveBatchRunArtifacts(
            sourceDirectory,
            outputDirectory,
            "WARNUNG: Deutsch (hÃ¶rgeschÃ¤digte) - WebVTT",
            [],
            successCount: 0,
            warningCount: 1,
            errorCount: 0);

        var batchLogText = File.ReadAllText(result.BatchLogPath);
        Assert.Contains("Deutsch (hörgeschädigte) - WebVTT", batchLogText);
        Assert.DoesNotContain("hÃ¶rgeschÃ¤digte", batchLogText, StringComparison.Ordinal);
    }

    private static string CreateDirectory(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "mkv-auto-tests", Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(path);
        return path;
    }
}
