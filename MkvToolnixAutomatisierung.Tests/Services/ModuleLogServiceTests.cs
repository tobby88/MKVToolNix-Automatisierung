using System.IO;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

[Collection("PortableStorage")]
public sealed class ModuleLogServiceTests
{
    private readonly PortableStorageFixture _storageFixture;

    public ModuleLogServiceTests(PortableStorageFixture storageFixture)
    {
        _storageFixture = storageFixture;
        _storageFixture.Reset();
    }

    [Fact]
    public void SaveModuleLog_WritesGeneralModuleProtocol()
    {
        var service = new ModuleLogService();

        var result = service.SaveModuleLog(
            "Archivpflege",
            "Scan",
            @"Z:\Videos\Serien\Der Alte",
            "SCAN: 2 Datei(en)\r\nWARNUNG: Deutsch (hÃ¶rgeschÃ¤digte)");

        Assert.StartsWith(PortableAppStorage.LogsDirectory, result.LogPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(result.LogPath));

        var logText = File.ReadAllText(result.LogPath);
        Assert.Contains("MKVToolNix-Automatisierung - Sitzungsprotokoll", logText, StringComparison.Ordinal);
        Assert.Contains("Modul: Archivpflege", logText, StringComparison.Ordinal);
        Assert.Contains("Vorgang: Scan", logText, StringComparison.Ordinal);
        Assert.Contains(@"Z:\Videos\Serien\Der Alte", logText, StringComparison.Ordinal);
        Assert.Contains("SCAN: 2 Datei(en)", logText, StringComparison.Ordinal);
        Assert.Contains("Deutsch (hörgeschädigte)", logText, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveModuleLog_AppendsOperationSectionsToSessionLog()
    {
        var service = new ModuleLogService();

        var first = service.SaveModuleLog("Emby-Abgleich", "Sync", null, "eins");
        var second = service.SaveModuleLog("Emby-Abgleich", "Sync", null, "zwei");

        Assert.Equal(first.LogPath, second.LogPath);
        Assert.True(File.Exists(first.LogPath));

        var logText = File.ReadAllText(first.LogPath);
        Assert.Contains("Vorgang: Sync", logText, StringComparison.Ordinal);
        Assert.Contains("eins", logText, StringComparison.Ordinal);
        Assert.Contains("zwei", logText, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveModuleLog_AppendsOnlyNewLinesForSameModuleContext()
    {
        var service = new ModuleLogService();

        var first = service.SaveModuleLog("Archivpflege", "Scan", @"Z:\Archiv", "SCAN: abgeschlossen");
        service.SaveModuleLog("Archivpflege", "Korrektur", @"Z:\Archiv", "SCAN: abgeschlossen\r\nTVDB gewählt: Folge -> 123");

        var logText = File.ReadAllText(first.LogPath);
        Assert.Equal(1, CountOccurrences(logText, "SCAN: abgeschlossen"));
        Assert.Contains("TVDB gewählt: Folge -> 123", logText, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveModuleLog_FiltersRoutineToolAndProgressLines()
    {
        var service = new ModuleLogService();

        var result = service.SaveModuleLog(
            "Archivpflege",
            "Änderungen anwenden",
            null,
            "The file is being analyzed.\r\nProgress: 17%\r\nHeader aktualisiert.\r\nDone.");

        var logText = File.ReadAllText(result.LogPath);
        Assert.DoesNotContain("The file is being analyzed.", logText, StringComparison.Ordinal);
        Assert.DoesNotContain("Progress: 17%", logText, StringComparison.Ordinal);
        Assert.DoesNotContain("Done.", logText, StringComparison.Ordinal);
        Assert.Contains("Header aktualisiert.", logText, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var startIndex = 0;
        while ((startIndex = text.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += value.Length;
        }

        return count;
    }
}
