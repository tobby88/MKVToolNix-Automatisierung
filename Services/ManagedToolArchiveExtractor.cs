using SharpCompress.Archives;
using SharpCompress.Readers;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Entpackt heruntergeladene ZIP- oder 7z-Archive ohne externe Hilfsprogramme.
/// </summary>
internal interface IManagedToolArchiveExtractor
{
    /// <summary>
    /// Entpackt ein Archiv vollständig oder werkzeugspezifisch reduziert in das angegebene Zielverzeichnis.
    /// </summary>
    /// <param name="archivePath">Pfad zum zuvor heruntergeladenen Archiv.</param>
    /// <param name="destinationDirectory">Leeres oder neu anzulegendes Zielverzeichnis.</param>
    /// <param name="progress">Optionaler Fortschrittskanal für die Dateieinträge des Archivs.</param>
    /// <param name="toolKind">Optionales Werkzeug, damit unnötige Archivteile übersprungen werden können.</param>
    /// <param name="cancellationToken">Abbruchsignal für Archiveinträge und laufende Datei-I/O-Operationen.</param>
    Task ExtractArchiveAsync(
        string archivePath,
        string destinationDirectory,
        IProgress<ManagedToolExtractionProgress>? progress = null,
        ManagedToolKind? toolKind = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementiert die Archiv-Extraktion rein in .NET mit SharpCompress.
/// </summary>
internal sealed class ManagedToolArchiveExtractor : IManagedToolArchiveExtractor
{
    private const long FreeSpaceReserve = 64L * 1024 * 1024;
    private readonly long _maximumExtractedBytes;
    private readonly Func<string, long?> _availableSpace;

    /// <summary>Begrenzt den benötigten Tool-Payload auf 8 GiB und lässt 64 MiB frei.</summary>
    public ManagedToolArchiveExtractor() : this(8L * 1024 * 1024 * 1024, ReadAvailableSpace) { }

    internal ManagedToolArchiveExtractor(long maximumExtractedBytes, Func<string, long?> availableSpace)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumExtractedBytes);
        _maximumExtractedBytes = maximumExtractedBytes;
        _availableSpace = availableSpace;
    }
    private static readonly IReadOnlyDictionary<ManagedToolKind, HashSet<string>> RequiredToolExecutables =
        new Dictionary<ManagedToolKind, HashSet<string>>
        {
            [ManagedToolKind.MkvToolNix] = new(StringComparer.OrdinalIgnoreCase)
            {
                "mkvmerge.exe",
                "mkvpropedit.exe",
                "mkvextract.exe"
            },
            [ManagedToolKind.Ffprobe] = new(StringComparer.OrdinalIgnoreCase)
            {
                "ffprobe.exe"
            },
            [ManagedToolKind.MediathekView] = new(StringComparer.OrdinalIgnoreCase)
            {
                "MediathekView_Portable.exe",
                "MediathekView.exe"
            }
        };

    /// <inheritdoc />
    public async Task ExtractArchiveAsync(
        string archivePath,
        string destinationDirectory,
        IProgress<ManagedToolExtractionProgress>? progress = null,
        ManagedToolKind? toolKind = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        cancellationToken.ThrowIfCancellationRequested();
        EnsureNoReparsePoints(destinationDirectory);
        if (Directory.Exists(destinationDirectory) && Directory.EnumerateFileSystemEntries(destinationDirectory).Any())
        {
            throw new InvalidOperationException("Das Zielverzeichnis der Werkzeugextraktion muss leer sein.");
        }

        Directory.CreateDirectory(destinationDirectory);

        await using var archive = await ArchiveFactory.OpenAsyncArchive(
            archivePath,
            ReaderOptions.ForFilePath,
            cancellationToken);
        var allEntries = new List<IArchiveEntry>();
        await foreach (var entry in archive.EntriesAsync.WithCancellation(cancellationToken))
        {
            if (!entry.IsDirectory)
            {
                allEntries.Add(entry);
                if (allEntries.Count > 100_000)
                    throw new IOException("Das Werkzeugarchiv enthält mehr als 100.000 Dateien und wird nicht entpackt.");
            }
        }

        var entries = SelectEntriesForTool(toolKind, allEntries);
        // Validate the entire selected payload before writing the first file.
        var targetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetPath = GetArchiveEntryDestinationPath(destinationDirectory, entry.Key);
            if (!targetPaths.Add(targetPath))
            {
                throw new InvalidOperationException($"Das Werkzeugarchiv enthaelt einen doppelten Zielpfad: {entry.Key}");
            }
        }

