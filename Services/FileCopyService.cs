using MkvToolnixAutomatisierung.Modules.SeriesEpisodeMux;

namespace MkvToolnixAutomatisierung.Services;

public class FileCopyService
{
    private const int BufferSize = 1024 * 1024;

    public virtual bool NeedsCopy(FileCopyPlan copyPlan)
    {
        return !copyPlan.IsReusable;
    }

    public virtual async Task CopyAsync(
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
