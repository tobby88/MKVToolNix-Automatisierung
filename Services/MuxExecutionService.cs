using System.Diagnostics;
using System.Runtime.ExceptionServices;
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
    /// <param name="onOutput">Optionaler, pro Aufruf serialisierter Callback für Standardausgabe und Standardfehler.</param>
    /// <param name="cancellationToken">Optionales Abbruchsignal. Bei Abbruch wird der gestartete Prozess beendet.</param>
    /// <returns>Exitcode des Prozesses.</returns>
    /// <remarks>
    /// Fehler im Ausgabe-Callback beenden den Prozess und werden über den zurückgegebenen Task
    /// weitergereicht. Sie dürfen weder einen Prozess-Eventthread unbehandelt verlassen noch
    /// die Veröffentlichung einer nur teilweise verarbeiteten Ausgabe zulassen.
    /// </remarks>
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
        void StopProcess()
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
        }
        using var registration = cancellationToken.Register(StopProcess);

        var outputSync = new object();
        ExceptionDispatchInfo? outputFailure = null;
        void ForwardOutput(object sender, DataReceivedEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.Data)) return;
            lock (outputSync)
            {
                if (outputFailure is not null) return;
                try
                {
                    onOutput?.Invoke(MojibakeRepair.NormalizeLikelyMojibake(args.Data));
                }
                catch (Exception exception)
                {
                    outputFailure = ExceptionDispatchInfo.Capture(exception);
                    StopProcess();
                }
            }
        }
        process.OutputDataReceived += ForwardOutput;
        process.ErrorDataReceived += ForwardOutput;

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
        lock (outputSync)
        {
            outputFailure?.Throw();
        }
        if (outputTransaction is not null && process.ExitCode is 0 or 1)
        {
            outputTransaction.Commit(cancellationToken);
        }

        return process.ExitCode;
    }
}
