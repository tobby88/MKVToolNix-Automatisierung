using System.Globalization;
using System.Reflection;
using System.Text;
using System.Windows;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Schreibt Notfallprotokolle für unbehandelte Ausnahmen, die nicht mehr durch die normalen
/// Modul-Fehlerpfade abgefangen werden.
/// </summary>
internal static class CrashLogService
{
    private const string CrashLogPrefix = "Crash";
    private static bool _globalHandlersRegistered;

    /// <summary>
    /// Registriert globale .NET-/WPF-Handler. Dispatcherfehler werden protokolliert und als
    /// Dialog angezeigt, Hintergrundthread-Abstürze können dagegen nur noch vor Prozessende
    /// protokolliert werden.
    /// </summary>
    public static void RegisterGlobalHandlers(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (_globalHandlersRegistered)
        {
            return;
        }

        _globalHandlersRegistered = true;

        app.DispatcherUnhandledException += (_, e) =>
        {
            var crashLogPath = TryWriteCrashLog("Dispatcher", e.Exception);
            var message = crashLogPath is null
                ? "Ein unerwarteter Fehler ist aufgetreten. Das Crash-Protokoll konnte nicht geschrieben werden."
                : $"Ein unerwarteter Fehler ist aufgetreten.\n\nCrash-Protokoll:\n{crashLogPath}";
            MessageBox.Show(message, "Unerwarteter Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            TryWriteCrashLog("AppDomain", e.ExceptionObject);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            TryWriteCrashLog("TaskScheduler", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>
    /// Schreibt ein Crash-Protokoll in den portablen Logordner. Fehler beim Schreiben werden
    /// absichtlich unterdrückt, weil dieser Pfad selbst während der Fehlerbehandlung läuft.
    /// </summary>
    public static string? TryWriteCrashLog(string source, object? exceptionObject, string? logDirectory = null)
    {
        try
        {
            return WriteCrashLog(source, exceptionObject, logDirectory);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Schreibt ein Crash-Protokoll und liefert den erzeugten Dateipfad zurück.
    /// </summary>
    /// <param name="source">Technische Quelle des globalen Fehlerhandlers.</param>
    /// <param name="exceptionObject">Die protokollierte Ausnahme oder ein fremdes Exception-Objekt.</param>
    /// <param name="logDirectory">Optionaler Zielordner für Tests; standardmäßig der portable Logordner.</param>
    public static string WriteCrashLog(string source, object? exceptionObject, string? logDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var targetDirectory = logDirectory ?? PortableAppStorage.LogsDirectory;
        Directory.CreateDirectory(targetDirectory);

        var timestamp = DateTimeOffset.Now;
        var fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"{CrashLogPrefix} - {timestamp:yyyy-MM-dd HH-mm-ss-fff} - {SanitizeFileNamePart(source)}.log.txt");
        var filePath = Path.Combine(targetDirectory, fileName);
        File.WriteAllText(filePath, BuildCrashLogText(source, exceptionObject, timestamp), Encoding.UTF8);
        return filePath;
    }

    private static string BuildCrashLogText(string source, object? exceptionObject, DateTimeOffset timestamp)
    {
        var builder = new StringBuilder();
        builder.AppendLine("MkvToolnixAutomatisierung Crash-Protokoll");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Zeitpunkt: {timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Quelle: {source}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Version: {Assembly.GetExecutingAssembly().GetName().Version}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Prozess: {Environment.ProcessPath}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Basisordner: {AppContext.BaseDirectory}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"OS: {Environment.OSVersion}");
        builder.AppendLine(CultureInfo.InvariantCulture, $".NET: {Environment.Version}");
        builder.AppendLine();
        builder.AppendLine("Ausnahme:");
        builder.AppendLine(exceptionObject?.ToString() ?? "<null>");
        return builder.ToString();
    }

    private static string SanitizeFileNamePart(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalidCharacters.Contains(character) ? '_' : character);
        }

        return builder.ToString();
    }
}
