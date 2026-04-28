using System.Windows;

using MkvToolnixAutomatisierung.Services;
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
            CrashLogService.RegisterGlobalHandlers(app);

            using var bootstrapper = new AppBootstrapper();
            var startupViewModel = new StartupProgressWindowViewModel();
            var startupWindow = new StartupProgressWindow(startupViewModel);
            var startupProgress = new Progress<ManagedToolStartupProgress>(startupViewModel.Report);
            using var startupCancellation = new CancellationTokenSource();
            var startupHandled = false;
            var startupCompleted = false;

            startupWindow.StartupCancellationRequested += (_, _) =>
            {
                if (startupCompleted || startupCancellation.IsCancellationRequested)
                {
                    return;
                }

                startupViewModel.Report(new ManagedToolStartupProgress(
                    "Start wird abgebrochen...",
                    "Laufende Vorgänge werden beendet."));
                startupCancellation.Cancel();
            };

            async Task StartAsync()
            {
                if (startupHandled)
                {
                    return;
                }

                startupHandled = true;

                try
                {
                    var window = await bootstrapper.CreateMainWindowAsync(startupProgress, startupCancellation.Token);
                    startupCompleted = true;
                    app.MainWindow = window;
                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    window.Show();
                    startupWindow.CloseFromProgram();
                    bootstrapper.ShowStartupWarnings();
                }
                catch (OperationCanceledException) when (startupCancellation.IsCancellationRequested)
                {
                    startupCompleted = true;
                    startupWindow.CloseFromProgram();
                    app.Shutdown();
                }
                catch (Exception ex)
                {
                    startupCompleted = true;
                    CrashLogService.TryWriteCrashLog("Startup", ex);
                    startupWindow.CloseFromProgram();
                    MessageBox.Show(
                        $"Die Anwendung konnte nicht gestartet werden.\n\n{ex.Message}",
                        "Startfehler",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    app.Shutdown();
                }
            }

            startupWindow.ContentRendered += (_, _) => _ = StartAsync();
            app.MainWindow = startupWindow;
            startupWindow.Show();
            app.Run();
        }
        catch (Exception ex)
        {
            CrashLogService.TryWriteCrashLog("Program", ex);
            MessageBox.Show(
                $"Die Anwendung konnte nicht gestartet werden.\n\n{ex.Message}",
                "Startfehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
