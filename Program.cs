using System.Windows;

using MkvToolnixAutomatisierung.ViewModels;
using MkvToolnixAutomatisierung.Windows;

namespace MkvToolnixAutomatisierung;

/// <summary>
/// Startet die WPF-Anwendung bewusst ohne klassischen App.xaml-Bootstrap, damit der Einstieg komplett im Code nachvollziehbar bleibt.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            var app = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };

            using var bootstrapper = new AppBootstrapper();
            var startupViewModel = new StartupProgressWindowViewModel();
            var startupWindow = new StartupProgressWindow(startupViewModel);
            var startupHandled = false;

            async void StartAsync()
            {
                if (startupHandled)
                {
                    return;
                }

                startupHandled = true;

                try
                {
                    var window = await bootstrapper.CreateMainWindowAsync(startupViewModel);
                    app.MainWindow = window;
                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    window.Show();
                    startupWindow.Close();
                    bootstrapper.ShowStartupWarnings();
                }
                catch (Exception ex)
                {
                    startupWindow.Close();
                    MessageBox.Show(
                        $"Die Anwendung konnte nicht gestartet werden.\n\n{ex.Message}",
                        "Startfehler",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    app.Shutdown();
                }
            }

            startupWindow.ContentRendered += (_, _) => StartAsync();
            app.MainWindow = startupWindow;
            startupWindow.Show();
            app.Run();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Die Anwendung konnte nicht gestartet werden.\n\n{ex.Message}",
                "Startfehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
