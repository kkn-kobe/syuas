using System.Runtime.ExceptionServices;

namespace Syuas.Tests;

internal static class Sta
{
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
