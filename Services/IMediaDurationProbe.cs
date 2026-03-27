namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Abstraktion für Laufzeitmessung, damit ffprobe und Windows-Fallback austauschbar bleiben.
/// </summary>
public interface IMediaDurationProbe
{
    TimeSpan? TryReadDuration(string filePath);
}
