using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MkvToolnixAutomatisierung.Services;

public sealed class WindowsMediaDurationProbe
{
    private readonly ConcurrentDictionary<string, TimeSpan?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public TimeSpan? TryReadDuration(string filePath)
    {
        return _cache.GetOrAdd(filePath, ReadDurationCore);
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