        var totalEntryCount = entries.Count;
        long totalByteCount = 0;
        foreach (var entry in entries)
        {
            var size = GetEntrySize(entry);
            if (size > _maximumExtractedBytes - totalByteCount)
                throw new IOException("Die entpackte Werkzeuggröße überschreitet das Sicherheitslimit.");
            totalByteCount += size;
        }
        EnsureAvailableSpace(destinationDirectory, totalByteCount);
        progress?.Report(new ManagedToolExtractionProgress(0, totalEntryCount, ExtractedByteCount: 0, TotalByteCount: totalByteCount));

        var extractedEntryCount = 0;
        long extractedByteCount = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetPath = GetArchiveEntryDestinationPath(destinationDirectory, entry.Key);
            EnsureAvailableSpace(destinationDirectory, GetEntrySize(entry));
            var writtenBytes = await ExtractEntryAsync(
                entry,
                targetPath,
                extractedEntryCount,
                totalEntryCount,
                extractedByteCount,
                totalByteCount,
                progress,
                cancellationToken);

            extractedEntryCount++;
            extractedByteCount += writtenBytes;
            progress?.Report(new ManagedToolExtractionProgress(
                extractedEntryCount,
                totalEntryCount,
                entry.Key,
                extractedByteCount,
                totalByteCount));
        }
    }

    private static IReadOnlyList<IArchiveEntry> SelectEntriesForTool(
        ManagedToolKind? toolKind,
        IReadOnlyList<IArchiveEntry> entries)
    {
        if (toolKind is null || !RequiredToolExecutables.TryGetValue(toolKind.Value, out var requiredExecutables))
        {
            return entries;
        }

        if (toolKind == ManagedToolKind.MediathekView)
        {
            return entries;
        }

        var requiredDirectories = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .Where(entry => requiredExecutables.Contains(Path.GetFileName(entry.Key)!))
            .Select(entry => GetArchiveDirectoryKey(entry.Key!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requiredDirectories.Count == 0)
        {
            return entries;
        }

        var filteredEntries = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .Where(entry => requiredDirectories.Contains(GetArchiveDirectoryKey(entry.Key!)))
            .ToList();

        return filteredEntries.Count > 0
            ? filteredEntries
            : entries;
    }

    private static string GetArchiveDirectoryKey(string entryKey)
    {
        var normalized = entryKey
            .Replace('\\', '/')
            .Trim('/');
        var separatorIndex = normalized.LastIndexOf('/');

        return separatorIndex >= 0
            ? normalized[..separatorIndex]
            : string.Empty;
    }

    private static string GetArchiveEntryDestinationPath(string destinationDirectory, string? entryKey)
    {
        if (string.IsNullOrWhiteSpace(entryKey))
        {
            throw new InvalidOperationException("Das Werkzeugarchiv enthält einen Eintrag ohne sicheren relativen Pfad.");
        }

        var normalizedEntryKey = entryKey.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalizedEntryKey))
        {
            throw new InvalidOperationException($"Das Werkzeugarchiv enthält einen absoluten Pfad: {entryKey}");
        }

        var destinationRoot = Path.GetFullPath(destinationDirectory);
        var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, normalizedEntryKey));
        if (!PathComparisonHelper.IsPathWithinRoot(targetPath, destinationRoot)
            || PathComparisonHelper.AreSamePath(targetPath, destinationRoot))
        {
            throw new InvalidOperationException($"Das Werkzeugarchiv enthält einen unsicheren relativen Pfad: {entryKey}");
        }

        var segments = normalizedEntryKey.Split(Path.DirectorySeparatorChar);
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment)
                || segment is "." or ".."
                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || segment.EndsWith(".", StringComparison.Ordinal)
                || segment.EndsWith(" ", StringComparison.Ordinal)
                || IsReservedDeviceName(segment)))
        {
            throw new InvalidOperationException($"Das Werkzeugarchiv enthält einen unsicheren relativen Pfad: {entryKey}");
        }

        return targetPath;
    }

    private static bool IsReservedDeviceName(string segment)
    {
        var name = segment.Split('.')[0];
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase)
               || name.Equals("PRN", StringComparison.OrdinalIgnoreCase)
               || name.Equals("AUX", StringComparison.OrdinalIgnoreCase)
               || name.Equals("NUL", StringComparison.OrdinalIgnoreCase)
               || (name.Length == 4 && name[3] is >= '1' and <= '9'
                   && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                       || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)));
    }

    private static void EnsureNoReparsePoints(string path)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(path)); directory is not null; directory = directory.Parent)
        {
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Das Extraktionsziel darf keine Verzeichnisverknuepfungen enthalten.");
            }
        }
    }

    private async Task<long> ExtractEntryAsync(
        IArchiveEntry entry,
        string targetPath,
        int completedEntryCount,
        int totalEntryCount,
        long completedByteCount,
        long totalByteCount,
        IProgress<ManagedToolExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var targetDirectory = Path.GetDirectoryName(targetPath)
                              ?? throw new InvalidOperationException($"Der Zielpfad für '{entry.Key}' ist ungültig.");
        EnsureNoReparsePoints(targetDirectory);
        Directory.CreateDirectory(targetDirectory);

        await using var input = await entry.OpenEntryStreamAsync(cancellationToken);
        await using var output = new FileStream(
            targetPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        var buffer = new byte[81920];
        long entryByteCount = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytesRead = await input.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            // Tatsächliche Bytes begrenzen, nicht nur die vom Archiv behauptete Größe.
            if (bytesRead > _maximumExtractedBytes - completedByteCount - entryByteCount)
                throw new IOException("Die tatsächliche entpackte Werkzeuggröße überschreitet das Sicherheitslimit.");
            await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            entryByteCount += bytesRead;
            progress?.Report(new ManagedToolExtractionProgress(
                completedEntryCount,
                totalEntryCount,
                entry.Key,
                CalculateInProgressByteCount(completedByteCount, entryByteCount, totalByteCount),
                totalByteCount));
        }
        return entryByteCount;
    }

    private void EnsureAvailableSpace(string destination, long requiredBytes)
    {
        if (_availableSpace(destination) is { } available && requiredBytes > Math.Max(0, available - FreeSpaceReserve))
            throw new IOException($"Nicht genügend freier Speicher zum Entpacken: {requiredBytes / (1024 * 1024)} MiB plus 64 MiB Reserve benötigt.");
    }

    private static long? ReadAvailableSpace(string destination)
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(destination))!).AvailableFreeSpace; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Manche UNC-Server liefern keine Kapazität. Größenlimit und echte I/O-Fehler
            // bleiben wirksam; ein nicht messbarer Wert ist nicht gleich null freier Platz.
            return null;
        }
    }

    private static long GetEntrySize(IArchiveEntry entry)
    {
        return Math.Max(0, entry.Size);
    }

    private static long CalculateInProgressByteCount(
        long completedByteCount,
        long currentEntryByteCount,
        long totalByteCount)
    {
        var extractedByteCount = completedByteCount + currentEntryByteCount;
        return totalByteCount > 0
            ? Math.Min(extractedByteCount, totalByteCount)
            : extractedByteCount;
    }
}
