using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace MkvToolnixAutomatisierung.Services;

public sealed class FfprobeDurationProbe : IMediaDurationProbe
{
    private readonly ConcurrentDictionary<string, TimeSpan?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _ffprobePath;

    public FfprobeDurationProbe(FfprobeLocator locator)
    {
        _ffprobePath = locator.TryFindFfprobePath();
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_ffprobePath);

    public string? ExecutablePath => _ffprobePath;

    public TimeSpan? TryReadDuration(string filePath)
    {
        return _cache.GetOrAdd(filePath, ReadDurationCore);
    }

    private TimeSpan? ReadDurationCore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(_ffprobePath) || !File.Exists(filePath))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _ffprobePath,
            Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                return null;
            }

            if (!double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                return null;
            }

            return seconds > 0 ? TimeSpan.FromSeconds(seconds) : null;
        }
        catch
        {
            return null;
        }
    }
}
