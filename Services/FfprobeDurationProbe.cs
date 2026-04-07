using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Liest Laufzeiten über ffprobe und cached Ergebnisse dateibezogen.
/// </summary>
public sealed class FfprobeDurationProbe : IMediaDurationProbe
{
    private static readonly TimeSpan SuccessfulLookupRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FailedLookupRefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(10);
    private readonly ConcurrentDictionary<string, CachedFileValue<TimeSpan?>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pathSync = new();
    private readonly IFfprobeLocator _locator;
    private string? _ffprobePath;
    private DateTime _nextLookupAfterUtc = DateTime.MinValue;

    /// <summary>
    /// Initialisiert den ffprobe-basierten Laufzeit-Probe-Dienst.
    /// </summary>
    /// <param name="locator">Liefert bei Bedarf den aktuell nutzbaren Pfad zur <c>ffprobe.exe</c>.</param>
    public FfprobeDurationProbe(IFfprobeLocator locator)
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
        var nowUtc = DateTime.UtcNow;
        lock (_pathSync)
        {
            if (!string.IsNullOrWhiteSpace(_ffprobePath) && !File.Exists(_ffprobePath))
            {
                _ffprobePath = null;
                _nextLookupAfterUtc = DateTime.MinValue;
                _cache.Clear();
            }

            if (nowUtc < _nextLookupAfterUtc)
            {
                return _ffprobePath;
            }
        }

        var resolvedPath = _locator.TryFindFfprobePath();

        lock (_pathSync)
        {
            if (!string.Equals(_ffprobePath, resolvedPath, StringComparison.OrdinalIgnoreCase))
            {
                _ffprobePath = resolvedPath;
                _cache.Clear();
            }

            _nextLookupAfterUtc = nowUtc + (string.IsNullOrWhiteSpace(resolvedPath)
                ? FailedLookupRefreshInterval
                : SuccessfulLookupRefreshInterval);
            return _ffprobePath;
        }
    }

    private static TimeSpan? ReadDurationCore(string filePath, string ffprobePath)
    {
        return ReadDurationCoreAsync(filePath, ffprobePath).GetAwaiter().GetResult();
    }

    private static async Task<TimeSpan?> ReadDurationCoreAsync(string filePath, string ffprobePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add("format=duration");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
        startInfo.ArgumentList.Add(filePath);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(ProcessTimeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                KillProcessTree(process);
                await DrainProcessOutputAfterKillAsync(process, standardOutputTask, standardErrorTask);
                return null;
            }

            var output = await standardOutputTask;
            _ = await standardErrorTask;

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

    private static void KillProcessTree(Process process)
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
    }

    private static async Task DrainProcessOutputAfterKillAsync(
        Process process,
        Task<string> standardOutputTask,
        Task<string> standardErrorTask)
    {
        try
        {
            await process.WaitForExitAsync();
            await Task.WhenAll(standardOutputTask, standardErrorTask);
        }
        catch
        {
        }
    }
}
