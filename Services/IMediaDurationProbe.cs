namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Abstraktion für Laufzeitmessung, damit ffprobe und Windows-Fallback austauschbar bleiben.
/// </summary>
public interface IMediaDurationProbe
{
    /// <summary>
    /// Liest möglichst effizient die bekannte Medienlaufzeit einer Datei.
    /// </summary>
    /// <param name="filePath">Zu analysierende Mediendatei.</param>
    /// <returns>Ermittelte Laufzeit oder <see langword="null"/>, wenn keine auslesbar ist.</returns>
    TimeSpan? TryReadDuration(string filePath);
}
