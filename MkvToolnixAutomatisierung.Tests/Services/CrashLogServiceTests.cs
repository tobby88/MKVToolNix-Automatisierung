using System;
using System.IO;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class CrashLogServiceTests
{
    [Fact]
    public void WriteCrashLog_WritesExceptionDetailsToRequestedDirectory()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "crash-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var exception = CreateExceptionWithStackTrace();

            var crashLogPath = CrashLogService.WriteCrashLog("Unit/Test", exception, tempDirectory);

            Assert.True(File.Exists(crashLogPath));
            Assert.Equal(tempDirectory, Path.GetDirectoryName(crashLogPath));
            Assert.Contains("Crash - ", Path.GetFileName(crashLogPath), StringComparison.Ordinal);
            Assert.Contains("Unit_Test", Path.GetFileName(crashLogPath), StringComparison.Ordinal);

            var content = File.ReadAllText(crashLogPath);
            Assert.Contains("MkvToolnixAutomatisierung Crash-Protokoll", content, StringComparison.Ordinal);
            Assert.Contains("Quelle: Unit/Test", content, StringComparison.Ordinal);
            Assert.Contains(nameof(InvalidOperationException), content, StringComparison.Ordinal);
            Assert.Contains("Testausnahme", content, StringComparison.Ordinal);
            Assert.Contains(nameof(CreateExceptionWithStackTrace), content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void TryWriteCrashLog_ReturnsNull_WhenTargetDirectoryCannotBeCreated()
    {
        var invalidDirectory = Path.Combine(Path.GetTempPath(), "crash-log-target-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(invalidDirectory, "blockiert");
        try
        {
            var crashLogPath = CrashLogService.TryWriteCrashLog("UnitTest", new InvalidOperationException("Test"), invalidDirectory);

            Assert.Null(crashLogPath);
        }
        finally
        {
            File.Delete(invalidDirectory);
        }
    }

    private static Exception CreateExceptionWithStackTrace()
    {
        try
        {
            throw new InvalidOperationException("Testausnahme");
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
