using System.IO;
using System.Text;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class SmallFileUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "small-update-" + Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(_root, "episode.nfo");
    public SmallFileUpdateTests() { Directory.CreateDirectory(_root); File.WriteAllText(PathName, "original"); }

    [Fact]
    public void ChangedSameLengthAndTimestampIsNotOverwritten()
    {
        var timestamp = File.GetLastWriteTimeUtc(PathName);
        var update = new SmallFileUpdate(PathName);
        File.WriteAllText(PathName, "external");
        File.SetLastWriteTimeUtc(PathName, timestamp);
        Assert.Throws<IOException>(() => update.Commit(Encoding.UTF8.GetBytes("new")));
        Assert.Equal("external", File.ReadAllText(PathName));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public void ConcurrentWriterOrDeleteHandlePreventsCommit()
    {
        var update = new SmallFileUpdate(PathName);
        using (File.Open(PathName, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
            Assert.Throws<IOException>(() => update.Commit(Encoding.UTF8.GetBytes("new")));
        Assert.Equal("original", File.ReadAllText(PathName));
    }

    [Fact]
    public void CommitTruncatesAndRemovesOwnBackup()
    {
        new SmallFileUpdate(PathName).Commit(Encoding.UTF8.GetBytes("new"));
        Assert.Equal("new", File.ReadAllText(PathName));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public void NoOpPreservesBytesAndModificationTime()
    {
        var timestamp = File.GetLastWriteTimeUtc(PathName);
        new SmallFileUpdate(PathName).Commit(File.ReadAllBytes(PathName));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(PathName));
        Assert.Single(Directory.GetFiles(_root));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
