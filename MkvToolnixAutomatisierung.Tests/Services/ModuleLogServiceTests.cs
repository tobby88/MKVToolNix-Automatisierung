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
        Assert.Contains("MKVToolNix-Automatisierung - Archivpflege-Protokoll", logText, StringComparison.Ordinal);
        Assert.Contains("Vorgang: Scan", logText, StringComparison.Ordinal);
        Assert.Contains(@"Z:\Videos\Serien\Der Alte", logText, StringComparison.Ordinal);
        Assert.Contains("SCAN: 2 Datei(en)", logText, StringComparison.Ordinal);
        Assert.Contains("Deutsch (hörgeschädigte)", logText, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveModuleLog_CreatesUniqueFilesForSameSecond()
    {
        var service = new ModuleLogService();

        var first = service.SaveModuleLog("Emby-Abgleich", "Sync", null, "eins");
        var second = service.SaveModuleLog("Emby-Abgleich", "Sync", null, "zwei");

        Assert.NotEqual(first.LogPath, second.LogPath);
        Assert.True(File.Exists(first.LogPath));
        Assert.True(File.Exists(second.LogPath));
    }
}
