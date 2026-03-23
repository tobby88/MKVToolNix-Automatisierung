namespace MkvToolnixAutomatisierung.Services;

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
