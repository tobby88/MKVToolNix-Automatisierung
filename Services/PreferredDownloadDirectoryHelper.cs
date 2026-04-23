namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Liefert den im Projekt bevorzugten Startordner für frische MediathekView-Downloads.
/// </summary>
internal static class PreferredDownloadDirectoryHelper
{
    private static readonly string[] PreferredDownloadsSubPath = ["MediathekView-latest-win", "Downloads"];

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
    /// Liefert das übliche Downloadverzeichnis unterhalb des aufgelösten Benutzerprofils.
    /// </summary>
    internal static string? TryGetDownloadsDirectory()
    {
        var userProfile = TryGetUserProfileDirectory();
        return string.IsNullOrWhiteSpace(userProfile)
            ? null
            : Path.Combine(userProfile, "Downloads");
    }

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
