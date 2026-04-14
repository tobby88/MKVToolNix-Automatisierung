using System.Windows.Input;

namespace MkvToolnixAutomatisierung.ViewModels.Commands;

/// <summary>
/// Minimaler ICommand-Wrapper für synchrone UI-Aktionen.
/// </summary>
internal sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    /// <summary>
    /// Erstellt einen synchronen Command.
    /// </summary>
    /// <param name="execute">Auszuführende Aktion; darf nicht <see langword="null"/> sein.</param>
    /// <param name="canExecute">Optionale Aktivierungsbedingung.</param>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <inheritdoc/>
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc/>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    /// <inheritdoc/>
    public void Execute(object? parameter) => _execute();

    /// <summary>Löst <see cref="CanExecuteChanged"/> aus, damit WPF den aktivierten Zustand neu bewertet.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
