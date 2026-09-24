namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Probiert zuerst ffprobe und fällt erst dann auf die Windows-Metadaten zurück.
/// </summary>
public sealed class PreferredMediaDurationProbe : IMediaDurationProbe
{
    private readonly IMediaDurationProbe _preferredProbe;
    private readonly IMediaDurationProbe _fallbackProbe;

    /// <summary>
    /// Initialisiert die Laufzeit-Probe mit bevorzugtem und alternativem Probe-Dienst.
    /// </summary>
    /// <param name="preferredProbe">Primärer Probe-Dienst, typischerweise ffprobe.</param>
    /// <param name="fallbackProbe">Fallback-Dienst, typischerweise Windows-Metadaten.</param>
    public PreferredMediaDurationProbe(IMediaDurationProbe preferredProbe, IMediaDurationProbe fallbackProbe)
    {
        _preferredProbe = preferredProbe;
        _fallbackProbe = fallbackProbe;
    }

    /// <inheritdoc />
    public TimeSpan? TryReadDuration(string filePath)
        => TryReadDuration(filePath, CancellationToken.None);

    /// <inheritdoc />
    public TimeSpan? TryReadDuration(string filePath, CancellationToken cancellationToken)
    {
        return _preferredProbe.TryReadDuration(filePath, cancellationToken) ?? _fallbackProbe.TryReadDuration(filePath, cancellationToken);
    }
}
