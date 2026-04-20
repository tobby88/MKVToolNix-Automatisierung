using System.Threading;
using System.Windows.Threading;

namespace MkvToolnixAutomatisierung.Tests.TestInfrastructure;

/// <summary>
/// Führt gezielte WPF-Interaktionstests auf einem dedizierten STA-Dispatcherthread aus.
/// </summary>
internal static class WpfTestHost
{
    /// <summary>
    /// Führt den angegebenen Testkörper auf einem STA-Thread mit aktivem WPF-Dispatcher aus.
    /// </summary>
    public static async Task RunAsync(Func<Task> testBody)
    {
        ArgumentNullException.ThrowIfNull(testBody);

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

            _ = Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await testBody();
                    completion.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
                finally
                {
                    Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });

            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WpfTestHost"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        await completion.Task;
        thread.Join();
    }

    /// <summary>
    /// Wartet, bis der aktuelle Dispatcher seine ausstehenden UI-Arbeiten bis ApplicationIdle abgearbeitet hat.
    /// </summary>
    public static Task WaitForIdleAsync()
    {
        return Dispatcher.CurrentDispatcher
            .InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle)
            .Task;
    }
}
