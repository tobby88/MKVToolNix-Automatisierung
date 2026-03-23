using System.IO;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class FileStateSnapshotTests : IDisposable
{
    private readonly string _tempDirectory;

    public FileStateSnapshotTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "mkv-auto-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void CachedFileValue_MatchesOnlyUnchangedFileSnapshot()
    {
        var filePath = Path.Combine(_tempDirectory, "episode.mp4");
        File.WriteAllText(filePath, "first");
        var initialSnapshot = FileStateSnapshot.TryCreate(filePath);

        Assert.NotNull(initialSnapshot);

        var cachedValue = new CachedFileValue<string>(initialSnapshot!.Value, "cached");

        Assert.True(cachedValue.Matches(FileStateSnapshot.TryCreate(filePath)));

        File.WriteAllText(filePath, "second version");
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddSeconds(1));

        Assert.False(cachedValue.Matches(FileStateSnapshot.TryCreate(filePath)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
