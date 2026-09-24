using System.IO;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class ReversibleFileMoveBatchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "move-package-" + Guid.NewGuid().ToString("N"));
    public ReversibleFileMoveBatchTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void LaterLockedSourceRestoresEarlierSourceAndReplacedTarget()
    {
        var video = Path.Combine(_root, "episode.mp4");
        var text = Path.ChangeExtension(video, ".txt");
        var target = Path.Combine(_root, "Serie", "episode.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(video, "new video");
        File.WriteAllText(text, "new text");
        File.WriteAllText(target, "old video");
        // Lesen bleibt möglich, Umbenennen/Löschen nicht: Der Fehler tritt nach dem ersten Move auf.
        using var locked = File.Open(text, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Throws<IOException>(() => ReversibleFileMoveBatch.Execute([
            new(video, target, true), new(text, Path.ChangeExtension(target, ".txt"), true)]));
        Assert.Equal("new video", File.ReadAllText(video));
        Assert.Equal("old video", File.ReadAllText(target));
        Assert.True(File.Exists(text));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void InvalidSelectedPackageDoesNotRenameExistingFolder()
    {
        var legacy = Path.Combine(_root, "Legacy");
        Directory.CreateDirectory(legacy);
        var missing = Path.Combine(_root, "episode.mp4");
        var result = new DownloadSortService().Apply(_root,
            [new DownloadSortMoveRequest("Episode", [missing], "Serie")],
            [new DownloadSortFolderRenamePlan("Legacy", "Serie", "test")]);
        Assert.Equal(0, result.RenamedFolderCount);
        Assert.True(Directory.Exists(legacy));
        Assert.False(Directory.Exists(Path.Combine(_root, "Serie")));
    }

    [Fact]
    public void DefectiveAndRegularPartsRollBackTogether()
    {
        var subtitle = Path.Combine(_root, "episode.srt");
        var video = Path.ChangeExtension(subtitle, ".mp4");
        File.WriteAllText(video, "defective");
        File.WriteAllText(subtitle, "subtitle");
        using var locked = File.Open(video, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Throws<IOException>(() => ReversibleFileMoveBatch.Execute([
            new(subtitle, Path.Combine(_root, "Serie", "episode.srt"), true),
            new(video, Path.Combine(_root, "defekt", "episode.mp4"), false)]));
        Assert.True(File.Exists(subtitle));
        Assert.True(File.Exists(video));
        Assert.False(Directory.Exists(Path.Combine(_root, "Serie")));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
