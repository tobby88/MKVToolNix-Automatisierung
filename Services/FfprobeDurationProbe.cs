using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Liest Laufzeiten über ffprobe und cached Ergebnisse dateibezogen.
/// </summary>
public sealed class FfprobeDurationProbe : IMediaDurationProbe
{
    private readonly ConcurrentDictionary<string, CachedFileValue<TimeSpan?>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pathSync = new();
    private readonly FfprobeLocator _locator;
    private string? _ffprobePath;

    /// <summary>
    /// Initialisiert den ffprobe-basierten Laufzeit-Probe-Dienst.
    /// </summary>
    /// <param name="locator">Liefert bei Bedarf den aktuell nutzbaren Pfad zur <c>ffprobe.exe</c>.</param>
    public FfprobeDurationProbe(FfprobeLocator locator)
    {
        _locator = locator;
    }

    /// <summary>
    /// Kennzeichnet, ob aktuell eine verwendbare <c>ffprobe.exe</c> gefunden wurde.
    /// </summary>
    public bool IsAvailable => !string.IsNullOrWhiteSpace(GetCurrentFfprobePath());

    /// <summary>
    /// Vollständiger Pfad zur aktuell verwendeten <c>ffprobe.exe</c>, falls vorhanden.
    /// </summary>
    public string? ExecutablePath => GetCurrentFfprobePath();

    /// <inheritdoc />
    public TimeSpan? TryReadDuration(string filePath)
    {
        var ffprobePath = GetCurrentFfprobePath();
        var snapshot = FileStateSnapshot.TryCreate(filePath);
        if (string.IsNullOrWhiteSpace(ffprobePath) || snapshot is null)
        {
            return null;
        }

        if (_cache.TryGetValue(filePath, out var cachedValue) && cachedValue.Matches(snapshot))
        {
            return cachedValue.Value;
        }

        var duration = ReadDurationCore(filePath, ffprobePath);
        _cache[filePath] = new CachedFileValue<TimeSpan?>(snapshot.Value, duration);
        return duration;
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
