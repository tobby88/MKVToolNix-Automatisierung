using System.Windows.Input;

namespace MkvToolnixAutomatisierung.ViewModels.Commands;

/// <summary>
/// ICommand-Variante für asynchrone Aktionen; Busy-State und Fehlerdarstellung werden vom aufrufenden ViewModel vorgegeben.
/// </summary>
internal sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _executeAsync;
    private readonly Action<Exception> _handleException;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;

    /// <summary>
    /// Erstellt einen asynchronen Command ohne zusätzliche CanExecute-Regel.
    /// </summary>
    /// <param name="executeAsync">Asynchrone Command-Aktion.</param>
    /// <param name="handleException">Zentrale Fehlerbehandlung für unerwartete Ausnahmen.</param>
    public AsyncRelayCommand(Func<Task> executeAsync, Action<Exception> handleException)
        : this(executeAsync, canExecute: null, handleException)
    {
    }

    /// <summary>
    /// Erstellt einen asynchronen Command mit optionaler CanExecute-Regel und expliziter Fehlerbehandlung.
    /// </summary>
    /// <param name="executeAsync">Asynchrone Command-Aktion.</param>
    /// <param name="canExecute">Optionale zusätzliche Aktivierungsbedingung.</param>
    /// <param name="handleException">Zentrale Fehlerbehandlung für unerwartete Ausnahmen.</param>
    public AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute, Action<Exception> handleException)
    {
        ArgumentNullException.ThrowIfNull(executeAsync);
        ArgumentNullException.ThrowIfNull(handleException);

        _executeAsync = executeAsync;
        _canExecute = canExecute;
        _handleException = handleException;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return !_isExecuting && (_canExecute?.Invoke() ?? true);
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        try
        {
            _isExecuting = true;
            RaiseCanExecuteChanged();
            await _executeAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _handleException(ex);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
