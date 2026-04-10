namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Liefert einen benutzbaren Pfad zur optionalen <c>ffprobe.exe</c>.
/// </summary>
public interface IFfprobeLocator
{
    /// <summary>
    /// Sucht eine passende <c>ffprobe.exe</c> oder liefert <see langword="null"/>, wenn aktuell keine gefunden wurde.
    /// </summary>
    /// <returns>Vollständiger Pfad zur gefundenen Executable oder <see langword="null"/>.</returns>
    string? TryFindFfprobePath();
}

/// <summary>
/// Liefert benutzbare Pfade zu den fuer dieses Projekt erforderlichen MKVToolNix-Werkzeugen.
/// </summary>
public interface IMkvToolNixLocator
{
    /// <summary>
    /// Ermittelt den Pfad zur verwendbaren <c>mkvmerge.exe</c>.
    /// </summary>
    /// <returns>Vollständiger Pfad zur auszuführenden Executable.</returns>
    string FindMkvMergePath();

    /// <summary>
    /// Ermittelt den Pfad zur verwendbaren <c>mkvpropedit.exe</c>.
    /// </summary>
    /// <returns>Vollständiger Pfad zur auszuführenden Executable.</returns>
    string FindMkvPropEditPath();
}
