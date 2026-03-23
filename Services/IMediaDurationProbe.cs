namespace MkvToolnixAutomatisierung.Services;

public interface IMediaDurationProbe
{
    TimeSpan? TryReadDuration(string filePath);
}
