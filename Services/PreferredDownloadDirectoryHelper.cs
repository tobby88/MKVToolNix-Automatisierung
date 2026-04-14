namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Liefert den im Projekt bevorzugten Startordner für frische MediathekView-Downloads.
/// </summary>
internal static class PreferredDownloadDirectoryHelper
{
    private static readonly string[] PreferredDownloadsSubPath = ["MediathekView-latest-win", "Downloads"];

    /// <summary>
    /// Liefert den bevorzugten Downloadordner unterhalb des Benutzerprofils oder fällt auf Dokumente zurück.
    /// </summary>
    public static string GetPreferredMediathekDownloadsDirectory()
    {
        var downloadsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        var preferredDirectory = PreferredDownloadsSubPath.Aggregate(downloadsDirectory, Path.Combine);

        return Directory.Exists(preferredDirectory)
            ? preferredDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }
}
