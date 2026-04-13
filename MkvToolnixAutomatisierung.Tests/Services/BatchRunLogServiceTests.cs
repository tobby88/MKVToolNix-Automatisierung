using System.IO;
using System.Text.Json;
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
        Assert.NotNull(result.NewOutputMetadataReportPath);
        Assert.True(File.Exists(result.NewOutputMetadataReportPath));

        var batchLogText = File.ReadAllText(result.BatchLogPath);
        Assert.Contains("MKVToolNix-Automatisierung - Batch-Log", batchLogText);
        Assert.Contains($"Quellordner: {sourceDirectory}", batchLogText);
        Assert.Contains($"Ausgabeordner: {outputDirectory}", batchLogText);
        Assert.Contains("Ergebnis: 1 erfolgreich, 0 Warnung(en), 0 Fehler", batchLogText);
        Assert.Contains(createdOutputFile, batchLogText);
        Assert.Contains("Strukturierter Metadaten-Report:", batchLogText);
        Assert.Contains(result.NewOutputMetadataReportPath!, batchLogText);
        Assert.Contains("Batch-Protokoll:", batchLogText);
        Assert.Contains("STARTE: Episode", batchLogText);

        var newFilesReportText = File.ReadAllText(result.NewOutputListPath!);
        Assert.Contains("Neu erzeugte Ausgabedateien", newFilesReportText);
        Assert.Contains(createdOutputFile, newFilesReportText);
        Assert.Equal(result.NewOutputListPath, result.PreferredOpenPath);

        var metadataReportText = File.ReadAllText(result.NewOutputMetadataReportPath!);
        Assert.Contains("\"schemaVersion\": 1", metadataReportText);
        Assert.Contains(createdOutputFile.Replace("\\", "\\\\"), metadataReportText);
    }

    [Fact]
    public void SaveBatchRunArtifacts_WritesStructuredMetadataReportWithTvdbId()
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
            [createdOutputFile],
            successCount: 1,
            warningCount: 0,
            errorCount: 0,
            newOutputMetadata:
            [
                new BatchOutputMetadataEntry
                {
                    OutputPath = createdOutputFile,
                    SeriesName = "Beispielserie",
                    SeasonNumber = "01",
                    EpisodeNumber = "02",
                    EpisodeTitle = "Pilot",
                    ProviderIds = new BatchOutputProviderIds
                    {
                        Tvdb = "100"
                    },
                    Tvdb = new BatchOutputTvdbMetadata
                    {
                        SeriesId = 42,
                        SeriesName = "Beispielserie",
                        EpisodeId = 100
                    }
                }
            ]);

        Assert.NotNull(result.NewOutputMetadataReportPath);

        using var document = JsonDocument.Parse(File.ReadAllText(result.NewOutputMetadataReportPath!));
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(createdOutputFile, item.GetProperty("outputPath").GetString());
        Assert.Equal("100", item.GetProperty("providerIds").GetProperty("tvdb").GetString());
        Assert.Equal(42, item.GetProperty("tvdb").GetProperty("seriesId").GetInt32());
        Assert.Equal(100, item.GetProperty("tvdb").GetProperty("episodeId").GetInt32());

        var batchLogText = File.ReadAllText(result.BatchLogPath);
        Assert.Contains("TVDB-Episode: 100", batchLogText);
        Assert.Contains("TVDB-Serie: 42", batchLogText);
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
        Assert.Null(result.NewOutputMetadataReportPath);
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
