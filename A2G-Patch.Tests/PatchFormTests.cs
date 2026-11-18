using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace A2GPatch.Tests
{
    /// <summary>Tests for PatchForm: what it shows for a folder and how it applies the switches.</summary>
    [Collection("GameInstall statics")]
    public class PatchFormTests
    {
        private static T Find<T>(Form form, string name) where T : Control => (T)form.Controls.Find(name, true).Single();

        private static PatchTile GoalsInput(Form form) => Find<PatchTile>(form, "GoalLimitsInput");
        private static PatchTile BugsInput(Form form) => Find<PatchTile>(form, "GameBugsInput");
        private static Button ApplyButton(Form form) => Find<Button>(form, "ApplyButton");

        private static void Click(Form form, string name) =>
            typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Find<Button>(form, name), new object[] { EventArgs.Empty });

        // A window on folders, recording its messages and answering them with answer; applying takes no extra time.
        private static PatchForm Form(List<string> messages, DialogResult answer, params string[] folders)
        {
            PatchForm form = new PatchForm(folders.ToList());
            form.ShowMessage = (text, buttons) => { messages.Add(text); return answer; };
            form.MinimumBusyTime = 0;
            return form;
        }

        // Clicks "Übernehmen" and lets the window work until the switches are applied.
        private static void Apply(PatchForm form)
        {
            Click(form, "ApplyButton");
            WaitWhileBusy(form);
        }

        // Handles the window's messages until it is done applying.
        private static void WaitWhileBusy(PatchForm form)
        {
            for (DateTime end = DateTime.Now.AddSeconds(30); form.Busy && DateTime.Now < end; Thread.Sleep(5))
                Application.DoEvents();
            Assert.False(form.Busy);
        }

        // The arc turns while busy (the timer runs only then), whatever the load on the test machine.
        [Fact]
        public void BusyIndicator_TurnsWhileBusy() => StaThread.Run(() =>
        {
            using (Form form = new Form())
            using (BusyIndicator progress = new BusyIndicator { Size = new Size(20, 20) })
            {
                form.Controls.Add(progress);
                form.Show();
                progress.State = BusyState.Busy;
                for (DateTime end = DateTime.Now.AddSeconds(30); progress.Angle == 0 && DateTime.Now < end; Thread.Sleep(5))
                    Application.DoEvents();
                Assert.NotEqual(0, progress.Angle);

                progress.State = BusyState.Idle;
                int angle = progress.Angle;
                for (int i = 0; i < 40; i++, Thread.Sleep(5))
                    Application.DoEvents();
                Assert.Equal(angle, progress.Angle);
                Assert.False(progress.Visible);
            }
        });

        // Handles the window's messages until the tile's knob and tint have arrived.
        private static void WaitWhileMoving(PatchTile tile)
        {
            for (DateTime end = DateTime.Now.AddSeconds(30); tile.Moving && DateTime.Now < end; Thread.Sleep(5))
                Application.DoEvents();
            Assert.False(tile.Moving);
        }

        private static void Draw(Control control)
        {
            using (Bitmap image = new Bitmap(control.Width, control.Height))
                control.DrawToBitmap(image, new Rectangle(Point.Empty, control.Size));
        }

        private static void Raise(Control control, string method, EventArgs e) =>
            control.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(control, new object[] { e });

        // Click and space bar toggle a tile, the knob slides and the tint fades in steps of the timer.
        [Fact]
        public void PatchTile_TogglesAndAnimates() => StaThread.Run(() =>
        {
            using (Form form = new Form())
            using (PatchTile tile = new PatchTile { Title = "Mehr Tore", Text = "Torgrenzen aufheben", Size = new Size(280, 70) })
            {
                form.Controls.Add(tile);
                form.Show();
                // Nobody listening yet.
                tile.Checked = true;
                tile.Checked = false;
                WaitWhileMoving(tile);
                int changes = 0;
                tile.CheckedChanged += (s, e) => changes++;

                Raise(tile, "OnClick", EventArgs.Empty);
                Assert.True(tile.Checked);
                Assert.True(tile.Focused);
                Assert.True(tile.Moving);
                tile.Animate();
                Assert.Equal(0.125f, tile.Knob);
                WaitWhileMoving(tile);
                Assert.Equal(1f, tile.Knob);

                Raise(tile, "OnKeyUp", new KeyEventArgs(Keys.Space));
                Assert.False(tile.Checked);
                Raise(tile, "OnKeyUp", new KeyEventArgs(Keys.Enter));
                Assert.False(tile.Checked);
                tile.Checked = false;
                Assert.Equal(2, changes);
                WaitWhileMoving(tile);
                Assert.Equal(0f, tile.Knob);

                // The tint follows the mouse, not on a disabled tile.
                Raise(tile, "OnMouseEnter", EventArgs.Empty);
                WaitWhileMoving(tile);
                Assert.Equal(1f, tile.Hover);
                tile.Enabled = false;
                WaitWhileMoving(tile);
                Assert.Equal(0f, tile.Hover);
                tile.Enabled = true;
                Raise(tile, "OnMouseLeave", EventArgs.Empty);
                WaitWhileMoving(tile);
                Assert.Equal(0f, tile.Hover);

                // Screen readers see a check box.
                AccessibleObject accessible = tile.AccessibilityObject;
                Assert.Equal(AccessibleRole.CheckButton, accessible.Role);
                Assert.Equal(AccessibleStates.None, accessible.State & AccessibleStates.Checked);
                tile.Checked = true;
                Assert.Equal(AccessibleStates.Checked, accessible.State & AccessibleStates.Checked);

                // Every look: focused or not, pending or not, disabled.
                tile.Focus();
                Assert.True(tile.Focused);
                Draw(tile);
                tile.Pending = true;
                tile.Enabled = false;
                Assert.False(tile.Focused);
                Draw(tile);
            }
        });

        [Fact]
        public void PatchTile_BlendsColors()
        {
            Assert.Equal(Color.FromArgb(0, 0, 0), PatchTile.Blend(Color.Black, Color.White, 0));
            Assert.Equal(Color.FromArgb(127, 127, 127), PatchTile.Blend(Color.Black, Color.White, 0.5f));
            Assert.Equal(Color.FromArgb(255, 255, 255), PatchTile.Blend(Color.Black, Color.White, 1));
        }

        // The tiles in their groups, three to a row, and the window drawn with banner and lines.
        [Fact]
        public void Tiles_StandInTheirGroups() => StaThread.Run(() =>
        {
            using (PatchForm form = Form(new List<string>(), DialogResult.OK))
            {
                Label[] headings = Enum.GetValues(typeof(PatchGroup)).Cast<PatchGroup>().Select(g => Find<Label>(form, g + "Label")).ToArray();
                Assert.Equal(PatchForm.GroupTitles, headings.Select(h => h.Text));
                foreach (Patch patch in Patch.All)
                {
                    PatchTile tile = Find<PatchTile>(form, patch.GetType().Name + "Input");
                    Assert.Equal(patch.Title, tile.Title);
                    Assert.Equal(patch.Name, tile.Text);
                    Assert.Equal(form.Theme.Groups[(int)patch.Group], tile.Accent);
                    // Below its own heading, above the next one.
                    Assert.True(tile.Top > headings[(int)patch.Group].Bottom);
                    Assert.True((int)patch.Group == headings.Length - 1 || tile.Bottom < headings[(int)patch.Group + 1].Top);
                }
                // Five tiles in two rows.
                Assert.Equal(2, Patch.All.Where(p => p.Group == PatchGroup.Patches).Select(p => Find<PatchTile>(form, p.GetType().Name + "Input").Top).Distinct().Count());
                form.Show();
                Draw(form);
            }
        });

        // Narrow, the tiles stand in one column and scroll; wide, three to a row. They never stick out
        // of the panel, and rearranging keeps the scroll position.
        [Fact]
        public void Tiles_FollowTheWindowSize() => StaThread.Run(() =>
        {
            using (PatchForm form = Form(new List<string>(), DialogResult.OK))
            {
                form.Show();
                Panel panel = Find<Panel>(form, "TilePanel");
                PatchTile[] game = Patch.All.Where(p => p.Group == PatchGroup.Patches).Select(p => Find<PatchTile>(form, p.GetType().Name + "Input")).ToArray();
                PatchTile[] all = Patch.All.Select(p => Find<PatchTile>(form, p.GetType().Name + "Input")).ToArray();

                form.Size = form.MinimumSize;
                Application.DoEvents();
                Assert.Equal(4, game.Select(t => t.Top).Distinct().Count());
                Assert.True(panel.VerticalScroll.Visible);
                Assert.False(panel.HorizontalScroll.Visible);
                Assert.All(all, t => Assert.True(t.Right <= panel.ClientSize.Width));
                // Each tile as tall as its text needs, at least the usual height.
                Assert.All(all, t => Assert.True(t.Height >= t.GetPreferredSize(new Size(t.Width, 0)).Height));
                Draw(form);

                panel.AutoScrollPosition = new Point(0, 10000);
                int scrolled = panel.AutoScrollPosition.Y, top = game[0].Top;
                Assert.True(scrolled < 0);
                form.Height++;
                Application.DoEvents();
                Assert.Equal(top - scrolled, game[0].Top - panel.AutoScrollPosition.Y);

                form.Size = SystemInformation.MaxWindowTrackSize;
                Application.DoEvents();
                Assert.Equal(2, game.Select(t => t.Top).Distinct().Count());
                Assert.All(all, t => Assert.True(t.Right <= panel.ClientSize.Width));
                Draw(form);
            }
        });

        // Runs body with IsGameRunning answering running.
        private static void WithGameRunning(bool running, Action body)
        {
            Func<string, bool> previous = GameInstall.IsGameRunning;
            GameInstall.IsGameRunning = folder => running;
            try
            {
                body();
            }
            finally
            {
                GameInstall.IsGameRunning = previous;
            }
        }

        // An exe with both patches' code whose exe can't be written (read-only, backup already there).
        private static void ReadOnlyExe(GameFolder folder)
        {
            File.WriteAllBytes(folder.File("anstoss2.exe"), GameCode.Both());
            File.WriteAllBytes(folder.File("anstoss2.exe" + GameInstall.BackupSuffix), new byte[] { 1 });
            File.SetAttributes(folder.File("anstoss2.exe"), FileAttributes.ReadOnly);
        }

        [Fact]
        public void Switches_ShowTheFolder_AndApplyWhatChanged() => StaThread.Run(() => WithGameRunning(false, () =>
        {
            using (GameFolder folder = new GameFolder())
            {
                File.WriteAllBytes(folder.File("anstoss2.exe"), GameCode.Both());
                List<string> messages = new List<string>();
                using (PatchForm form = Form(messages, DialogResult.OK, folder.Path))
                {
                    Assert.Equal("anstoss2.exe wird direkt gepatcht.", Find<Label>(form, "StatusLabel").Text);
                    Assert.Equal("Torgrenze auf 15 Tore pro Mannschaft erhöhen", GoalsInput(form).Text);
                    Assert.True(GoalsInput(form).Enabled && BugsInput(form).Enabled);
                    Assert.False(GoalsInput(form).Checked || BugsInput(form).Checked);
                    Assert.False(ApplyButton(form).Enabled);
                    // The GOG patches under their heading, greyed out for another release.
                    Label gog = Find<Label>(form, "GogLabel");
                    Assert.Equal("Nur für die GOG-Version", gog.Text);
                    Assert.All(new[] { "CopyProtectionInput", "DialogStylesInput", "AnimationEdgeInput" }, name =>
                    {
                        PatchTile tile = Find<PatchTile>(form, name);
                        Assert.False(tile.Enabled);
                        Assert.True(tile.Top > gog.Top);
                    });
                    // Patch 1.01 is for the 2007 CD as well: above the heading, greyed out here too.
                    Assert.False(Find<PatchTile>(form, "Patch101Input").Enabled);
                    Assert.True(Find<PatchTile>(form, "Patch101Input").Top < gog.Top);

                    // A change gets its dot, the button counts the changes.
                    BugsInput(form).Checked = true;
                    Assert.True(ApplyButton(form).Enabled);
                    Assert.True(BugsInput(form).Pending);
                    Assert.False(GoalsInput(form).Pending);
                    Assert.Equal("Übernehmen (1)", ApplyButton(form).Text);
                    GoalsInput(form).Checked = true;
                    Assert.Equal("Übernehmen (2)", ApplyButton(form).Text);
                    GoalsInput(form).Checked = false;
                    BugsInput(form).Checked = false;
                    Assert.False(ApplyButton(form).Enabled);
                    Assert.False(BugsInput(form).Pending);
                    Assert.Equal("Übernehmen", ApplyButton(form).Text);

                    BugsInput(form).Checked = true;
                    Apply(form);
                    Assert.False(BugsInput(form).Pending);
                    Assert.True(GameInstall.Detect(folder.Path).IsApplied(GameInstallTests.Bugs));
                    Assert.False(GameInstall.Detect(folder.Path).IsApplied(GameInstallTests.Goals));
                    Assert.True(BugsInput(form).Checked);
                    Assert.False(ApplyButton(form).Enabled);

                    BugsInput(form).Checked = false;
                    Apply(form);
                    Assert.Equal(GameCode.Both(), File.ReadAllBytes(folder.File("anstoss2.exe")));
                    Assert.Empty(messages);
                }
            }
        }));

        // While applying, a turning circle and a locked window that can't be closed; then a tick until the next change.
        [Fact]
        public void Apply_ShowsItIsBusy_ThenConfirms() => StaThread.Run(() => WithGameRunning(false, () =>
        {
            using (GameFolder folder = new GameFolder())
            {
                File.WriteAllBytes(folder.File("anstoss2.exe"), GameCode.Both());
                using (PatchForm form = Form(new List<string>(), DialogResult.OK, folder.Path))
                {
                    BusyIndicator progress = Find<BusyIndicator>(form, "Progress");
                    Label progressLabel = Find<Label>(form, "ProgressLabel");
                    form.Show();
                    Assert.False(progress.Visible);
                    Assert.Equal("", progressLabel.Text);

                    // Busy until the window handles its messages again; the minimum time is covered there too.
                    form.MinimumBusyTime = 50;
                    BugsInput(form).Checked = true;
                    Click(form, "ApplyButton");
                    Assert.True(form.Busy);
                    Assert.Equal(BusyState.Busy, progress.State);
                    Assert.True(progress.Visible);
                    Assert.Equal("Änderungen werden übernommen …", progressLabel.Text);
                    Assert.All(new[] { "FolderInput", "BrowseButton", "ApplyButton", "CloseButton", "GameBugsInput", "GoalLimitsInput" },
                        name => Assert.False(form.Controls.Find(name, true).Single().Enabled));
                    using (Bitmap image = new Bitmap(progress.Width, progress.Height))
                        progress.DrawToBitmap(image, new Rectangle(Point.Empty, progress.Size));
                    form.Close();
                    Assert.False(form.IsDisposed);

                    WaitWhileBusy(form);
                    Assert.True(GameInstall.Detect(folder.Path).IsApplied(GameInstallTests.Bugs));
                    Assert.Equal(BusyState.Done, progress.State);
                    Assert.Equal("Änderungen übernommen.", progressLabel.Text);
                    Assert.True(Find<Button>(form, "CloseButton").Enabled && GoalsInput(form).Enabled);
                    Assert.False(ApplyButton(form).Enabled);
                    using (Bitmap image = new Bitmap(progress.Width, progress.Height))
                        progress.DrawToBitmap(image, new Rectangle(Point.Empty, progress.Size));

                    // The next change hides the confirmation.
                    GoalsInput(form).Checked = true;
                    Assert.False(progress.Visible);
                    Assert.Equal("", progressLabel.Text);
                }
            }
        }));

        [Fact]
        public void Apply_WhileTheGameRuns_AsksToEndIt() => StaThread.Run(() => WithGameRunning(true, () =>
        {
            using (GameFolder folder = new GameFolder())
            {
                File.WriteAllBytes(folder.File("anstoss2.exe"), GameCode.Both());
                List<string> messages = new List<string>();
                using (PatchForm form = Form(messages, DialogResult.OK, folder.Path))
                {
                    GoalsInput(form).Checked = true;
                    Apply(form);

                    Assert.Equal(new[] { "Bitte zuerst das Spiel beenden." }, messages);
                    Assert.False(GameInstall.Detect(folder.Path).IsApplied(GameInstallTests.Goals));
                    // The choice stays, to apply once the game is closed.
                    Assert.True(GoalsInput(form).Checked);
                }
            }
        }));

        // A read-only exe stands for missing rights (Program Files): the program offers to restart as administrator.
        [Theory]
        [InlineData(DialogResult.Yes)]
        [InlineData(DialogResult.No)]
        public void Apply_WithoutWriteAccess_OffersToRestartAsAdministrator(DialogResult answer) => StaThread.Run(() => WithGameRunning(false, () =>
        {
            using (GameFolder folder = new GameFolder())
            {
                ReadOnlyExe(folder);
                List<string> messages = new List<string>();
                List<string> restarted = new List<string>();
                using (PatchForm form = Form(messages, answer, folder.Path))
                {
                    form.RestartElevated = restarted.Add;
                    form.Show();
                    GoalsInput(form).Checked = true;
                    Apply(form);

                    Assert.Single(messages);
                    Assert.StartsWith("Für diesen Ordner fehlen die Schreibrechte.", messages[0]);
                    Assert.Equal(answer == DialogResult.Yes ? new[] { folder.Path } : new string[0], restarted);
                    Assert.Equal(answer == DialogResult.Yes, form.IsDisposed);
                }
            }
        }));

        [Fact]
        public void Apply_WhenTheUacPromptIsDeclined_StaysOpen() => StaThread.Run(() => WithGameRunning(false, () =>
        {
            using (GameFolder folder = new GameFolder())
            {
                ReadOnlyExe(folder);
                using (PatchForm form = Form(new List<string>(), DialogResult.Yes, folder.Path))
                {
                    form.RestartElevated = f => throw new Win32Exception(1223);
                    form.Show();
                    GoalsInput(form).Checked = true;
                    Apply(form);

                    Assert.False(form.IsDisposed);
                    Assert.False(GoalsInput(form).Checked);
                }
            }
        }));

        [Fact]
        public void Apply_WithTheExeInUse_ShowsWhy() => StaThread.Run(() => WithGameRunning(false, () =>
        {
            using (GameFolder folder = new GameFolder())
            {
                File.WriteAllBytes(folder.File("anstoss2.exe"), GameCode.Both());
                List<string> messages = new List<string>();
                using (PatchForm form = Form(messages, DialogResult.OK, folder.Path))
                {
                    using (new FileStream(folder.File("anstoss2.exe"), FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        GoalsInput(form).Checked = true;
                        Apply(form);

                        // Not even readable now: the window says so instead of a state.
                        Assert.Single(messages);
                        Assert.Null(form.Install);
                        Assert.Contains("anstoss2.exe", Find<Label>(form, "StatusLabel").Text);
                        Assert.False(GoalsInput(form).Enabled);
                        Assert.False(ApplyButton(form).Enabled);
                    }
                    Assert.Equal(GameCode.Both(), File.ReadAllBytes(folder.File("anstoss2.exe")));
                }
            }
        }));

        [Fact]
        public void StatusText_DescribesEveryCase()
        {
            using (GameFolder packed = new GameFolder())
            using (GameFolder foreign = new GameFolder())
            using (GameFolder empty = new GameFolder())
            {
                File.WriteAllBytes(packed.File("run.exe"), new byte[] { 0x4D, 0x5A });
                File.WriteAllBytes(foreign.File("run.exe"), new byte[] { 0x4D, 0x5A });
                File.WriteAllBytes(foreign.File(GameInstall.DllName), new byte[] { 0x4D, 0x5A });

                Assert.Equal("Kein Spielordner gefunden.", PatchForm.StatusText(null));
                Assert.Equal("Kein bekanntes Anstoss 2 Gold in diesem Ordner.", PatchForm.StatusText(GameInstall.Detect(empty.Path)));
                Assert.Equal("run.exe ist gepackt: Die Patches wirken über die msvfw32.dll ab dem Spielstart.", PatchForm.StatusText(GameInstall.Detect(packed.Path)));
                Assert.Equal("run.exe ist gepackt, aber die msvfw32.dll im Ordner gehört nicht zu A2G-Patch.", PatchForm.StatusText(GameInstall.Detect(foreign.Path)));
            }
        }

        [Fact]
        public void WithoutAFolder_NothingCanBeSwitched() => StaThread.Run(() =>
        {
            using (PatchForm form = Form(new List<string>(), DialogResult.OK))
            {
                Assert.Null(form.Install);
                Assert.Equal("Kein Spielordner gefunden.", Find<Label>(form, "StatusLabel").Text);
                Assert.False(GoalsInput(form).Enabled || BugsInput(form).Enabled);
                Assert.False(ApplyButton(form).Enabled);
            }
        });

        [Fact]
        public void Browse_AddsAndChoosesTheFolder_OnceOnly() => StaThread.Run(() =>
        {
            using (GameFolder first = new GameFolder())
            using (GameFolder second = new GameFolder())
            {
                File.WriteAllBytes(second.File("run.exe"), GameCode.Exe(GameCode.Arrow()));
                using (PatchForm form = Form(new List<string>(), DialogResult.OK, first.Path))
                {
                    ComboBox folders = Find<ComboBox>(form, "FolderInput");
                    Assert.False(BugsInput(form).Enabled);
                    string start = null;
                    form.BrowseFolder = s => { start = s; return second.Path + "\\"; };

                    Click(form, "BrowseButton");
                    Assert.Equal(first.Path, start);
                    Click(form, "BrowseButton");

                    Assert.Equal(new[] { first.Path, second.Path }, folders.Items.Cast<string>());
                    Assert.Equal(second.Path, folders.SelectedItem);
                    Assert.Equal(PatchKind.Exe, form.Install.Kind);
                    // Only the arrow fix's code is in this exe.
                    Assert.True(BugsInput(form).Enabled);
                    Assert.False(GoalsInput(form).Enabled);

                    form.BrowseFolder = s => null;
                    Click(form, "BrowseButton");
                    Assert.Equal(second.Path, folders.SelectedItem);
                }
            }
        });

        // Another version's dll (here: of another length) is replaced by applying the same choice, unless nothing is chosen.
        [Fact]
        public void Apply_WithAnOutdatedDll_IsOfferedForAnyChoice() => StaThread.Run(() => GameInstallTests.WithDll(GameInstallTests.OwnDll, () =>
        {
            using (GameFolder arrowOn = new GameFolder())
            using (GameFolder allOff = new GameFolder())
            {
                foreach (GameFolder folder in new[] { arrowOn, allOff })
                    File.WriteAllBytes(folder.File("run.exe"), new byte[] { 0x4D, 0x5A });
                File.WriteAllBytes(arrowOn.File(GameInstall.DllName), Encoding.ASCII.GetBytes("MZ...A2GPatchFlags\0\x02\0...."));
                File.WriteAllBytes(allOff.File(GameInstall.DllName), Encoding.ASCII.GetBytes("MZ...A2GPatchFlags\0\x00\0...."));
                using (PatchForm form = Form(new List<string>(), DialogResult.OK, arrowOn.Path, allOff.Path))
                {
                    Assert.True(form.Install.DllOutdated);
                    Assert.True(BugsInput(form).Checked);
                    Assert.True(ApplyButton(form).Enabled);

                    Find<ComboBox>(form, "FolderInput").SelectedIndex = 1;
                    Assert.True(form.Install.DllOutdated);
                    Assert.False(ApplyButton(form).Enabled);
                    BugsInput(form).Checked = true;
                    Assert.True(ApplyButton(form).Enabled);
                }
            }
        }));

        // The real dialogs, answered by Dialogs.Answer.
        [Fact]
        public void Dialogs_AskTheUser() => StaThread.Run(() =>
        {
            string folder = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            using (PatchForm form = new PatchForm(new List<string>()))
            {
                Dialogs.Answer(Dialogs.Ok);
                Assert.Equal(folder, form.BrowseFolder(folder), StringComparer.OrdinalIgnoreCase);
                Dialogs.Answer(Dialogs.Cancel);
                Assert.Null(form.BrowseFolder(null));

                // A message box with only OK closes on Escape (Cancel) as well, answering OK.
                Dialogs.Answer(Dialogs.Cancel);
                Assert.Equal(DialogResult.OK, form.ShowMessage("Test", MessageBoxButtons.OK));
                Dialogs.Answer(Dialogs.Yes);
                Assert.Equal(DialogResult.Yes, form.ShowMessage("Test?", MessageBoxButtons.YesNo));
            }
        });

        [Fact]
        public void CloseButton_ClosesTheWindow() => StaThread.Run(() =>
        {
            using (PatchForm form = Form(new List<string>(), DialogResult.OK))
            {
                form.Show();
                Click(form, "CloseButton");

                Assert.True(form.IsDisposed);
            }
        });
    }
}
