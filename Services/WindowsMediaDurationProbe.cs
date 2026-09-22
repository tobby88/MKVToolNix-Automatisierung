using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Liest Laufzeiten über Windows-Shell-Eigenschaften als Fallback, wenn ffprobe nicht verfügbar ist.
/// </summary>
public sealed class WindowsMediaDurationProbe : IMediaDurationProbe
{
    private readonly ConcurrentDictionary<string, CachedFileValue<TimeSpan?>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, TimeSpan?> _durationReader;

    /// <summary>Initializes the optional Windows Media Player duration fallback.</summary>
    public WindowsMediaDurationProbe() : this(ReadDurationCore)
    {
    }

    internal WindowsMediaDurationProbe(Func<string, TimeSpan?> durationReader)
    {
        _durationReader = durationReader;
    }

    /// <inheritdoc />
    public TimeSpan? TryReadDuration(string filePath)
    {
        var snapshot = FileStateSnapshot.TryCreate(filePath);
        if (snapshot is null)
        {
            return null;
        }

        if (_cache.TryGetValue(filePath, out var cachedValue) && cachedValue.Matches(snapshot))
        {
            return cachedValue.Value;
        }

        var duration = _durationReader(filePath);
        // COM availability and media readiness can fail transiently, just like ffprobe.
        if (duration is { } value && value > TimeSpan.Zero)
        {
            _cache[filePath] = new CachedFileValue<TimeSpan?>(snapshot.Value, duration);
        }
        else
        {
            _cache.TryRemove(filePath, out _);
            duration = null;
        }
        return duration;
    }

    private static TimeSpan? ReadDurationCore(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var playerType = Type.GetTypeFromProgID("WMPlayer.OCX");
        if (playerType is null)
        {
            return null;
        }

        object? player = null;
        object? media = null;

        try
        {
            player = Activator.CreateInstance(playerType);
            media = playerType.InvokeMember(
                "newMedia",
                BindingFlags.InvokeMethod,
                binder: null,
                target: player,
                args: [filePath]);

            if (media is null)
            {
                return null;
            }

            var durationValue = media.GetType().InvokeMember(
                "duration",
                BindingFlags.GetProperty,
                binder: null,
                target: media,
                args: null);

            if (durationValue is double seconds && seconds > 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }

            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            ReleaseComObject(media);
            ReleaseComObject(player);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}
