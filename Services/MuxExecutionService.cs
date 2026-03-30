using System.Diagnostics;
using System.Text;

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
    /// <param name="cancellationToken">Optionales Abbruchsignal. Bei Abbruch wird der gestartete Prozess beendet.</param>
    /// <returns>Exitcode des Prozesses.</returns>
    public async Task<int> ExecuteAsync(
        string mkvMergePath,
        IReadOnlyList<string> arguments,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = mkvMergePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("mkvmerge konnte nicht gestartet werden.");
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ein bereits beendeter Prozess darf den Abbruchpfad nicht stören.
            }
        });

        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                onOutput?.Invoke(MojibakeRepair.NormalizeLikelyMojibake(args.Data));
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                onOutput?.Invoke(MojibakeRepair.NormalizeLikelyMojibake(args.Data));
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
