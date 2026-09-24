namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Optimistische Aktualisierung kleiner Metadaten-Dateien: gelesen wird ein begrenzter
/// Snapshot, veröffentlicht nur bei identischem Inhalt unter exklusiver Windows-Dateisperre.
/// Anders als File.Replace kann kein fremder Writer im letzten Compare/Write-Fenster gewinnen.
/// </summary>
/// <remarks>
/// Die kurze In-place-Veröffentlichung braucht eine vorab auf Datenträger geflushte Sicherung.
/// Bei regulären Fehlern wird sie unter derselben Sperre zurückgeschrieben. Bei Prozessabsturz
/// bleibt die eindeutig benannte .edit-*.tmp-Datei zur expliziten Wiederherstellung bestehen.
/// Diese Klasse ist nur für NFO/JSON gedacht, niemals für große Mediendateien.
/// </remarks>
internal sealed class SmallFileUpdate
{
    private const int MaximumBytes = 64 * 1024 * 1024;
    private readonly string _path;
    private readonly byte[] _original;

    public SmallFileUpdate(string path)
    {
        _path = Path.GetFullPath(path);
        FileMutationSafety.EnsureOrdinaryFile(_path);
        using var stream = File.OpenRead(_path);
        _original = ReadBounded(stream);
    }

    /// <summary>Unveränderbarer Lesestream des Zustands, auf dem die Bearbeitung basiert.</summary>
    public Stream OpenRead() => new MemoryStream(_original, writable: false);

    /// <summary>
    /// Veröffentlicht nur den unverändert vorgefundenen Snapshot. Konflikte werden nicht
    /// automatisch wiederholt: Der Benutzer muss die inzwischen geänderten Metadaten prüfen.
    /// </summary>
    public void Commit(byte[] updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        if (updated.Length > MaximumBytes) throw new IOException("Die Metadaten-Datei überschreitet das 64-MiB-Limit.");
        FileMutationSafety.EnsureOrdinaryFile(_path);
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (!_original.AsSpan().SequenceEqual(ReadBounded(stream)))
            throw new IOException($"Die Datei wurde während der Bearbeitung geändert und nicht überschrieben. Bitte neu prüfen: {_path}");
        if (_original.AsSpan().SequenceEqual(updated)) return;

        var backupPath = Path.Combine(Path.GetDirectoryName(_path)!, $".{Path.GetFileName(_path)}.edit-{Guid.NewGuid():N}.tmp");
        using (var backup = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            backup.Write(_original);
            backup.Flush(flushToDisk: true);
        }

        try
        {
            WriteComplete(stream, updated);
        }
        catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException)
        {
            try { WriteComplete(stream, _original); }
            catch (Exception restoreError) when (restoreError is IOException or UnauthorizedAccessException)
            {
                throw new IOException($"Schreiben und Rücknahme fehlgeschlagen. Original gesichert unter '{backupPath}'.",
                    new AggregateException(writeError, restoreError));
            }
            TryDeleteBackup(backupPath);
            throw;
        }
        TryDeleteBackup(backupPath);
    }

    private static byte[] ReadBounded(FileStream stream)
    {
        if (stream.Length > MaximumBytes) throw new IOException("Die Metadaten-Datei überschreitet das 64-MiB-Limit.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static void WriteComplete(FileStream stream, byte[] bytes)
    {
        stream.Position = 0;
        stream.Write(bytes);
        stream.SetLength(bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static void TryDeleteBackup(string path)
    {
        // Eine nach erfolgreichem Flush nicht löschbare Sicherung ist kein Schreibfehler.
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
