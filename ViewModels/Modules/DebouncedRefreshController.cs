namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Steuert debouncte Hintergrund-Refreshes mit Versionsschutz gegen veraltete Rückschreibungen.
/// </summary>
internal sealed class DebouncedRefreshController : IDisposable
{
    private readonly TimeSpan _delay;
    private CancellationTokenSource? _currentRefreshCts;
    private int _version;
    private bool _disposed;

    /// <summary>
    /// Erstellt einen Controller mit fester Debounce-Dauer.
    /// </summary>
    /// <param name="delay">Wartezeit zwischen Schedule und tatsächlichem Refresh.</param>
    public DebouncedRefreshController(TimeSpan delay)
    {
        _delay = delay;
    }

    /// <summary>
    /// Zuletzt geplante Refresh-Task, sofern aktuell noch eine Planung oder Ausführung aktiv ist.
    /// Bereits abgeschlossene Refreshes werden vor Abschluss der Task wieder ausgetragen.
    /// </summary>
    public Task? CurrentTask { get; private set; }

    /// <summary>
    /// Plant einen debouncten Refresh und verwirft dabei automatisch ältere Planungen.
    /// </summary>
    /// <param name="refreshAsync">Asynchrone Refresh-Aktion inklusive Versionsnummer und Cancellation-Token.</param>
    public void Schedule(Func<int, CancellationToken, Task> refreshAsync)
    {
        ArgumentNullException.ThrowIfNull(refreshAsync);
        ObjectDisposedException.ThrowIf(_disposed, this);

        CancelCore();

        var version = Interlocked.Increment(ref _version);
        var cancellationSource = new CancellationTokenSource();
        _currentRefreshCts = cancellationSource;
        var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CurrentTask = completionSource.Task;
        _ = RunRefreshAsync(refreshAsync, version, cancellationSource, completionSource);
    }

    /// <summary>
    /// Bricht eine geplante oder laufende Refresh-Kette ab.
    /// </summary>
    /// <param name="invalidateInFlightRefreshes">
    /// <see langword="true"/>, wenn bereits gestartete Refreshes ihre Ergebnisse ebenfalls verwerfen sollen.
    /// </param>
    public void Cancel(bool invalidateInFlightRefreshes = false)
    {
        CancelCore();
        CurrentTask = null;

        if (invalidateInFlightRefreshes)
        {
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>
    /// Prüft, ob eine beim Refresh erhaltene Versionsnummer noch zur aktuellsten Planung gehört.
    /// </summary>
    /// <param name="version">Version aus der geplanten Refresh-Ausführung.</param>
    /// <returns><see langword="true"/>, wenn die Version noch aktuell ist.</returns>
    public bool IsCurrent(int version)
    {
        return !_disposed && version == Volatile.Read(ref _version);
    }

    public void Dispose()
    {
        _disposed = true;
        Cancel(invalidateInFlightRefreshes: true);
    }

    private async Task RunRefreshAsync(
        Func<int, CancellationToken, Task> refreshAsync,
        int version,
        CancellationTokenSource cancellationSource,
        TaskCompletionSource completionSource)
    {
        var cancellationToken = cancellationSource.Token;
        Exception? failure = null;

        try
        {
            await Task.Delay(_delay, cancellationToken);
            await refreshAsync(version, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            if (ReferenceEquals(_currentRefreshCts, cancellationSource))
            {
                _currentRefreshCts = null;
                CurrentTask = null;
            }

            // Laufende Nutzer des Tokens duerfen auch nach Cancel noch Registrierungen anlegen.
            // Erst die abgeschlossene Aktion gibt ihre Tokenquelle frei.
            cancellationSource.Dispose();

            if (failure is null)
            {
                completionSource.TrySetResult();
            }
            else
            {
                completionSource.TrySetException(failure);
            }
        }
    }

    private void CancelCore()
    {
        var cancellationSource = _currentRefreshCts;
        _currentRefreshCts = null;
        cancellationSource?.Cancel();
    }
}
