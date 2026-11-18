using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using Xunit;

namespace A2GPatch.Tests
{
    /// <summary>
    /// Tests for Theme: light, dark or as Windows says, the windows following a switch, the choice
    /// remembered. The choice goes to a key of the tests' own, deleted after each test.
    /// </summary>
    [Collection("GameInstall statics")]
    public sealed class ThemeTests : IDisposable
    {
        private const int WmSettingChange = 0x1A;
        private const string TestKey = @"Software\A2G-Patch-Tests";
        private readonly string originalKey = Theme.SettingsKey;

        public ThemeTests()
        {
            Theme.SettingsKey = @"HKEY_CURRENT_USER\" + TestKey;
            Registry.CurrentUser.DeleteSubKeyTree(TestKey, false);
        }

        public void Dispose()
        {
            Registry.CurrentUser.DeleteSubKeyTree(TestKey, false);
            Theme.SettingsKey = this.originalKey;
        }

        private static T Find<T>(Form form, string name) where T : Control => (T)form.Controls.Find(name, true).Single();

        private static void Draw(Control control)
        {
            using (Bitmap image = new Bitmap(control.Width, control.Height))
                control.DrawToBitmap(image, new Rectangle(Point.Empty, control.Size));
        }

        // Hands the window a WM_SETTINGCHANGE naming what changed (null: no name).
        private static void SettingChanged(Form form, string what)
        {
            IntPtr text = what == null ? IntPtr.Zero : Marshal.StringToHGlobalUni(what);
            try
            {
                object[] args = { Message.Create(form.Handle, WmSettingChange, IntPtr.Zero, text) };
                typeof(PatchForm).GetMethod("WndProc", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, args);
            }
            finally
            {
                Marshal.FreeHGlobal(text);
            }
        }

        // Runs action with Windows answering "dark?" by asking prefersDark.
        private static void WithPreference(Func<bool> prefersDark, Action action)
        {
            Func<bool> original = Theme.PrefersDark;
            Theme.PrefersDark = prefersDark;
            try
            {
                StaThread.Run(action);
            }
            finally
            {
                Theme.PrefersDark = original;
            }
        }

        [Fact]
        public void OnlyAZeroForLightAppsMeansDark()
        {
            Assert.True(Theme.MeansDark(0));
            Assert.False(Theme.MeansDark(1));
            Assert.False(Theme.MeansDark(null));
            Assert.False(Theme.MeansDark("0"));
        }

        // The real registry, whatever it says on this machine.
        [Fact]
        public void ReadsWindowsAppMode()
        {
            bool dark = Theme.PrefersDark();
            Assert.Same(dark ? Theme.Dark : Theme.Light, Theme.For(ThemeChoice.System));
        }

        [Fact]
        public void AChoiceOverridesWindows()
        {
            Func<bool> original = Theme.PrefersDark;
            try
            {
                foreach (bool dark in new[] { false, true })
                {
                    Theme.PrefersDark = () => dark;
                    Assert.Same(Theme.Light, Theme.For(ThemeChoice.Light));
                    Assert.Same(Theme.Dark, Theme.For(ThemeChoice.Dark));
                    Assert.Same(dark ? Theme.Dark : Theme.Light, Theme.For(ThemeChoice.System));
                }
            }
            finally
            {
                Theme.PrefersDark = original;
            }
        }

        [Fact]
        public void RemembersTheChoice()
        {
            Assert.Equal(ThemeChoice.System, Theme.LoadChoice());
            foreach (ThemeChoice choice in new[] { ThemeChoice.Dark, ThemeChoice.Light, ThemeChoice.System })
            {
                Theme.SaveChoice(choice);
                Assert.Equal(choice, Theme.LoadChoice());
            }
            Assert.Equal(ThemeChoice.Dark, Theme.ParseChoice("Dark"));
            Assert.Equal(ThemeChoice.System, Theme.ParseChoice("dark"));
            Assert.Equal(ThemeChoice.System, Theme.ParseChoice(2));
            Assert.Equal(ThemeChoice.System, Theme.ParseChoice(null));
        }

