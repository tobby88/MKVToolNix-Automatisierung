using System.Windows;
using MkvToolnixAutomatisierung.ViewModels;

namespace MkvToolnixAutomatisierung;

/// <summary>
/// Schlanker Window-Host: das eigentliche Verhalten lebt vollständig im ViewModel.
/// </summary>
public partial class MainWindow : Window
{
    internal MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        MainWindowStartupLayout.ApplyTo(this, SystemParameters.WorkArea);
        DataContext = viewModel;
    }
}
