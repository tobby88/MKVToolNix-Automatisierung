using System.Windows;
using MkvToolnixAutomatisierung.ViewModels;

namespace MkvToolnixAutomatisierung.Windows;

/// <summary>
/// Kleines Vorschaltfenster für sichtbaren Startfortschritt vor dem Hauptfenster.
/// </summary>
public partial class StartupProgressWindow : Window
{
    internal StartupProgressWindow(StartupProgressWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
    }
}
