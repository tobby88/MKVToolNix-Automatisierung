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
    /// <param name="cancellationToken">Abbruchsignal zwischen Archiveinträgen.</param>
    void ExtractArchive(
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
            }
        };

    /// <inheritdoc />
    public void ExtractArchive(
        string archivePath,
        string destinationDirectory,
        IProgress<ManagedToolExtractionProgress>? progress = null,
        ManagedToolKind? toolKind = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        Directory.CreateDirectory(destinationDirectory);

        using var archive = ArchiveFactory.OpenArchive(archivePath, ReaderOptions.ForFilePath);
        var allEntries = archive.Entries
            .Where(entry => !entry.IsDirectory)
            .ToList();
        var entries = SelectEntriesForTool(toolKind, allEntries);
        var totalEntryCount = entries.Count;
        var totalByteCount = entries.Sum(entry => GetEntrySize(entry));
        progress?.Report(new ManagedToolExtractionProgress(0, totalEntryCount, ExtractedByteCount: 0, TotalByteCount: totalByteCount));

        var extractedEntryCount = 0;
        long extractedByteCount = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetPath = GetArchiveEntryDestinationPath(destinationDirectory, entry.Key);
            ExtractEntry(entry, targetPath, cancellationToken);

            extractedEntryCount++;
            extractedByteCount += GetEntrySize(entry);
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

        return targetPath;
    }

    private static void ExtractEntry(IArchiveEntry entry, string targetPath, CancellationToken cancellationToken)
    {
        var targetDirectory = Path.GetDirectoryName(targetPath)
                              ?? throw new InvalidOperationException($"Der Zielpfad für '{entry.Key}' ist ungültig.");
        Directory.CreateDirectory(targetDirectory);

        using var input = entry.OpenEntryStream();
        using var output = File.Create(targetPath);
        var buffer = new byte[81920];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytesRead = input.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0)
            {
                break;
            }

            output.Write(buffer, 0, bytesRead);
        }
    }

    private static long GetEntrySize(IArchiveEntry entry)
    {
        return Math.Max(0, entry.Size);
    }
}
