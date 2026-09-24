namespace MkvToolnixAutomatisierung.Services;

/// <summary>Prüft Windows-Dateinamen vor einer schreibenden Operation, ohne Dateien anzulegen.</summary>
internal static class WindowsPathValidation
{
    /// <summary>Verwirft Gerätealiase, abgeschnittene Pfadsegmente und zu lange Dateinamen.</summary>
    public static void ValidateFileName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var stem = name.Split('.')[0].TrimEnd(' ');
        var reserved = stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase)
            || (stem.Length == 4 && "123456789\u00b9\u00b2\u00b3".Contains(stem[3])
                && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)));
        if (reserved || name.Length > 255 || name is "." or ".."
            || name.EndsWith('.') || name.EndsWith(' ')
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Ungültiger Windows-Dateiname: reservierter Name, unzulässige Zeichen oder mehr als 255 Zeichen.", nameof(name));
        }
    }

    /// <summary>Prüft Komponenten und das Extended-Windows-Limit; lange .NET-Pfade bleiben erlaubt.</summary>
    public static void ValidateFilePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (fullPath.Length >= 32760)
        {
            throw new PathTooLongException("Der Zielpfad überschreitet das Windows-Pfadlängenlimit.");
        }

        foreach (var component in fullPath[Path.GetPathRoot(fullPath)!.Length..]
                     .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            ValidateFileName(component);
        }
    }
}
