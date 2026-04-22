using System.ComponentModel;
using System.Windows;
using MkvToolnixAutomatisierung.ViewModels;

namespace MkvToolnixAutomatisierung.Windows;

/// <summary>
/// Kleines Vorschaltfenster für sichtbaren Startfortschritt vor dem Hauptfenster.
/// </summary>
public partial class StartupProgressWindow : Window
{
    private bool _allowClose;

    internal StartupProgressWindow(StartupProgressWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
        Closing += StartupProgressWindow_Closing;
    }

    /// <summary>
    /// Meldet dem Hostprogramm einen Benutzerwunsch, den Start abzubrechen.
    /// </summary>
    internal event EventHandler? StartupCancellationRequested;

    /// <summary>
    /// Schließt das Fenster bewusst aus dem Startcode heraus, ohne den Abbruchpfad auszulösen.
    /// </summary>
    internal void CloseFromProgram()
    {
        _allowClose = true;
        Close();
    }

    private void StartupProgressWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        StartupCancellationRequested?.Invoke(this, EventArgs.Empty);
    }
}
