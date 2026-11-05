using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace A2GPatch.Tests
{
    /// <summary>Answers a modal dialog (message box, folder browser) the calling thread is about to show, as a user would.</summary>
    internal static class Dialogs
    {
        internal const int Ok = 1;
        internal const int Cancel = 2;
        internal const int Yes = 6;

        private const uint WmCommand = 0x0111;

        private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint threadId, EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder name, int size);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// Presses <paramref name="button"/> (Ok, Cancel, Yes) in the next dialog the calling thread
        /// shows; call it right before showing the dialog. Gives up after 10 s.
        /// </summary>
        internal static void Answer(int button)
        {
            uint thread = GetCurrentThreadId();
            new Thread(() =>
            {
                for (int i = 0; i < 200; i++)
                {
                    IntPtr dialog = Find(thread);
                    if (dialog != IntPtr.Zero)
                    {
                        // Let the dialog finish setting up (the folder browser selects its start folder).
                        Thread.Sleep(300);
                        PostMessage(dialog, WmCommand, (IntPtr)button, IntPtr.Zero);
                        return;
                    }
                    Thread.Sleep(50);
                }
            }) { IsBackground = true }.Start();
        }

        // The thread's visible dialog window (class #32770); zero without one.
        private static IntPtr Find(uint thread)
        {
            IntPtr found = IntPtr.Zero;
            EnumThreadWindows(thread, (window, parameter) =>
            {
                StringBuilder name = new StringBuilder(16);
                GetClassName(window, name, name.Capacity);
                if (IsWindowVisible(window) && name.ToString() == "#32770")
                {
                    found = window;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }
    }
}
