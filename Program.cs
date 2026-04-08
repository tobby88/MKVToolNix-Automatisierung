using System.Windows;

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
                ShutdownMode = ShutdownMode.OnMainWindowClose
            };

            using var bootstrapper = new AppBootstrapper();
            var window = bootstrapper.CreateMainWindow();
            app.Run(window);
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
