using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>Ein ausdrücklich gefundener Arbeitsrest, keine pauschale Löschfreigabe für einen Ordner.</summary>
internal sealed record RecoveryEntry(string Path, string Kind, string Hint, bool IsDirectory,
    FileStateSnapshot? Snapshot, DateTime LastWriteUtc, string? RestoreTarget = null, string? ExpectedHash = null)
{
    public bool CanRestore => RestoreTarget is not null && ExpectedHash is not null;
}

/// <summary>
/// Sichtet nur bekannte Artefaktmuster, überspringt Links und aktive Werkzeuge. Löschen bleibt
/// eine ausdrückliche Benutzeraktion im Papierkorb, niemals automatischer Start-Cleanup.
/// </summary>
internal sealed partial class RecoveryMaintenanceService(IEnumerable<string> activePaths, string? mediathekRoot = null)
{
    private readonly string[] _activePaths = activePaths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(System.IO.Path.GetFullPath).ToArray();
    private readonly HashSet<RecoveryEntry> _scanned = [];

    public IReadOnlyList<RecoveryEntry> Scan(string root, CancellationToken cancellationToken = default)
    {
        root = System.IO.Path.GetFullPath(root);
        FileMutationSafety.EnsureOrdinaryPath(root);
        _scanned.Clear();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                var name = System.IO.Path.GetFileName(path);
                if (isDirectory)
                {
                    var oldMediathek = mediathekRoot is not null && PathComparisonHelper.AreSamePath(directory, mediathekRoot)
                        && !name.StartsWith('.') && _activePaths.Any(active => IsWithin(active, mediathekRoot));
                    if (WorkingDirectoryPattern().IsMatch(name) || oldMediathek)
                    {
                        if (!IsProtected(path))
                            _scanned.Add(new(path, oldMediathek ? "Alte MediathekView-Version" : "Arbeits-/Sicherungsordner",
                                "Kann Benutzerdaten oder ein fertiges Zwischenergebnis enthalten. Vor Entfernen ansehen.", true, null, Directory.GetLastWriteTimeUtc(path)));
                    }
                    else pending.Push(path);
                    continue;
                }
                var backup = BackupPattern().Match(name);
                if (backup.Success)
                {
                    var target = System.IO.Path.Combine(directory, backup.Groups[1].Value);
                    _scanned.Add(new(path, "Metadatensicherung", "Originalzustand vor NFO-/JSON-Schreibvorgang. Wiederherstellen ersetzt nur diese Metadatendatei.",
                        false, FileStateSnapshot.TryCreate(path), File.GetLastWriteTimeUtc(path), target, backup.Groups[2].Value));
                }
                else if (WorkingFilePattern().IsMatch(name))
                    _scanned.Add(new(path, name.StartsWith(".archive-change-", StringComparison.Ordinal) ? "Archiv-Wiederaufnahmebeleg" : "Arbeits-/Sicherungsdatei",
                        "Vor Entfernen ansehen. Bei Archivbelegen betroffene MKV neu scannen; keine alten Aufträge blind wiederholen.",
                        false, FileStateSnapshot.TryCreate(path), File.GetLastWriteTimeUtc(path)));
            }
        }
        return _scanned.OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Prüft unveränderte Herkunft und schützt erneut die aktiven Dateien vor jeder Aktion.</summary>
    private void Validate(RecoveryEntry entry)
    {
        if (!_scanned.Contains(entry) || IsProtected(entry.Path)) throw new IOException("Der Arbeitsrest ist nicht freigegeben oder wird noch verwendet. Bitte neu prüfen.");
        FileMutationSafety.EnsureOrdinaryPath(entry.Path);
        if (entry.IsDirectory)
        {
            if (!Directory.Exists(entry.Path) || Directory.GetLastWriteTimeUtc(entry.Path) != entry.LastWriteUtc)
                throw new IOException("Der Ordner wurde inzwischen verändert. Bitte neu prüfen.");
        }
        else
        {
            FileMutationSafety.EnsureOrdinaryFile(entry.Path);
            if (FileStateSnapshot.TryCreate(entry.Path) != entry.Snapshot) throw new IOException("Die Sicherung wurde inzwischen verändert. Bitte neu prüfen.");
        }
    }

    /// <summary>
    /// Stellt ausschließlich eine vollständig gehashte Metadatensicherung wieder her. Der jetzige
    /// Zustand wird zusätzlich behalten; der Sicherungsbeleg selbst wird nicht automatisch gelöscht.
    /// </summary>
    public string RestoreMetadata(RecoveryEntry entry)
    {
        Validate(entry);
        if (!entry.CanRestore || entry.Snapshot?.Length > 64 * 1024 * 1024)
            throw new IOException("Diese Sicherung muss manuell geprüft werden.");
        var content = File.ReadAllBytes(entry.Path);
        if (Convert.ToHexString(SHA256.HashData(content)) != entry.ExpectedHash)
            throw new IOException("Die Sicherung ist unvollständig oder beschädigt. Es wurde nichts wiederhergestellt.");
        var target = entry.RestoreTarget!;
        FileMutationSafety.EnsureOrdinaryFile(target);
        if (!File.Exists(target))
        {
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            output.Write(content);
            output.Flush(true);
            return "Die fehlende Metadatendatei wurde wiederhergestellt.";
        }
        var update = new SmallFileUpdate(target);
        var undo = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(target)!, $".{System.IO.Path.GetFileName(target)}.before-recovery-{Guid.NewGuid():N}.tmp");
        using (var source = update.OpenRead())
        using (var output = new FileStream(undo, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            source.CopyTo(output);
            output.Flush(true);
        }
        update.Commit(content);
        return $"Metadaten wiederhergestellt. Vorheriger Zustand gesichert: {undo}";
    }

    public void Recycle(RecoveryEntry entry)
    {
        Validate(entry);
        if (entry.IsDirectory)
        {
            // Kein rekursiver Move darf unbemerkt einen Link in einen fremden Datenbaum folgen.
            foreach (var path in Directory.EnumerateFileSystemEntries(entry.Path, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
                FileMutationSafety.EnsureOrdinaryPath(path);
            FileSystem.DeleteDirectory(entry.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        }
        else FileSystem.DeleteFile(entry.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
    }

    private bool IsProtected(string path) => _activePaths.Any(active => IsWithin(active, path) || IsWithin(path, active));
    private static bool IsWithin(string path, string root) => PathComparisonHelper.AreSamePath(path, root)
        || PathComparisonHelper.TryGetRelativePathWithinRoot(path, root) is not null;

    [GeneratedRegex(@"^\.(?:mux|staging)-[a-fA-F0-9]{32}$|^\.replaced-.+", RegexOptions.CultureInvariant)]
    private static partial Regex WorkingDirectoryPattern();
    [GeneratedRegex(@"^\.(.+\.(?:nfo|json))\.edit-[a-fA-F0-9]{32}-([A-F0-9]{64})\.tmp$", RegexOptions.CultureInvariant)]
    private static partial Regex BackupPattern();
    [GeneratedRegex(@"^\.archive-change-[a-fA-F0-9]{32}\.json$|^\..+\.(?:edit|replace|before-recovery)-.+\.tmp(?:\.writing)?$|^\.download-[a-fA-F0-9]{32}-.+", RegexOptions.CultureInvariant)]
    private static partial Regex WorkingFilePattern();
}
