using System.Security.Principal;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Verhindert konkurrierende App-Prozesse desselben Windows-Benutzers auch über Sitzungen
/// und portable Installationsorte hinweg. Fremde Programme bleiben durch Datei-Sperren geschützt.
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private bool _disposed;
    private SingleInstanceGuard(Mutex mutex) => _mutex = mutex;

    /// <summary>
    /// Nicht blockierender Startschutz. Ein nach Prozessabsturz verwaister Mutex wird übernommen.
    /// Besitzer und Dispose müssen auf demselben Thread laufen (Program.Main bleibt im UI-Thread).
    /// </summary>
    internal static SingleInstanceGuard? TryAcquire(string? name = null)
    {
        name ??= @"Global\MkvToolnixAutomatisierung-" + WindowsIdentity.GetCurrent().User?.Value;
        var mutex = new Mutex(false, name);
        try
        {
            if (mutex.WaitOne(0)) return new SingleInstanceGuard(mutex);
        }
        catch (AbandonedMutexException)
        {
            return new SingleInstanceGuard(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
