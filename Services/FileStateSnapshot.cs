namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Minimaler Dateisnapshot, um Cache-Einträge an Größe und Änderungszeit zu koppeln.
/// </summary>
internal readonly record struct FileStateSnapshot(long Length, DateTime LastWriteTimeUtc)
{
    public static FileStateSnapshot? TryCreate(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var info = new FileInfo(filePath);
            return new FileStateSnapshot(info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>
/// Hält einen gecachten Wert zusammen mit dem Dateisnapshot, aus dem er abgeleitet wurde.
/// </summary>
internal sealed record CachedFileValue<T>(FileStateSnapshot Snapshot, T Value)
{
    public bool Matches(FileStateSnapshot? snapshot)
    {
        return snapshot is not null && Snapshot.Equals(snapshot.Value);
    }
}
