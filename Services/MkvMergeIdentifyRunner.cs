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
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = CreateIdentifyStartInfo(mkvMergePath, inputFilePath);

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
            }
        });

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);
        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;
        return ParseIdentifyResult(standardOutput, standardError, process.ExitCode);
    }

    public static JsonDocument Identify(string mkvMergePath, string inputFilePath)
    {
        var startInfo = CreateIdentifyStartInfo(mkvMergePath, inputFilePath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("mkvmerge konnte nicht gestartet werden.");

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var standardOutput = standardOutputTask.GetAwaiter().GetResult();
        var standardError = standardErrorTask.GetAwaiter().GetResult();
        return ParseIdentifyResult(standardOutput, standardError, process.ExitCode);
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

    private static JsonDocument ParseIdentifyResult(string standardOutput, string standardError, int exitCode)
    {
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
