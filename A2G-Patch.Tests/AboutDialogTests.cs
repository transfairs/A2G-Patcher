using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Xunit;

namespace A2GPatch.Tests
{
    /// <summary>Tests for AboutDialog and how the main window opens it.</summary>
    [Collection("GameInstall statics")]
    public class AboutDialogTests
    {
        private static readonly MethodInfo OnLinkClicked = typeof(LinkLabel).GetMethod("OnLinkClicked", BindingFlags.Instance | BindingFlags.NonPublic);

        // Clicks every link of every link label in the dialog.
        private static void ClickAll(AboutDialog dialog)
        {
            foreach (LinkLabel label in dialog.Controls.OfType<LinkLabel>())
                foreach (LinkLabel.Link link in label.Links)
                    OnLinkClicked.Invoke(label, new object[] { new LinkLabelLinkClickedEventArgs(link) });
        }

        [Fact]
        public void ShowsNameVersionLogoAndLinks() => StaThread.Run(() =>
        {
            using (AboutDialog dialog = new AboutDialog(Theme.Light))
            {
                Assert.Equal("Über A2G-Patch", dialog.Text);
                Assert.Equal("A2G-Patch", dialog.Controls["TitleLabel"].Text);
                Assert.Equal("v" + AboutDialog.Version, dialog.Controls["VersionLabel"].Text);
                PictureBox logo = (PictureBox)dialog.Controls["Logo"];
                Assert.True(logo.Image.Width >= 64);
                Assert.Equal(2, dialog.Controls.OfType<LinkLabel>().Count());
                Assert.Same(dialog.AcceptButton, dialog.CancelButton);
            }
        });

        [Fact]
        public void Links_OpenTheirTargets()
        {
            List<string> opened = new List<string>();
            Action<string> original = AboutDialog.Open;
            AboutDialog.Open = opened.Add;
            try
            {
                StaThread.Run(() =>
                {
                    using (AboutDialog dialog = new AboutDialog(Theme.Light))
                        ClickAll(dialog);
                });
            }
            finally
            {
                AboutDialog.Open = original;
            }

            Assert.Equal(new[] { AboutDialog.SiteUrl, AboutDialog.GithubUrl }, opened);
        }

        // A link that can't be opened says why instead of crashing the program.
        [Fact]
        public void Link_ThatFails_ShowsTheError()
        {
            int tries = 0;
            Action<string> original = AboutDialog.Open;
            AboutDialog.Open = target => { tries++; throw new InvalidOperationException("kein Browser"); };
            try
            {
                StaThread.Run(() =>
                {
                    using (AboutDialog dialog = new AboutDialog(Theme.Light))
                    {
                        LinkLabel label = dialog.Controls.OfType<LinkLabel>().First();
                        // A message box with only OK closes on Escape (Cancel).
                        Dialogs.Answer(Dialogs.Cancel);
                        OnLinkClicked.Invoke(label, new object[] { new LinkLabelLinkClickedEventArgs(label.Links[0]) });
                    }
                });
            }
            finally
            {
                AboutDialog.Open = original;
            }

            Assert.Equal(1, tries);
        }

        // "Über" in the banner opens the dialog over the window; it is closed again as soon as it shows.
        [Fact]
        public void AboutLink_OpensTheDialog() => StaThread.Run(() =>
        {
            int shown = 0;
            EventHandler close = (s, e) =>
            {
                foreach (AboutDialog dialog in Application.OpenForms.OfType<AboutDialog>().ToList())
                {
                    shown++;
                    dialog.Close();
                }
            };
            Application.Idle += close;
            try
            {
                using (PatchForm form = new PatchForm(new List<string>()))
                {
                    form.Show();
                    LinkLabel link = (LinkLabel)form.Controls["AboutLink"];
                    Assert.Equal("Über", link.Text);
                    OnLinkClicked.Invoke(link, new object[] { new LinkLabelLinkClickedEventArgs(link.Links[0]) });
                }
            }
            finally
            {
                Application.Idle -= close;
            }

            Assert.Equal(1, shown);
        });
    }
}
