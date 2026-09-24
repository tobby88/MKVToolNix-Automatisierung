using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Führt reine <c>mkvmerge --identify</c>-Prozesse aus und liefert das rohe JSON-Ergebnis zurück.
/// </summary>
internal static class MkvMergeIdentifyRunner
{
    public static async Task<JsonDocument> IdentifyAsync(
        string mkvMergePath,
        string inputFilePath,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var budget = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(60));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        var startInfo = CreateIdentifyStartInfo(mkvMergePath, inputFilePath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("mkvmerge konnte nicht gestartet werden.");
        using var registration = linked.Token.Register(() =>
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
            }
        });

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(linked.Token);
        var standardErrorTask = process.StandardError.ReadToEndAsync(linked.Token);
        // Detection also calls this API synchronously; never require its caller's UI context.
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            var standardOutput = await standardOutputTask.ConfigureAwait(false);
            var standardError = await standardErrorTask.ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            return ParseIdentifyResult(standardOutput, standardError, process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            try { await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or IOException) { }
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException($"mkvmerge --identify hat das Zeitlimit überschritten: {inputFilePath}");
        }
    }

    public static JsonDocument Identify(string mkvMergePath, string inputFilePath)
    {
        return IdentifyAsync(mkvMergePath, inputFilePath, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static ProcessStartInfo CreateIdentifyStartInfo(string mkvMergePath, string inputFilePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = mkvMergePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--identify");
        startInfo.ArgumentList.Add("--identification-format");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add(inputFilePath);
        return startInfo;
    }

    internal static JsonDocument ParseIdentifyResult(string standardOutput, string standardError, int exitCode)
    {
        // Valid JSON alone is not success: mkvmerge also emits JSON on fatal errors.
        if (exitCode is not (0 or 1))
        {
            var errorDetails = string.IsNullOrWhiteSpace(standardError)
                ? standardOutput.Trim()
                : standardError.Trim();
            throw new InvalidOperationException($"mkvmerge --identify ist fehlgeschlagen (Exitcode {exitCode}): {errorDetails}");
        }

        if (!string.IsNullOrWhiteSpace(standardOutput))
        {
            try
            {
                return JsonDocument.Parse(standardOutput);
            }
            catch (JsonException)
            {
                if (exitCode == 0)
                {
                    throw;
                }
            }
        }

        var details = string.IsNullOrWhiteSpace(standardError)
            ? "Es wurde keine gültige JSON-Antwort geliefert."
            : standardError.Trim();

        throw new InvalidOperationException($"mkvmerge --identify ist fehlgeschlagen: {details}");
    }
}
