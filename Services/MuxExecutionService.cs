using System.Diagnostics;
using System.Text;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Startet externe MKVToolNix-Prozesse und liefert deren Konsolenausgabe fortlaufend an den Aufrufer zurück.
/// </summary>
public sealed class MuxExecutionService
{
    /// <summary>
    /// Startet ein MKVToolNix-Werkzeug mit einer vorbereiteten Argumentliste und liefert dessen Konsolenzeilen fortlaufend zurück.
    /// </summary>
    /// <param name="executablePath">Pfad zur auszuführenden MKVToolNix-Executable.</param>
    /// <param name="arguments">Bereits aufgelöste Argumentliste des Plans.</param>
    /// <param name="toolDisplayName">Lesbarer Name des gestarteten Werkzeugs für Fehlermeldungen.</param>
    /// <param name="onOutput">Optionaler Callback für Standardausgabe und Standardfehler.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal. Bei Abbruch wird der gestartete Prozess beendet.</param>
    /// <returns>Exitcode des Prozesses.</returns>
    public async Task<int> ExecuteAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string toolDisplayName,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Nur mkvmerge-Pläne haben --output. mkvpropedit bleibt ein bewusst direkter
        // Header-Eingriff. Die fachliche Planung/Vorschau behält immer den finalen Pfad.
        var outputIndex = arguments.ToList().IndexOf("--output");
        using var outputTransaction = outputIndex >= 0 && outputIndex + 1 < arguments.Count
            ? new MuxOutputTransaction(arguments[outputIndex + 1])
            : null;
        var executionArguments = arguments.ToArray();
        if (outputTransaction is not null)
        {
            executionArguments[outputIndex + 1] = outputTransaction.TemporaryOutputPath;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        foreach (var argument in executionArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"{toolDisplayName} konnte nicht gestartet werden.");
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

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Die Abbruchregistrierung beendet den Prozess. Vor dem Aufräumen muss er
            // die temporäre Datei tatsächlich freigegeben haben, nicht nur den Kill erhalten.
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (outputTransaction is not null && process.ExitCode is 0 or 1)
        {
            outputTransaction.Commit(cancellationToken);
        }

        return process.ExitCode;
    }
}
