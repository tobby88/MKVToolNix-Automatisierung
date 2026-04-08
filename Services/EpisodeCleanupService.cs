using Microsoft.VisualBasic.FileIO;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Verschiebt oder löscht temporäre/verbrauchte Dateien nach erfolgreicher Verarbeitung.
/// </summary>
internal interface IEpisodeCleanupService
{
    /// <summary>
    /// Verschiebt Dateien gesammelt in ein Zielverzeichnis.
    /// </summary>
    Task<FileMoveResult> MoveFilesToDirectoryAsync(
        IReadOnlyList<string> sourceFilePaths,
        string targetDirectory,
        Action<int, int, string>? onProgress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verschiebt Dateien gesammelt in den Papierkorb.
    /// </summary>
    Task<FileRecycleResult> RecycleFilesAsync(
        IReadOnlyList<string> filePaths,
        Action<int, int, string>? onProgress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Entfernt eine temporäre Datei best effort.
    /// </summary>
    void DeleteTemporaryFile(string? filePath);

    /// <summary>
    /// Entfernt einen Ordner, wenn er leer ist.
    /// </summary>
    void DeleteDirectoryIfEmpty(string? directoryPath);

    /// <summary>
    /// Räumt leere Elternordner innerhalb eines Wurzelpfads auf.
    /// </summary>
    void DeleteEmptyParentDirectories(IEnumerable<string> sourceFilePaths, string? stopAtRoot);
}

/// <summary>
/// Standardimplementierung für Verschieben, Recycling und Dateisystem-Aufräumen.
/// </summary>
internal sealed class EpisodeCleanupService : IEpisodeCleanupService
{
    public async Task<FileMoveResult> MoveFilesToDirectoryAsync(
        IReadOnlyList<string> sourceFilePaths,
        string targetDirectory,
        Action<int, int, string>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(targetDirectory);

            var movedFiles = new List<string>();
            var failedFiles = new List<string>();
            var files = sourceFilePaths
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceFilePath = files[index];
                onProgress?.Invoke(index + 1, files.Count, sourceFilePath);

                try
                {
                    var destinationPath = BuildUniqueTargetPath(targetDirectory, Path.GetFileName(sourceFilePath));
                    File.Move(sourceFilePath, destinationPath);
                    movedFiles.Add(destinationPath);
                }
                catch
                {
                    failedFiles.Add(sourceFilePath);
                }
            }

            return new FileMoveResult(movedFiles, failedFiles);
        }, cancellationToken);
    }

    public async Task<FileRecycleResult> RecycleFilesAsync(
        IReadOnlyList<string> filePaths,
        Action<int, int, string>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var recycledFiles = new List<string>();
            var failedFiles = new List<string>();
            var files = filePaths
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var filePath = files[index];
                onProgress?.Invoke(index + 1, files.Count, filePath);

                try
                {
                    FileSystem.DeleteFile(
                        filePath,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin);
                    recycledFiles.Add(filePath);
                }
                catch
                {
                    failedFiles.Add(filePath);
                }
            }

            return new FileRecycleResult(recycledFiles, failedFiles);
        }, cancellationToken);
    }

    public void DeleteTemporaryFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        try
        {
            File.Delete(filePath);
        }
        catch
        {
        }
    }

    public void DeleteDirectoryIfEmpty(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        try
        {
            if (Directory.EnumerateFileSystemEntries(directoryPath).Any())
            {
                return;
            }

            Directory.Delete(directoryPath, recursive: false);
        }
        catch
        {
        }
    }

    public void DeleteEmptyParentDirectories(IEnumerable<string> sourceFilePaths, string? stopAtRoot)
    {
        if (string.IsNullOrWhiteSpace(stopAtRoot) || !Directory.Exists(stopAtRoot))
        {
            return;
        }

        var parentDirectories = sourceFilePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetDirectoryName(path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Where(path => PathComparisonHelper.IsPathWithinRoot(path, stopAtRoot))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => path.Length)
            .ToList();

        foreach (var parentDirectory in parentDirectories)
        {
            var currentDirectory = parentDirectory;
            while (!string.IsNullOrWhiteSpace(currentDirectory)
                && PathComparisonHelper.IsPathWithinRoot(currentDirectory, stopAtRoot)
                && !PathComparisonHelper.AreSamePath(currentDirectory, stopAtRoot))
            {
                try
                {
                    if (!Directory.Exists(currentDirectory) || Directory.EnumerateFileSystemEntries(currentDirectory).Any())
                    {
                        break;
                    }

                    Directory.Delete(currentDirectory, recursive: false);
                    currentDirectory = Path.GetDirectoryName(currentDirectory);
                }
                catch
                {
                    break;
                }
            }
        }
    }

    private static string BuildUniqueTargetPath(string targetDirectory, string fileName)
    {
        var destinationPath = Path.Combine(targetDirectory, fileName);
        if (!File.Exists(destinationPath))
        {
            return destinationPath;
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var suffix = 2;

        while (true)
        {
            destinationPath = Path.Combine(targetDirectory, $"{fileNameWithoutExtension} ({suffix}){extension}");
            if (!File.Exists(destinationPath))
            {
                return destinationPath;
            }

            suffix++;
        }
    }
}

/// <summary>
/// Rückgabe eines Verschiebevorgangs inklusive Dateien, die nicht bewegt werden konnten.
/// </summary>
internal sealed record FileMoveResult(
    IReadOnlyList<string> MovedFiles,
    IReadOnlyList<string> FailedFiles);

/// <summary>
/// Rückgabe eines Papierkorb-Laufs inklusive Fehlkandidaten.
/// </summary>
internal sealed record FileRecycleResult(
    IReadOnlyList<string> RecycledFiles,
    IReadOnlyList<string> FailedFiles);
