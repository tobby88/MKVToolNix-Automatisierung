namespace MkvToolnixAutomatisierung.Services;

internal readonly record struct FileStateSnapshot(long Length, DateTime LastWriteTimeUtc)
{
    public static FileStateSnapshot? TryCreate(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        var info = new FileInfo(filePath);
        return new FileStateSnapshot(info.Length, info.LastWriteTimeUtc);
    }
}

internal sealed record CachedFileValue<T>(FileStateSnapshot Snapshot, T Value)
{
    public bool Matches(FileStateSnapshot? snapshot)
    {
        return snapshot is not null && Snapshot.Equals(snapshot.Value);
    }
}
