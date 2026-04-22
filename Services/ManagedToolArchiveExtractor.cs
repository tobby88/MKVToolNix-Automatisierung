using SharpCompress.Archives;
using SharpCompress.Common;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Entpackt heruntergeladene ZIP- oder 7z-Archive ohne externe Hilfsprogramme.
/// </summary>
internal interface IManagedToolArchiveExtractor
{
    /// <summary>
    /// Entpackt ein Archiv vollständig in das angegebene Zielverzeichnis.
    /// </summary>
    /// <param name="archivePath">Pfad zum zuvor heruntergeladenen Archiv.</param>
    /// <param name="destinationDirectory">Leeres oder neu anzulegendes Zielverzeichnis.</param>
    /// <param name="progress">Optionaler Fortschrittskanal für die Dateieinträge des Archivs.</param>
    void ExtractArchive(string archivePath, string destinationDirectory, IProgress<ManagedToolExtractionProgress>? progress = null);
}

/// <summary>
/// Implementiert die Archiv-Extraktion rein in .NET mit SharpCompress.
/// </summary>
internal sealed class ManagedToolArchiveExtractor : IManagedToolArchiveExtractor
{
    /// <inheritdoc />
    public void ExtractArchive(string archivePath, string destinationDirectory, IProgress<ManagedToolExtractionProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        Directory.CreateDirectory(destinationDirectory);

        using var archive = ArchiveFactory.Open(archivePath);
        var entries = archive.Entries
            .Where(entry => !entry.IsDirectory)
            .ToList();
        var totalEntryCount = entries.Count;
        progress?.Report(new ManagedToolExtractionProgress(0, totalEntryCount));

        var extractedEntryCount = 0;
        foreach (var entry in entries)
        {
            entry.WriteToDirectory(destinationDirectory, new ExtractionOptions
            {
                ExtractFullPath = true,
                Overwrite = true
            });

            extractedEntryCount++;
            progress?.Report(new ManagedToolExtractionProgress(
                extractedEntryCount,
                totalEntryCount,
                entry.Key));
        }
    }
}
