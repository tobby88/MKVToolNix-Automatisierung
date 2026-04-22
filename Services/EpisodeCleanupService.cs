using System.Runtime.InteropServices;

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
    private readonly Action<string, string> _moveFile;
    private readonly Action<string> _recycleFile;

    /// <summary>
    /// Initialisiert die Standardimplementierung mit echten Dateisystemoperationen.
    /// </summary>
    public EpisodeCleanupService()
        : this(
            static (sourceFilePath, destinationPath) => File.Move(sourceFilePath, destinationPath),
            RecycleFileWithoutUi)
    {
    }

    /// <summary>
    /// Testbarer Einstieg mit austauschbaren Dateioperationen.
    /// </summary>
    /// <param name="moveFile">Konkrete Verschiebeoperation.</param>
    /// <param name="recycleFile">Konkrete Papierkorboperation.</param>
    internal EpisodeCleanupService(
        Action<string, string> moveFile,
        Action<string> recycleFile)
    {
        _moveFile = moveFile;
        _recycleFile = recycleFile;
    }

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
            var pendingFiles = new List<string>();
            var files = GetDistinctExistingFilePaths(sourceFilePaths);

            for (var index = 0; index < files.Count; index++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    pendingFiles.AddRange(files.Skip(index));
                    break;
                }

                var sourceFilePath = files[index];
                onProgress?.Invoke(index + 1, files.Count, sourceFilePath);

                try
                {
                    var destinationPath = BuildUniqueTargetPath(targetDirectory, Path.GetFileName(sourceFilePath));
                    _moveFile(sourceFilePath, destinationPath);
                    movedFiles.Add(destinationPath);
                }
                catch
                {
                    failedFiles.Add(sourceFilePath);
                }
            }

            return new FileMoveResult(
                movedFiles,
                failedFiles,
                pendingFiles,
                WasCanceled: pendingFiles.Count > 0);
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
            var pendingFiles = new List<string>();
            var files = GetDistinctExistingFilePaths(filePaths);

            for (var index = 0; index < files.Count; index++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    pendingFiles.AddRange(files.Skip(index));
                    break;
                }

                var filePath = files[index];
                onProgress?.Invoke(index + 1, files.Count, filePath);

                try
                {
                    _recycleFile(filePath);
                    recycledFiles.Add(filePath);
                }
                catch
                {
                    failedFiles.Add(filePath);
                }
            }

            return new FileRecycleResult(
                recycledFiles,
                failedFiles,
                pendingFiles,
                WasCanceled: pendingFiles.Count > 0);
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

    /// <summary>
    /// Verdichtet eine Quellliste auf vorhandene, eindeutige Pfade in stabiler Reihenfolge.
    /// </summary>
    private static List<string> GetDistinctExistingFilePaths(IReadOnlyList<string> filePaths)
    {
        return filePaths
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Verschiebt eine Datei ohne modale Shell-Dialoge in den Papierkorb.
    /// </summary>
    /// <remarks>
    /// <see cref="Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(string, Microsoft.VisualBasic.FileIO.UIOption, Microsoft.VisualBasic.FileIO.RecycleOption)"/>
    /// würde bei Fehlern modale Dialoge anzeigen und Batch-/Workerthreads blockieren. Für das
    /// projektinterne Cleanup ist stattdessen ein stiller Shell-Aufruf robuster.
    /// </remarks>
    /// <param name="filePath">Zu recyclelnde Datei.</param>
    private static void RecycleFileWithoutUi(string filePath)
    {
        var operation = new ShellFileOperation
        {
            OperationType = ShellFileOperationType.Delete,
            From = filePath + '\0' + '\0',
            Flags = ShellFileOperationFlags.AllowUndo
                | ShellFileOperationFlags.NoConfirmation
                | ShellFileOperationFlags.NoErrorUi
                | ShellFileOperationFlags.Silent
        };

        var result = SHFileOperation(ref operation);
        if (result != 0 || operation.AnyOperationsAborted)
        {
            throw new IOException($"Datei konnte nicht in den Papierkorb verschoben werden: {filePath}");
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShellFileOperation fileOperation);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileOperation
    {
        public IntPtr WindowHandle;
        public ShellFileOperationType OperationType;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string From;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? To;

        public ShellFileOperationFlags Flags;

        [MarshalAs(UnmanagedType.Bool)]
        public bool AnyOperationsAborted;

        public IntPtr NameMappings;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? ProgressTitle;
    }

    private enum ShellFileOperationType : uint
    {
        Delete = 3
    }

    [Flags]
    private enum ShellFileOperationFlags : ushort
    {
        Silent = 0x0004,
        NoConfirmation = 0x0010,
        AllowUndo = 0x0040,
        NoErrorUi = 0x0400
    }
}

/// <summary>
/// Rückgabe eines Verschiebevorgangs inklusive Fehlkandidaten und ggf. unbearbeiteter Restdateien.
/// </summary>
internal sealed record FileMoveResult(
    IReadOnlyList<string> MovedFiles,
    IReadOnlyList<string> FailedFiles,
    IReadOnlyList<string> PendingFiles,
    bool WasCanceled)
{
    public FileMoveResult(
        IReadOnlyList<string> movedFiles,
        IReadOnlyList<string> failedFiles)
        : this(movedFiles, failedFiles, [], WasCanceled: false)
    {
    }
}

/// <summary>
/// Rückgabe eines Papierkorb-Laufs inklusive Fehlkandidaten und ggf. unbearbeiteter Restdateien.
/// </summary>
internal sealed record FileRecycleResult(
    IReadOnlyList<string> RecycledFiles,
    IReadOnlyList<string> FailedFiles,
    IReadOnlyList<string> PendingFiles,
    bool WasCanceled)
{
    public FileRecycleResult(
        IReadOnlyList<string> recycledFiles,
        IReadOnlyList<string> failedFiles)
        : this(recycledFiles, failedFiles, [], WasCanceled: false)
    {
    }
}
