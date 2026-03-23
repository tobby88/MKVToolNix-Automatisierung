namespace MkvToolnixAutomatisierung.Services;

public static class PortableAppStorage
{
    private const string DataDirectoryName = "Data";
    private const string CacheDirectoryName = "Cache";
    private const string LogsDirectoryName = "Logs";
    private const string SettingsFileName = "settings.json";

    public static string AppDirectory => Path.GetFullPath(AppContext.BaseDirectory);

    public static string DataDirectory => Path.Combine(AppDirectory, DataDirectoryName);

    public static string CacheDirectory => Path.Combine(AppDirectory, CacheDirectoryName);

    public static string LogsDirectory => Path.Combine(AppDirectory, LogsDirectoryName);

    public static string SettingsFilePath => Path.Combine(DataDirectory, SettingsFileName);

    public static string SettingsBackupFilePath => SettingsFilePath + ".bak";

    public static string? PrepareForStartup()
    {
        var warnings = new List<string>();

        if (!EnsureDataDirectoryExists(warnings))
        {
            return string.Join(Environment.NewLine + Environment.NewLine, warnings.Distinct(StringComparer.Ordinal));
        }

        EnsureOptionalDirectoryExists(CacheDirectory);
        EnsureOptionalDirectoryExists(LogsDirectory);
        VerifyDataDirectoryWritable(warnings);

        return warnings.Count == 0
            ? null
            : string.Join(Environment.NewLine + Environment.NewLine, warnings.Distinct(StringComparer.Ordinal));
    }

    public static void EnsureDataDirectoryForSave()
    {
        Directory.CreateDirectory(DataDirectory);
    }

    private static bool EnsureDataDirectoryExists(List<string> warnings)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            return true;
        }
        catch (Exception ex)
        {
            warnings.Add(
                "Portable Modus benötigt ein beschreibbares Anwendungsverzeichnis."
                + Environment.NewLine
                + "Die lokalen Daten sollten hier liegen:"
                + Environment.NewLine
                + DataDirectory
                + Environment.NewLine
                + Environment.NewLine
                + $"Fehler beim Vorbereiten des Datenordners: {ex.Message}");
            return false;
        }
    }

    private static void EnsureOptionalDirectoryExists(string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
        }
        catch
        {
        }
    }

    private static void VerifyDataDirectoryWritable(List<string> warnings)
    {
        var probeFilePath = Path.Combine(DataDirectory, ".portable-write-test");

        try
        {
            File.WriteAllText(probeFilePath, "ok");
            File.Delete(probeFilePath);
        }
        catch (Exception ex)
        {
            warnings.Add(
                "Portable Modus benötigt Schreibzugriff auf den lokalen Datenordner."
                + Environment.NewLine
                + DataDirectory
                + Environment.NewLine
                + Environment.NewLine
                + $"Fehler beim Schreibtest: {ex.Message}");
        }
    }
}
