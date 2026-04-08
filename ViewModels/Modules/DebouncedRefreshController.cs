namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Steuert debouncte Hintergrund-Refreshes mit Versionsschutz gegen veraltete Rückschreibungen.
/// </summary>
internal sealed class DebouncedRefreshController : IDisposable
{
    private readonly TimeSpan _delay;
    private CancellationTokenSource? _currentRefreshCts;
    private int _version;

    /// <summary>
    /// Erstellt einen Controller mit fester Debounce-Dauer.
    /// </summary>
    /// <param name="delay">Wartezeit zwischen Schedule und tatsächlichem Refresh.</param>
    public DebouncedRefreshController(TimeSpan delay)
    {
        _delay = delay;
    }

    /// <summary>
    /// Zuletzt geplante Refresh-Task, sofern aktuell eine Planung aktiv ist.
    /// </summary>
    public Task? CurrentTask { get; private set; }

    /// <summary>
    /// Plant einen debouncten Refresh und verwirft dabei automatisch ältere Planungen.
    /// </summary>
    /// <param name="refreshAsync">Asynchrone Refresh-Aktion inklusive Versionsnummer und Cancellation-Token.</param>
    public void Schedule(Func<int, CancellationToken, Task> refreshAsync)
    {
        ArgumentNullException.ThrowIfNull(refreshAsync);

        CancelCore();

        var version = Interlocked.Increment(ref _version);
        var cancellationSource = new CancellationTokenSource();
        _currentRefreshCts = cancellationSource;
        CurrentTask = RunRefreshAsync(refreshAsync, version, cancellationSource.Token);
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
        return version == Volatile.Read(ref _version);
    }

    public void Dispose()
    {
        Cancel();
    }

    private async Task RunRefreshAsync(
        Func<int, CancellationToken, Task> refreshAsync,
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_delay, cancellationToken);
            await refreshAsync(version, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void CancelCore()
    {
        _currentRefreshCts?.Cancel();
        _currentRefreshCts?.Dispose();
        _currentRefreshCts = null;
    }
}
