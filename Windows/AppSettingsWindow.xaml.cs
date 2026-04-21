using System.Windows;
using MkvToolnixAutomatisierung.ViewModels;

namespace MkvToolnixAutomatisierung.Windows;

/// <summary>
/// Schlanker Window-Host für den zentralen Einstellungsdialog.
/// </summary>
public partial class AppSettingsWindow : Window
{
    private readonly AppSettingsWindowViewModel _viewModel;

    internal AppSettingsWindow(AppSettingsWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _viewModel.CloseRequested += ViewModelOnCloseRequested;
        DataContext = _viewModel;
        SettingsTabControl.SelectedIndex = (int)_viewModel.SelectedPage;
        Closed += AppSettingsWindow_Closed;
    }

    private void AppSettingsWindow_Closed(object? sender, EventArgs e)
    {
        _viewModel.CloseRequested -= ViewModelOnCloseRequested;
    }

    private void ViewModelOnCloseRequested(object? sender, bool accepted)
    {
        DialogResult = accepted;
    }

    private void SelectArchiveButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectArchiveRootDirectory();
    }

    private void SelectMkvToolNixButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectMkvToolNixDirectory();
    }

    private void SelectFfprobeButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectFfprobePath();
    }

    private async void TestEmbyConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.TestEmbyConnectionAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Emby-Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.SaveAndClose();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Cancel();
    }
}
