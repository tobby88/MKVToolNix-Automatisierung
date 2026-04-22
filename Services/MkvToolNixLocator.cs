namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Findet die für das Projekt benötigten MKVToolNix-Executables aus den gespeicherten Toolpfaden.
/// </summary>
public sealed class MkvToolNixLocator : IMkvToolNixLocator
{
    private readonly AppToolPathStore _toolPathStore;

    /// <summary>
    /// Initialisiert den MKVToolNix-Locator mit dem standardmäßigen Toolpfad-Store.
    /// </summary>
    public MkvToolNixLocator()
        : this(new AppToolPathStore())
    {
    }

    /// <summary>
    /// Initialisiert den MKVToolNix-Locator mit einem expliziten Toolpfad-Store.
    /// </summary>
    /// <param name="toolPathStore">Persistente Quelle für manuell gesetzte Toolpfade.</param>
    public MkvToolNixLocator(AppToolPathStore toolPathStore)
    {
        _toolPathStore = toolPathStore;
    }

    /// <summary>
    /// Ermittelt den Pfad zur verwendbaren <c>mkvmerge.exe</c> aus Settings oder Download-Ordnern.
    /// </summary>
    /// <returns>Vollständiger Pfad zur auszuführenden mkvmerge-Executable.</returns>
    public string FindMkvMergePath()
    {
        return FindToolPath("mkvmerge.exe");
    }

    /// <summary>
    /// Ermittelt den Pfad zur verwendbaren <c>mkvpropedit.exe</c> aus Settings oder Download-Ordnern.
    /// </summary>
    /// <returns>Vollständiger Pfad zur auszuführenden mkvpropedit-Executable.</returns>
    public string FindMkvPropEditPath()
    {
        return FindToolPath("mkvpropedit.exe");
    }

    private string FindToolPath(string executableName)
    {
        var resolvedPaths = ManagedToolResolution.TryResolveMkvToolNix(_toolPathStore.Load());
        if (resolvedPaths is null)
        {
            throw new FileNotFoundException(
                $"Es wurde keine verwendbare MKVToolNix-Installation für {executableName} gefunden.");
        }

        return string.Equals(executableName, "mkvmerge.exe", StringComparison.OrdinalIgnoreCase)
            ? resolvedPaths.MkvMergePath
            : resolvedPaths.MkvPropEditPath;
    }
}
