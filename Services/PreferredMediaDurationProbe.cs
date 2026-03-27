namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Probiert zuerst ffprobe und fällt erst dann auf die Windows-Metadaten zurück.
/// </summary>
public sealed class PreferredMediaDurationProbe : IMediaDurationProbe
{
    private readonly IMediaDurationProbe _preferredProbe;
    private readonly IMediaDurationProbe _fallbackProbe;

    public PreferredMediaDurationProbe(IMediaDurationProbe preferredProbe, IMediaDurationProbe fallbackProbe)
    {
        _preferredProbe = preferredProbe;
        _fallbackProbe = fallbackProbe;
    }

    public TimeSpan? TryReadDuration(string filePath)
    {
        return _preferredProbe.TryReadDuration(filePath) ?? _fallbackProbe.TryReadDuration(filePath);
    }
}
