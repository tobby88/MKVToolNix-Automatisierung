using Microsoft.VisualBasic.FileIO;

namespace MkvToolnixAutomatisierung.Services;

public sealed class EpisodeCleanupService
{
    public async Task<FileMoveResult> MoveFilesToDirectoryAsync(
        IReadOnlyList<string> sourceFilePaths,
        string targetDirectory,
        Action<int, int, string>? onProgress = null)
    {
        return await Task.Run(() =>
        {
            Directory.CreateDirectory(targetDirectory);

            var movedFiles = new List<string>();
            var failedFiles = new List<string>();
            var files = sourceFilePaths
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var index = 0; index < files.Count; index++)
            {
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
        });
    }

    public async Task<FileRecycleResult> RecycleFilesAsync(
        IReadOnlyList<string> filePaths,
        Action<int, int, string>? onProgress = null)
    {
        return await Task.Run(() =>
        {
            var recycledFiles = new List<string>();
            var failedFiles = new List<string>();
            var files = filePaths
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var index = 0; index < files.Count; index++)
            {
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
        });
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

public sealed record FileMoveResult(
    IReadOnlyList<string> MovedFiles,
    IReadOnlyList<string> FailedFiles);

public sealed record FileRecycleResult(
    IReadOnlyList<string> RecycledFiles,
    IReadOnlyList<string> FailedFiles);
