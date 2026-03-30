namespace MkvToolnixAutomatisierung.ViewModels.Modules;

/// <summary>
/// Kapselt den aktuell cancelbaren Batch-Vorgang samt Benutzerabbruch.
/// </summary>
internal sealed class BatchOperationController : IDisposable
{
    private CancellationTokenSource? _currentOperationSource;

    /// <summary>
    /// Art des aktuell laufenden Batch-Vorgangs.
    /// </summary>
    public BatchOperationKind CurrentOperationKind { get; private set; }

    /// <summary>
    /// Gibt an, ob der aktuelle Vorgang noch aktiv abgebrochen werden kann.
    /// </summary>
    public bool CanCancelCurrentOperation => _currentOperationSource is { IsCancellationRequested: false };

    /// <summary>
    /// Beschriftung fuer den sichtbaren Abbrechen-Knopf passend zum laufenden Vorgang.
    /// </summary>
    public string CancelButtonText => CurrentOperationKind switch
    {
        BatchOperationKind.Scan => "Scan abbrechen",
        BatchOperationKind.Execution => "Batch abbrechen",
        _ => "Vorgang abbrechen"
    };

    /// <summary>
    /// Startet einen neuen cancelbaren Batch-Vorgang und liefert dessen Token zurueck.
    /// </summary>
    /// <param name="operationKind">Art des gestarteten Vorgangs.</param>
    /// <returns>Abbruchtoken des neuen Vorgangs.</returns>
    public CancellationToken Begin(BatchOperationKind operationKind)
    {
        if (_currentOperationSource is not null)
        {
            throw new InvalidOperationException("Ein Batch-Vorgang laeuft bereits.");
        }

        _currentOperationSource = new CancellationTokenSource();
        CurrentOperationKind = operationKind;
        return _currentOperationSource.Token;
    }

    /// <summary>
    /// Fordert fuer den aktuell laufenden Vorgang einen Benutzerabbruch an.
    /// </summary>
    /// <returns><see langword="true"/>, wenn der Abbruch neu ausgelöst wurde.</returns>
    public bool CancelCurrentOperation()
    {
        if (_currentOperationSource is null || _currentOperationSource.IsCancellationRequested)
        {
            return false;
        }

        _currentOperationSource.Cancel();
        return true;
    }

    /// <summary>
    /// Beendet den aktuell verknuepften Vorgang und gibt dessen Ressourcen frei.
    /// </summary>
    /// <param name="operationKind">Vorgangsart, die soeben abgeschlossen wurde.</param>
    public void Complete(BatchOperationKind operationKind)
    {
        if (CurrentOperationKind != operationKind)
        {
            return;
        }

        _currentOperationSource?.Dispose();
        _currentOperationSource = null;
        CurrentOperationKind = BatchOperationKind.None;
    }

    public void Dispose()
    {
        _currentOperationSource?.Cancel();
        _currentOperationSource?.Dispose();
        _currentOperationSource = null;
        CurrentOperationKind = BatchOperationKind.None;
    }
}

/// <summary>
/// Beschreibt die grossen, explizit abbrechbaren Batch-Vorgaenge.
/// </summary>
internal enum BatchOperationKind
{
    None = 0,
    Scan = 1,
    Execution = 2
}
