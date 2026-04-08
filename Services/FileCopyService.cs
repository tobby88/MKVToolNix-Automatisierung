using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Kopiert vorhandene Archivdateien als lokale Arbeitskopien, bevor sie erweitert oder neu gemuxt werden.
/// </summary>
internal interface IFileCopyService
{
    /// <summary>
    /// Prüft, ob die beschriebene Arbeitskopie tatsächlich neu erstellt oder aktualisiert werden muss.
    /// </summary>
    bool NeedsCopy(FileCopyPlan copyPlan);

    /// <summary>
    /// Erstellt oder aktualisiert eine lokale Arbeitskopie einer vorhandenen Archivdatei.
    /// </summary>
    Task CopyAsync(
        FileCopyPlan copyPlan,
        Action<long, long>? onProgress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Standardimplementierung für Arbeitskopien vorhandener Archivdateien.
/// </summary>
internal sealed class FileCopyService : IFileCopyService
{
    private const int BufferSize = 1024 * 1024;

    /// <summary>
    /// Prüft, ob die beschriebene Arbeitskopie tatsächlich neu erstellt oder aktualisiert werden muss.
    /// </summary>
    /// <param name="copyPlan">Beschreibung der gewünschten Arbeitskopie.</param>
    /// <returns><see langword="true"/>, wenn eine Kopieroperation nötig ist.</returns>
    public bool NeedsCopy(FileCopyPlan copyPlan)
    {
        return !copyPlan.IsReusable;
    }

    /// <summary>
    /// Erstellt oder aktualisiert eine lokale Arbeitskopie einer vorhandenen Archivdatei.
    /// </summary>
    /// <param name="copyPlan">Beschreibung von Quell- und Zielpfad der Arbeitskopie.</param>
    /// <param name="onProgress">Optionaler Callback für bereits kopierte und gesamte Bytes.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal.</param>
    public async Task CopyAsync(
        FileCopyPlan copyPlan,
        Action<long, long>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var destinationDirectory = Path.GetDirectoryName(copyPlan.DestinationFilePath);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new InvalidOperationException("Der Zielordner für die Arbeitskopie konnte nicht bestimmt werden.");
        }

        Directory.CreateDirectory(destinationDirectory);

        await using var sourceStream = new FileStream(
            copyPlan.SourceFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            useAsync: true);

        await using var destinationStream = new FileStream(
            copyPlan.DestinationFilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            useAsync: true);

        var buffer = new byte[BufferSize];
        long copiedBytes = 0;

        while (true)
        {
            var bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            await destinationStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            copiedBytes += bytesRead;
            onProgress?.Invoke(copiedBytes, copyPlan.FileSizeBytes);
        }
    }
}
