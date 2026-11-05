using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace A2GPatch.Tests
{
    /// <summary>
    /// Runs an action on a dedicated STA thread and rethrows any exception on the calling thread -
    /// for tests that touch WinForms controls, which require an STA thread the test runner's own
    /// thread isn't guaranteed to be.
    /// </summary>
    internal static class StaThread
    {
        /// <summary>Runs <paramref name="action"/> on a new STA thread, waits for it and rethrows its exception, if any.</summary>
        public static void Run(Action action)
        {
            ExceptionDispatchInfo captured = null;
            Thread thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    captured = ExceptionDispatchInfo.Capture(ex);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            captured?.Throw();
        }
    }
}
