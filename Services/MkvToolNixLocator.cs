namespace MkvToolnixAutomatisierung.Services;

public sealed class MkvToolNixLocator
{
    private const string DownloadsFolderName = "Downloads";
    private const string DirectoryPrefix = "mkvtoolnix-64-bit-";
    private const string RelativeMkvMergePath = "mkvtoolnix\\mkvmerge.exe";

    public string FindMkvMergePath()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloadsDirectory = Path.Combine(userProfile, DownloadsFolderName);

        if (!Directory.Exists(downloadsDirectory))
        {
            throw new DirectoryNotFoundException($"Download-Ordner nicht gefunden: {downloadsDirectory}");
        }

        var candidate = Directory
            .GetDirectories(downloadsDirectory, $"{DirectoryPrefix}*")
            .Select(path => new DirectoryInfo(path))
            .OrderByDescending(directory => directory.LastWriteTimeUtc)
            .Select(directory => Path.Combine(directory.FullName, RelativeMkvMergePath))
            .FirstOrDefault(File.Exists);

        if (candidate is null)
        {
            throw new FileNotFoundException("Es wurde keine mkvmerge.exe in einem mkvtoolnix-Download-Ordner gefunden.");
        }

        return candidate;
    }
}
