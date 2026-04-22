namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Sucht eine benutzbare ffprobe.exe aus Settings oder naheliegenden Standardpfaden.
/// </summary>
public sealed class FfprobeLocator : IFfprobeLocator
{
    private readonly AppToolPathStore _toolPathStore;

    /// <summary>
    /// Initialisiert den ffprobe-Locator mit dem standardmäßigen Toolpfad-Store.
    /// </summary>
    public FfprobeLocator()
        : this(new AppToolPathStore())
    {
    }

    /// <summary>
    /// Initialisiert den ffprobe-Locator mit einem expliziten Toolpfad-Store.
    /// </summary>
    /// <param name="toolPathStore">Persistente Quelle für manuell gesetzte Toolpfade.</param>
    public FfprobeLocator(AppToolPathStore toolPathStore)
    {
        _toolPathStore = toolPathStore;
    }

    /// <inheritdoc />
    public string? TryFindFfprobePath()
    {
        return ManagedToolResolution
            .TryResolveFfprobe(_toolPathStore.Load())
            ?.Path;
    }
}
