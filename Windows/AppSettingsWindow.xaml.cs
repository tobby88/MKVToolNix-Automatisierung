using System.Windows;
using System.ComponentModel;
using MkvToolnixAutomatisierung.ViewModels;

namespace MkvToolnixAutomatisierung.Windows;

/// <summary>
/// Schlanker Window-Host für den zentralen Einstellungsdialog.
/// </summary>
public partial class AppSettingsWindow : Window
{
    private readonly AppSettingsWindowViewModel _viewModel;
    private bool _isSynchronizingSecretFields;

    internal AppSettingsWindow(AppSettingsWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _viewModel.CloseRequested += ViewModelOnCloseRequested;
        DataContext = _viewModel;
        SettingsTabControl.SelectedIndex = (int)_viewModel.SelectedPage;
        SynchronizeSecretFieldsFromViewModel();
        Closed += AppSettingsWindow_Closed;
        Closing += AppSettingsWindow_Closing;
    }

    private void AppSettingsWindow_Closed(object? sender, EventArgs e)
    {
        _viewModel.CloseRequested -= ViewModelOnCloseRequested;
    }

    private void AppSettingsWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_viewModel.IsInteractive)
        {
            return;
        }

        _viewModel.Cancel();
        e.Cancel = true;
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

    private void SelectMediathekViewButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectMediathekViewPath();
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

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.SaveAndCloseAsync();
        }
        catch (OperationCanceledException)
        {
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

    private void TvdbApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_isSynchronizingSecretFields)
        {
            _viewModel.TvdbApiKey = TvdbApiKeyBox.Password;
        }
    }

    private void TvdbPinBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_isSynchronizingSecretFields)
        {
            _viewModel.TvdbPin = TvdbPinBox.Password;
        }
    }

    private void EmbyApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_isSynchronizingSecretFields)
        {
            _viewModel.EmbyApiKey = EmbyApiKeyBox.Password;
        }
    }

    private void SynchronizeSecretFieldsFromViewModel()
    {
        _isSynchronizingSecretFields = true;
        try
        {
            TvdbApiKeyBox.Password = _viewModel.TvdbApiKey;
            TvdbPinBox.Password = _viewModel.TvdbPin;
            EmbyApiKeyBox.Password = _viewModel.EmbyApiKey;
        }
        finally
        {
            _isSynchronizingSecretFields = false;
        }
    }
}