        // The menu's colours come from the theme: its surface, the blue group colour for the check.
        [Fact]
        public void MenuColorsFollowTheTheme()
        {
            Theme.MenuColors colors = new Theme.MenuColors(Theme.Dark);
            Assert.Equal(Theme.Dark.Surface, colors.ToolStripDropDownBackground);
            Assert.Equal(Theme.Dark.Surface, colors.ImageMarginGradientBegin);
            Assert.Equal(Theme.Dark.Surface, colors.ImageMarginGradientMiddle);
            Assert.Equal(Theme.Dark.Surface, colors.ImageMarginGradientEnd);
            Assert.Equal(Theme.Dark.Border, colors.MenuBorder);
            Assert.Equal(colors.MenuItemSelected, colors.MenuItemBorder);
            Assert.NotEqual(Theme.Dark.Surface, colors.MenuItemSelected);
            Assert.Equal(Theme.Dark.Groups[0], colors.CheckBackground);
            Assert.Equal(Theme.Dark.Groups[0], colors.CheckSelectedBackground);
            Assert.Equal(Theme.Dark.Groups[0], colors.CheckPressedBackground);
        }

        // "Darstellung" opens the menu; a pick switches at once, is checked and remembered, and
        // overrides Windows until "Wie Windows" is picked again.
        [Fact]
        public void Window_SwitchesByTheMenu() => WithPreference(() => true, () =>
        {
            using (PatchForm form = new PatchForm(new List<string>()))
            {
                Assert.Same(Theme.Dark, form.Theme);
                form.Show();
                LinkLabel link = Find<LinkLabel>(form, "ThemeLink");
                Assert.Equal("Darstellung", link.Text);
                Assert.True(link.Right < Find<LinkLabel>(form, "AboutLink").Left);
                typeof(LinkLabel).GetMethod("OnLinkClicked", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(link, new object[] { new LinkLabelLinkClickedEventArgs(link.Links[0]) });
                Assert.True(form.ThemeMenu.Visible);
                Assert.Equal(PatchForm.ThemeTitles, form.ThemeMenu.Items.Cast<ToolStripItem>().Select(i => i.Text));
                Assert.True(((ToolStripMenuItem)form.ThemeMenu.Items["SystemTheme"]).Checked);
                Draw(form.ThemeMenu);

                form.ThemeMenu.Items["LightTheme"].PerformClick();
                Assert.Same(Theme.Light, form.Theme);
                Assert.Equal(ThemeChoice.Light, Theme.LoadChoice());
                Assert.True(((ToolStripMenuItem)form.ThemeMenu.Items["LightTheme"]).Checked);
                Assert.False(((ToolStripMenuItem)form.ThemeMenu.Items["SystemTheme"]).Checked);
                SettingChanged(form, "ImmersiveColorSet");
                Assert.Same(Theme.Light, form.Theme);

                form.ThemeMenu.Items["SystemTheme"].PerformClick();
                Assert.Same(Theme.Dark, form.Theme);
                Assert.Equal(ThemeChoice.System, Theme.LoadChoice());
                form.ThemeMenu.Close();
            }

            // The next start takes the remembered choice.
            Theme.SaveChoice(ThemeChoice.Light);
            using (PatchForm form = new PatchForm(new List<string>()))
                Assert.Same(Theme.Light, form.Theme);
        });

        [Fact]
        public void BothThemesHaveEveryColour()
        {
            foreach (Theme theme in new[] { Theme.Light, Theme.Dark })
            {
                Assert.Equal(Enum.GetValues(typeof(PatchGroup)).Length, theme.Groups.Length);
                Assert.NotEqual(theme.Page, theme.Text);
                Assert.NotEqual(theme.Surface, theme.Title);
            }
            Assert.False(Theme.Light.IsDark);
            Assert.True(Theme.Dark.IsDark);
        }

        // Opens dark, follows Windows to light and back; other setting changes leave it as it is.
        [Fact]
        public void Window_FollowsWindowsAppMode()
        {
            bool dark = true;
            WithPreference(() => dark, () =>
            {
                using (PatchForm form = new PatchForm(new List<string>()))
                {
                    Button browse = Find<Button>(form, "BrowseButton");
                    ComboBox folder = Find<ComboBox>(form, "FolderInput");
                    PatchTile tile = Find<PatchTile>(form, "GoalLimitsInput");
                    Assert.Same(Theme.Dark, form.Theme);
                    Assert.Equal(Theme.Dark.Page, form.BackColor);
                    Assert.Equal(FlatStyle.Flat, browse.FlatStyle);
                    Assert.Equal(Theme.Dark.Button, folder.BackColor);
                    Assert.Same(Theme.Dark, tile.Theme);
                    Assert.Equal(Theme.Dark.Groups[(int)PatchGroup.Balancing], tile.Accent);
                    Assert.Equal(Theme.Dark.ApplyDisabled, Find<Button>(form, "ApplyButton").BackColor);
                    form.Show();
                    Draw(form);

                    dark = false;
                    SettingChanged(form, "WindowMetrics");
                    SettingChanged(form, null);
                    Assert.Same(Theme.Dark, form.Theme);
                    SettingChanged(form, "ImmersiveColorSet");
                    Assert.Same(Theme.Light, form.Theme);
                    Assert.Equal(Theme.Light.Page, form.BackColor);
                    Assert.Equal(FlatStyle.Standard, browse.FlatStyle);
                    Assert.True(browse.UseVisualStyleBackColor);
                    Assert.Equal(SystemColors.Window, folder.BackColor);
                    Assert.Equal(Theme.Light.Groups[(int)PatchGroup.Balancing], tile.Accent);
                    Draw(form);

                    dark = true;
                    SettingChanged(form, "ImmersiveColorSet");
                    Assert.Same(Theme.Dark, form.Theme);
                }
            });
        }

        // Light from the start: Windows' own buttons, nothing done to the title bar.
        [Fact]
        public void Window_OpensLight() => WithPreference(() => false, () =>
        {
            using (PatchForm form = new PatchForm(new List<string>()))
            {
                Assert.Same(Theme.Light, form.Theme);
                Assert.Equal(FlatStyle.Standard, Find<Button>(form, "CloseButton").FlatStyle);
                form.Show();
                Draw(form);
            }
        });

        // The about dialog in either theme, links in the theme's colour.
        [Fact]
        public void AboutDialog_TakesTheTheme() => StaThread.Run(() =>
        {
            foreach (Theme theme in new[] { Theme.Light, Theme.Dark })
                using (AboutDialog dialog = new AboutDialog(theme))
                {
                    Assert.Equal(theme.Page, dialog.BackColor);
                    Assert.Equal(theme.GrayText, dialog.Controls["VersionLabel"].ForeColor);
                    Assert.Equal(theme.IsDark ? FlatStyle.Flat : FlatStyle.Standard, ((Button)dialog.Controls["OkButton"]).FlatStyle);
                    if (theme.IsDark)
                        Assert.All(dialog.Controls.OfType<LinkLabel>(), link => Assert.Equal(theme.Link, link.LinkColor));
                    dialog.Show();
                    Draw(dialog);
                }
        });

        // Ring, arc and tick in the dark theme's colours.
        [Fact]
        public void BusyIndicator_DrawsDark() => StaThread.Run(() =>
        {
            using (BusyIndicator progress = new BusyIndicator { Size = new Size(20, 20), Theme = Theme.Dark })
            {
                progress.State = BusyState.Busy;
                Draw(progress);
                progress.State = BusyState.Done;
                Draw(progress);
                Assert.Same(Theme.Dark, progress.Theme);
            }
        });
    }
}
