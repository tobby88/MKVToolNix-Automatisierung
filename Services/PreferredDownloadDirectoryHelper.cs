using System.Runtime.InteropServices;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Liefert den im Projekt bevorzugten Startordner für frische MediathekView-Downloads.
/// </summary>
internal static class PreferredDownloadDirectoryHelper
{
    private static readonly string[] PreferredDownloadsSubPath = ["MediathekView", "Downloads"];

    /// <summary>
    /// Liefert das aktuell bevorzugte Benutzerprofil für downloadbezogene Fallback-Suchen.
    /// </summary>
    /// <remarks>
    /// In normalen Desktop-Sessions liefert weiterhin das Windows-Benutzerprofil das Ergebnis.
    /// Test- und Sandbox-Umgebungen können über <c>USERPROFILE</c> oder <c>HOME</c> jedoch
    /// gezielt ein alternatives Profil vorgeben, ohne dass produktive Codepfade abweichen.
    /// </remarks>
    internal static string? TryGetUserProfileDirectory()
    {
        var environmentProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrWhiteSpace(environmentProfile))
        {
            return environmentProfile;
        }

        var homeDirectory = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(homeDirectory))
        {
            return homeDirectory;
        }

        var shellProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(shellProfile)
            ? null
            : shellProfile;
    }

    /// <summary>
    /// Berücksichtigt die Windows-Known-Folder-Umleitung und explizite Profil-Overrides.
    /// </summary>
    internal static string? TryGetDownloadsDirectory()
    {
        var userProfile = TryGetUserProfileDirectory();
        return ResolveDownloadsDirectory(userProfile,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ReadKnownDownloadsFolder);
    }

    internal static string? ResolveDownloadsDirectory(string? profile, string shellProfile, Func<string?> readKnownFolder)
    {
        // Ein bewusst umgebogenes Test-/Portable-Profil darf nicht auf echte Benutzerdaten zeigen.
        if (PathComparisonHelper.AreSamePath(profile, shellProfile))
        {
            var knownFolder = readKnownFolder();
            if (!string.IsNullOrWhiteSpace(knownFolder) && Path.IsPathFullyQualified(knownFolder))
            {
                return knownFolder;
            }
        }
        return string.IsNullOrWhiteSpace(profile) ? null : Path.Combine(profile, "Downloads");
    }

    private static string? ReadKnownDownloadsFolder()
    {
        var folderId = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        var pointer = IntPtr.Zero;
        try
        {
            // KF_FLAG_DONT_VERIFY avoids network I/O merely to resolve the configured location.
            return SHGetKnownFolderPath(ref folderId, 0x4000, IntPtr.Zero, out pointer) == 0
                ? Marshal.PtrToStringUni(pointer) : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);

    /// <summary>
    /// Liefert den bevorzugten Downloadordner unterhalb des Benutzerprofils oder fällt auf Dokumente zurück.
    /// </summary>
    public static string GetPreferredMediathekDownloadsDirectory()
    {
        var downloadsDirectory = TryGetDownloadsDirectory();
        if (string.IsNullOrWhiteSpace(downloadsDirectory))
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        var preferredDirectory = PreferredDownloadsSubPath.Aggregate(downloadsDirectory, Path.Combine);

        return Directory.Exists(preferredDirectory)
            ? preferredDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }
}
