using System.IO;
using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class MuxExecutionResultClassifierTests
{
    [Fact]
    public void Classify_ReturnsSuccess_ForCleanExitCodeZero()
    {
        var result = MuxExecutionResultClassifier.Classify(
            new MuxExecutionResult(0, HasWarning: false, LastProgressPercent: 100),
            outputSnapshotBeforeRun: null,
            outputPath: Path.Combine(Path.GetTempPath(), "nicht-erzeugt.mkv"));

        Assert.Equal(MuxExecutionOutcomeKind.Success, result);
    }

    [Fact]
    public void Classify_ReturnsWarning_ForExitCodeZeroWithToolWarning()
    {
        var result = MuxExecutionResultClassifier.Classify(
            new MuxExecutionResult(0, HasWarning: true, LastProgressPercent: 100),
            outputSnapshotBeforeRun: null,
            outputPath: Path.Combine(Path.GetTempPath(), "nicht-erzeugt.mkv"));

        Assert.Equal(MuxExecutionOutcomeKind.Warning, result);
    }

    [Fact]
    public void Classify_ReturnsWarning_ForExitCodeOneWhenOutputWasCreated()
    {
        var outputPath = CreateTemporaryOutputPath();
        try
        {
            File.WriteAllText(outputPath, "neu");

            var result = MuxExecutionResultClassifier.Classify(
                new MuxExecutionResult(1, HasWarning: false, LastProgressPercent: 100),
                outputSnapshotBeforeRun: null,
                outputPath);

            Assert.Equal(MuxExecutionOutcomeKind.Warning, result);
        }
        finally
        {
            DeleteIfExists(outputPath);
        }
    }

    [Fact]
    public void Classify_ReturnsWarning_ForExitCodeOneWhenExistingOutputWasChanged()
    {
        var outputPath = CreateTemporaryOutputPath();
        try
        {
            File.WriteAllText(outputPath, "alt");
            var snapshotBeforeRun = FileStateSnapshot.TryCreate(outputPath);

            File.AppendAllText(outputPath, "-neu");

            var result = MuxExecutionResultClassifier.Classify(
                new MuxExecutionResult(1, HasWarning: false, LastProgressPercent: 100),
                snapshotBeforeRun,
                outputPath);

            Assert.Equal(MuxExecutionOutcomeKind.Warning, result);
        }
        finally
        {
            DeleteIfExists(outputPath);
        }
    }

    [Fact]
    public void Classify_ReturnsError_ForExitCodeOneWhenExistingOutputWasUnchanged()
    {
        var outputPath = CreateTemporaryOutputPath();
        try
        {
            File.WriteAllText(outputPath, "unveraendert");
            var snapshotBeforeRun = FileStateSnapshot.TryCreate(outputPath);

            var result = MuxExecutionResultClassifier.Classify(
                new MuxExecutionResult(1, HasWarning: false, LastProgressPercent: 100),
                snapshotBeforeRun,
                outputPath);

            Assert.Equal(MuxExecutionOutcomeKind.Error, result);
        }
        finally
        {
            DeleteIfExists(outputPath);
        }
    }

    private static string CreateTemporaryOutputPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mkv-auto-result-classifier", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "output.mkv");
    }

    private static void DeleteIfExists(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
