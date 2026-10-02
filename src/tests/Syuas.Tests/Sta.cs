using System.Runtime.ExceptionServices;

namespace Syuas.Tests;

internal static class Sta
{
    public static void RunAsync(Func<Task> action) => Run(() =>
    {
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        Exception? failure = null;
        _ = dispatcher.BeginInvoke(new Action(async () =>
        {
            try { await action(); }
            catch (Exception e) { failure = e; }
            finally { dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background); }
        }));
        System.Windows.Threading.Dispatcher.Run();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    });

    public static void Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "STA test timed out.");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
