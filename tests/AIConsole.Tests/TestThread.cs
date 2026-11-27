using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace AIConsole.Tests;

/// <summary>Runs WPF control tests on a dedicated STA thread.</summary>
internal static class TestThread
{
    public static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    /// <summary>Runs an async test body on an STA thread with a pumped dispatcher.</summary>
    public static void RunStaAsync(Func<Task> action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

            Task task;
            try { task = action(); }
            catch (Exception ex)
            {
                error = ex;
                return;
            }

            task.ContinueWith(t =>
            {
                if (t.IsFaulted) error = t.Exception?.GetBaseException();
                else if (t.IsCanceled) error = new TaskCanceledException(t);
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.FromCurrentSynchronizationContext());

            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
