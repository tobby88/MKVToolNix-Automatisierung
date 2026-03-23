using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace MkvToolnixAutomatisierung.Services;

public sealed class FfprobeDurationProbe : IMediaDurationProbe
{
    private readonly ConcurrentDictionary<string, TimeSpan?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pathSync = new();
    private readonly FfprobeLocator _locator;
    private string? _ffprobePath;

    public FfprobeDurationProbe(FfprobeLocator locator)
    {
        _locator = locator;
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(GetCurrentFfprobePath());

    public string? ExecutablePath => GetCurrentFfprobePath();

    public TimeSpan? TryReadDuration(string filePath)
    {
        var ffprobePath = GetCurrentFfprobePath();
        if (string.IsNullOrWhiteSpace(ffprobePath) || !File.Exists(filePath))
        {
            return null;
        }

        return _cache.GetOrAdd(filePath, path => ReadDurationCore(path, ffprobePath));
    }

    private string? GetCurrentFfprobePath()
    {
        var resolvedPath = _locator.TryFindFfprobePath();

        lock (_pathSync)
        {
            if (!string.Equals(_ffprobePath, resolvedPath, StringComparison.OrdinalIgnoreCase))
            {
                _ffprobePath = resolvedPath;
                _cache.Clear();
            }

            return _ffprobePath;
        }
    }

    private static TimeSpan? ReadDurationCore(string filePath, string ffprobePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
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
