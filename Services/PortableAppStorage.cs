using System.Reflection;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Definiert die portable Ordnerstruktur relativ zur EXE und prüft beim Start die Schreibbarkeit.
/// </summary>
internal static class PortableAppStorage
{
    private const string DataDirectoryName = "Data";
    private const string LogsDirectoryName = "Logs";
    private const string ToolsDirectoryName = "Tools";
    private const string SettingsFileName = "settings.json";
    private const string ReadmeFileName = "README.md";
    private const string EmbeddedReadmeResourceName = "README.md";

    public static string AppDirectory => Path.GetFullPath(AppContext.BaseDirectory);

    public static string DataDirectory => Path.Combine(AppDirectory, DataDirectoryName);

    public static string LogsDirectory => Path.Combine(AppDirectory, LogsDirectoryName);

    public static string ToolsDirectory => Path.Combine(AppDirectory, ToolsDirectoryName);

    public static string ReadmeFilePath => Path.Combine(AppDirectory, ReadmeFileName);

    public static string SettingsFilePath => Path.Combine(DataDirectory, SettingsFileName);

    public static string SettingsBackupFilePath => SettingsFilePath + ".bak";

    public static string? PrepareForStartup()
    {
        var warnings = new List<string>();

        if (!EnsureDataDirectoryExists(warnings))
        {
            return string.Join(Environment.NewLine + Environment.NewLine, warnings.Distinct(StringComparer.Ordinal));
        }

        EnsureOptionalDirectoryExists(LogsDirectory);
        EnsureOptionalDirectoryExists(ToolsDirectory);
        EnsureBundledReadmeFileExists(warnings);
        VerifyDataDirectoryWritable(warnings);

        return warnings.Count == 0
            ? null
            : string.Join(Environment.NewLine + Environment.NewLine, warnings.Distinct(StringComparer.Ordinal));
    }

    public static void EnsureDataDirectoryForSave()
    {
        Directory.CreateDirectory(DataDirectory);
    }

    public static void EnsureLogsDirectoryForSave()
    {
        Directory.CreateDirectory(LogsDirectory);
    }

    public static void EnsureToolsDirectoryForSave()
    {
        Directory.CreateDirectory(ToolsDirectory);
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

    private static void EnsureBundledReadmeFileExists(List<string> warnings)
    {
        try
        {
            if (File.Exists(ReadmeFilePath))
            {
                return;
            }

            using var readmeStream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(EmbeddedReadmeResourceName);
            if (readmeStream is null)
            {
                return;
            }

            using var outputStream = File.Create(ReadmeFilePath);
            readmeStream.CopyTo(outputStream);
        }
        catch (Exception ex)
        {
            warnings.Add(
                "Die eingebettete README konnte nicht neben der Anwendung angelegt werden."
                + Environment.NewLine
                + ReadmeFilePath
                + Environment.NewLine
                + Environment.NewLine
                + $"Fehler beim Schreiben der Hilfedatei: {ex.Message}");
        }
    }
}
