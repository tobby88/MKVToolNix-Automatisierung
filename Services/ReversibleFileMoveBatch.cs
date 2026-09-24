namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Verschiebt ein bereits fachlich geprüftes Paket als Einheit. Ersetzte Ziele bleiben
/// bis zum Abschluss als .tmp erhalten; bei normalen Fehlern werden alle Schritte umgekehrt.
/// Während eines Pakets wird kein Abbruch ausgelöst, sondern erst wieder zwischen Paketen.
/// </summary>
internal static class ReversibleFileMoveBatch
{
    internal sealed record Move(string Source, string Target, bool MayReplace);

    public static void Execute(IReadOnlyList<Move> moves)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var snapshots = new Dictionary<string, FileStateSnapshot?>(StringComparer.OrdinalIgnoreCase);
        foreach (var move in moves)
        {
            if (!targets.Add(Path.GetFullPath(move.Target))) throw new IOException("Mehrere Paketdateien haben dasselbe Ziel.");
            FileMutationSafety.EnsureOrdinaryFile(move.Source);
            FileMutationSafety.EnsureOrdinaryFile(move.Target);
            if (!File.Exists(move.Source)) throw new FileNotFoundException("Eine Paketquelle fehlt.", move.Source);
            if (Directory.Exists(move.Target) || (!move.MayReplace && File.Exists(move.Target)))
                throw new IOException($"Das Paketziel ist bereits belegt: {move.Target}");
            snapshots[move.Target] = FileStateSnapshot.TryCreate(move.Target);
        }

        var completed = new List<(Move Move, string? Backup, bool SourceMoved)>();
        var createdDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var move in moves)
            {
                var directory = Path.GetDirectoryName(move.Target)!;
                if (!Directory.Exists(directory)) { Directory.CreateDirectory(directory); createdDirectories.Add(directory); }
                if (!Equals(snapshots[move.Target], FileStateSnapshot.TryCreate(move.Target)))
                    throw new IOException($"Das Paketziel wurde inzwischen geändert: {move.Target}");
                string? backup = null;
                if (snapshots[move.Target] is not null)
                {
                    backup = Path.Combine(directory, $".{Path.GetFileName(move.Target)}.replace-{Guid.NewGuid():N}.tmp");
                    File.Move(move.Target, backup);
                }
                completed.Add((move, backup, false));
                // Niemals überschreiben: Ein zwischenzeitlich neu angelegtes Ziel ist ein Konflikt.
                File.Move(move.Source, move.Target);
                completed[^1] = (move, backup, true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            var failures = new List<string>();
            foreach (var step in completed.AsEnumerable().Reverse())
            {
                try
                {
                    if (step.SourceMoved) File.Move(step.Move.Target, step.Move.Source);
                    if (step.Backup is not null) File.Move(step.Backup, step.Move.Target);
                }
                catch (Exception rollbackError) when (rollbackError is IOException or UnauthorizedAccessException)
                {
                    failures.Add($"{step.Move.Source} / {step.Move.Target} / Sicherung {step.Backup}: {rollbackError.Message}");
                }
            }
            foreach (var directory in createdDirectories)
            {
                try { Directory.Delete(directory, recursive: false); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            if (failures.Count > 0)
                throw new IOException($"Paket-Rücknahme unvollständig: {string.Join("; ", failures)}. Ursprünglicher Fehler: {error.Message}", error);
            throw new IOException($"Paket vollständig zurückgenommen: {error.Message}", error);
        }

        foreach (var step in completed.Where(step => step.Backup is not null))
        {
            // Erfolg bleibt Erfolg, selbst wenn ein Scanner die alte Sicherung gerade sperrt.
            try { File.Delete(step.Backup!); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
