// Build: dll\build.cmd first (the exe embeds msvfw32.dll), then
// msbuild A2G-Patch.csproj /restore /t:Rebuild /p:Configuration=Release /p:Platform=x86
using System;
using System.Linq;
using System.Windows.Forms;

namespace A2GPatch
{
    /// <summary>Entry point: collects the folders that may hold the game and shows the window.</summary>
    internal static class Program
    {
        /// <summary>Starts the program; an optional first argument names the game folder (used after restarting as administrator).</summary>
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new PatchForm(GameInstall.CandidateFolders(args.FirstOrDefault(), AppDomain.CurrentDomain.BaseDirectory, GameInstall.ReadRegistry)));
        }
    }
}
