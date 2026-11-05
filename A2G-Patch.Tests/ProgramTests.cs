using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Xunit;

namespace A2GPatch.Tests
{
    /// <summary>Tests for Program: the entry point shows the window on the folder it was given.</summary>
    public class ProgramTests
    {
        [Fact]
        public void Main_ShowsTheWindowOnTheGivenFolder()
        {
            using (GameFolder folder = new GameFolder())
            {
                // Main sets the text rendering default, which fails once a window exists, so it runs in a
                // domain of its own: other tests have created windows in this one.
                AppDomain domain = AppDomain.CreateDomain("Main", null, AppDomain.CurrentDomain.SetupInformation);
                try
                {
                    domain.SetData("folder", folder.Path);
                    domain.DoCallBack(RunMain);
                    Assert.Equal(folder.Path, domain.GetData("shown"));
                }
                finally
                {
                    AppDomain.Unload(domain);
                }
            }
        }

        // In the new domain: runs Main with the folder as argument and closes the window once it's shown,
        // noting the folder it shows.
        private static void RunMain() => StaThread.Run(() =>
        {
            AppDomain domain = AppDomain.CurrentDomain;
            Application.Idle += (s, e) =>
            {
                PatchForm form = Application.OpenForms.OfType<PatchForm>().SingleOrDefault();
                if (form == null || !form.Visible || domain.GetData("shown") != null)
                    return;
                domain.SetData("shown", ((ComboBox)form.Controls.Find("FolderInput", false).Single()).SelectedItem);
                form.Close();
            };
            typeof(Program).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { new[] { (string)domain.GetData("folder") } });
        });
    }
}
