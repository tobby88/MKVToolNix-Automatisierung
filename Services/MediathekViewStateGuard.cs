using System.Diagnostics;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>Verhindert portable Settings-Migration während eines möglichen MediathekView-Schreibers.</summary>
internal static class MediathekViewStateGuard
{
    public static void EnsureStopped()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                string name;
                try { name = process.ProcessName; }
                catch (InvalidOperationException) { continue; }
                // Portable startet ggf. nur javaw, dessen Befehlszeile ohne zusätzliche native
                // Rechte/Abhängigkeiten nicht zuverlässig lesbar ist. Lieber Migration aufschieben
                // als eine laufend veränderte Konfiguration als aktuellen Snapshot aktivieren.
                if (name.StartsWith("MediathekView", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("java", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("javaw", StringComparison.OrdinalIgnoreCase))
                    throw new ToolStateInUseException("Bitte MediathekView und laufende Java-Anwendungen vor der Übernahme portabler Einstellungen schließen. Die bisherige Installation bleibt aktiv.");
            }
        }
    }
}

internal sealed class ToolStateInUseException(string message) : IOException(message);
