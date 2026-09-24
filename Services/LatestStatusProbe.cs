namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Entprellt reine Status-I/O außerhalb des UI-Threads. Höchstens eine native Abfrage je
/// Instanz läuft gleichzeitig; nicht abbrechbare SMB-Aufrufe erzeugen keine Threadflut.
/// Eine neuere Anforderung entwertet alte Rückmeldungen auch nach abgeschlossenem I/O.
/// </summary>
internal sealed class LatestStatusProbe<T> : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _pending;
    private long _revision;
    public Task Completion { get; private set; } = Task.CompletedTask;

    public void Request(Func<T> query, Action<T> apply, Action<Exception>? failed = null, int delayMilliseconds = 100)
    {
        Cancel();
        var revision = _revision;
        var source = new CancellationTokenSource();
        _pending = source;
        Completion = RunAsync();
        async Task RunAsync()
        {
            try
            {
                await Task.Delay(delayMilliseconds, source.Token);
                await _gate.WaitAsync(source.Token);
                T result;
                try { result = await Task.Run(query, source.Token); }
                finally { _gate.Release(); }
                if (revision == _revision && !source.IsCancellationRequested) apply(result);
            }
            catch (OperationCanceledException) when (source.IsCancellationRequested) { }
            catch (Exception ex) { if (revision == _revision) failed?.Invoke(ex); }
            finally
            {
                if (ReferenceEquals(_pending, source)) _pending = null;
                source.Dispose();
            }
        }
    }

    public void Cancel()
    {
        _revision++;
        _pending?.Cancel();
        _pending = null;
    }

    public void Dispose() => Cancel();
}
