using System.Windows;

namespace MkvToolnixAutomatisierung;

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

            var window = new MainWindow();
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
