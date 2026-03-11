using System.Diagnostics;

namespace MkvToolnixAutomatisierung.Services;

public sealed class MuxExecutionService
{
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