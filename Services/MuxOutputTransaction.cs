namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Schreibt einen Remux zunächst neben das Ziel und veröffentlicht ihn erst nach Erfolg.
/// So bleiben vorhandene Archivdateien auch bei Abbruch oder Werkzeugfehler unverändert.
/// Direkte Header-Edits verwenden diese Transaktion nicht.
/// </summary>
internal sealed class MuxOutputTransaction : IDisposable
{
    private readonly string _outputPath;
    private readonly string _stagingDirectory;
    private readonly FileStateSnapshot? _originalState;

    public MuxOutputTransaction(string outputPath)
    {
        _outputPath = Path.GetFullPath(outputPath);
        FileMutationSafety.EnsureOrdinaryFile(_outputPath);
        _originalState = FileStateSnapshot.TryCreate(_outputPath);
        var outputDirectory = Path.GetDirectoryName(_outputPath)!;
        // Gleicher Zielordner/Datenträger: Die Veröffentlichung benötigt keinen zweiten
        // vollständigen Kopiervorgang, auch nicht bei großen Dateien im Netzwerkarchiv.
        _stagingDirectory = Path.Combine(outputDirectory, $".mux-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_stagingDirectory);
        // Keine Medienendung im überwachten Archiv: Emby darf die laufende Ausgabe
        // nicht als zweite Episode einlesen. mkvmerge braucht keine .mkv-Endung.
        TemporaryOutputPath = Path.Combine(_stagingDirectory, Path.GetFileName(_outputPath) + ".tmp");
    }

    /// <summary>Ausgabepfad für den laufenden mkvmerge-Prozess.</summary>
    public string TemporaryOutputPath { get; }

    /// <summary>
    /// Ersetzt die Zieldatei erst nach vollständiger Ausgabe und unverändertem Zielzustand.
    /// Ein inzwischen von anderer Stelle angelegtes oder geändertes Ziel wird nicht überschrieben.
    /// </summary>
    public void Commit(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileMutationSafety.EnsureOrdinaryFile(_outputPath);
        if (!File.Exists(TemporaryOutputPath) || new FileInfo(TemporaryOutputPath).Length == 0)
        {
            throw new IOException("MKVToolNix hat keine vollständige Ausgabedatei erzeugt. Die bisherige Zieldatei bleibt unverändert.");
        }

        if (!Equals(_originalState, FileStateSnapshot.TryCreate(_outputPath)))
        {
            throw new IOException("Die Ziel-MKV wurde während des Muxens verändert. Sie wird nicht überschrieben; bitte erneut analysieren.");
        }

        File.Move(TemporaryOutputPath, _outputPath, overwrite: _originalState is not null);
    }

    /// <summary>Entfernt nur eigene temporäre Ausgabe; fremde Dateien werden nicht rekursiv gelöscht.</summary>
    public void Dispose()
    {
        try
        {
            File.Delete(TemporaryOutputPath);
            Directory.Delete(_stagingDirectory);
        }
        catch (IOException)
        {
            // Aufräumprobleme dürfen den ursprünglichen Werkzeugfehler nicht verdecken.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
