using System.Diagnostics;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Startet den externen mkvmerge-Prozess und liefert seine Konsolenausgabe fortlaufend an den Aufrufer zurück.
/// </summary>
public sealed class MuxExecutionService
{
    /// <summary>
    /// Startet mkvmerge mit einer vorbereiteten Argumentliste und liefert dessen Konsolenzeilen fortlaufend zurück.
    /// </summary>
    /// <param name="mkvMergePath">Pfad zur auszuführenden mkvmerge-Executable.</param>
    /// <param name="arguments">Bereits aufgelöste Argumentliste des Plans.</param>
    /// <param name="onOutput">Optionaler Callback für Standardausgabe und Standardfehler.</param>
    /// <returns>Exitcode des Prozesses.</returns>
    public async Task<int> ExecuteAsync(string mkvMergePath, IReadOnlyList<string> arguments, Action<string>? onOutput = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = mkvMergePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("mkvmerge konnte nicht gestartet werden.");

        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                onOutput?.Invoke(args.Data);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                onOutput?.Invoke(args.Data);
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
