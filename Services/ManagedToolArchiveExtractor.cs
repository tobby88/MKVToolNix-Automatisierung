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
    void ExtractArchive(string archivePath, string destinationDirectory);
}

/// <summary>
/// Implementiert die Archiv-Extraktion rein in .NET mit SharpCompress.
/// </summary>
internal sealed class ManagedToolArchiveExtractor : IManagedToolArchiveExtractor
{
    /// <inheritdoc />
    public void ExtractArchive(string archivePath, string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        Directory.CreateDirectory(destinationDirectory);

        using var archive = ArchiveFactory.Open(archivePath);
        foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
        {
            entry.WriteToDirectory(destinationDirectory, new ExtractionOptions
            {
                ExtractFullPath = true,
                Overwrite = true
            });
        }
    }
}

