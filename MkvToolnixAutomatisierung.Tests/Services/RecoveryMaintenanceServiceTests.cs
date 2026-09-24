using System.Security.Cryptography;
using System.IO;
using System.Text;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class RecoveryMaintenanceServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mkv-recovery-" + Guid.NewGuid().ToString("N"));
    public RecoveryMaintenanceServiceTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void Scan_ExcludesActiveVersionAndUnknownFilesButIncludesKnownStagingAndJournal()
    {
        var toolRoot = Path.Combine(_root, "mediathekview");
        var active = Path.Combine(toolRoot, "15.0.0", "MediathekView.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(active)!);
        File.WriteAllText(active, "active");
        Directory.CreateDirectory(Path.Combine(toolRoot, "14.0.0"));
        Directory.CreateDirectory(Path.Combine(_root, ".staging-" + Guid.NewGuid().ToString("N")));
        File.WriteAllText(Path.Combine(_root, "personal.json"), "personal");
        File.WriteAllText(Path.Combine(_root, ".archive-change-" + Guid.NewGuid().ToString("N") + ".json"), "{}");
        var service = new RecoveryMaintenanceService([active], toolRoot);
        var entries = service.Scan(_root);
        Assert.Equal(3, entries.Count);
        Assert.DoesNotContain(entries, entry => entry.Path.Contains("15.0.0") || entry.Path.EndsWith("personal.json"));
    }

    [Fact]
    public void RestoreMetadata_PreservesCurrentContentsAndRejectsChangedBackup()
    {
        var target = Path.Combine(_root, "Episode.nfo");
        var backup = WriteBackup(target, "<episodedetails><title>Old</title></episodedetails>");
        File.WriteAllText(target, "<episodedetails><title>New</title></episodedetails>");
        var service = new RecoveryMaintenanceService([]);
        var entry = Assert.Single(service.Scan(_root));
        Assert.True(entry.CanRestore);
        var result = service.RestoreMetadata(entry);
        Assert.Contains("wiederhergestellt", result);
        Assert.Contains("Old", File.ReadAllText(target));
        var undo = Assert.Single(Directory.GetFiles(_root, "*.before-recovery-*.tmp"));
        Assert.Contains("New", File.ReadAllText(undo));
        Assert.True(File.Exists(backup));
        File.AppendAllText(backup, "changed");
        Assert.Throws<IOException>(() => service.RestoreMetadata(entry));
    }

    [Fact]
    public void RestoreMetadata_RejectsIncompleteOrUnverifiedBackup()
    {
        var target = Path.Combine(_root, "Episode.nfo");
        var backup = WriteBackup(target, "complete");
        File.WriteAllText(backup, "partial");
        var service = new RecoveryMaintenanceService([]);
        var entry = Assert.Single(service.Scan(_root));
        Assert.Throws<IOException>(() => service.RestoreMetadata(entry));
        Assert.False(File.Exists(target));
        File.Move(backup, backup + ".writing");
        Assert.False(Assert.Single(service.Scan(_root)).CanRestore);
    }

    private static string WriteBackup(string target, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var path = Path.Combine(Path.GetDirectoryName(target)!, $".{Path.GetFileName(target)}.edit-{Guid.NewGuid():N}-{Convert.ToHexString(SHA256.HashData(bytes))}.tmp");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
