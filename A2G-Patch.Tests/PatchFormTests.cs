using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Xunit;

namespace A2GPatch.Tests
{
    /// <summary>Tests for PatchForm: what it shows for a folder and how it applies the switches.</summary>
    [Collection("GameInstall statics")]
    public class PatchFormTests
    {
        private static T Find<T>(Form form, string name) where T : Control => (T)form.Controls.Find(name, false).Single();

        private static CheckBox GoalsInput(Form form) => Find<CheckBox>(form, "GoalLimitsInput");
        private static CheckBox ArrowInput(Form form) => Find<CheckBox>(form, "ArrowRepeatInput");
        private static Button ApplyButton(Form form) => Find<Button>(form, "ApplyButton");

        private static void Click(Form form, string name) =>
            typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Find<Button>(form, name), new object[] { EventArgs.Empty });

        // A window on folders, recording its messages and answering them with answer.
        private static PatchForm Form(List<string> messages, DialogResult answer, params string[] folders)
        {
            PatchForm form = new PatchForm(folders.ToList());
            form.ShowMessage = (text, buttons) => { messages.Add(text); return answer; };
            return form;
        }

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
                    Assert.Equal("Torgrenzen aufheben", GoalsInput(form).Text);
                    Assert.True(GoalsInput(form).Enabled && ArrowInput(form).Enabled);
                    Assert.False(GoalsInput(form).Checked || ArrowInput(form).Checked);
                    Assert.False(ApplyButton(form).Enabled);

                    ArrowInput(form).Checked = true;
                    Assert.True(ApplyButton(form).Enabled);
                    ArrowInput(form).Checked = false;
                    Assert.False(ApplyButton(form).Enabled);

                    ArrowInput(form).Checked = true;
                    Click(form, "ApplyButton");
                    Assert.True(GameInstall.Detect(folder.Path).IsApplied(GameInstallTests.Arrow));
                    Assert.False(GameInstall.Detect(folder.Path).IsApplied(GameInstallTests.Goals));
                    Assert.True(ArrowInput(form).Checked);
                    Assert.False(ApplyButton(form).Enabled);

                    ArrowInput(form).Checked = false;
                    Click(form, "ApplyButton");
                    Assert.Equal(GameCode.Both(), File.ReadAllBytes(folder.File("anstoss2.exe")));
                    Assert.Empty(messages);
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
                    Click(form, "ApplyButton");

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
                    Click(form, "ApplyButton");

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
                    Click(form, "ApplyButton");

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
                        Click(form, "ApplyButton");

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
                Assert.False(GoalsInput(form).Enabled || ArrowInput(form).Enabled);
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
                    Assert.False(ArrowInput(form).Enabled);
                    string start = null;
                    form.BrowseFolder = s => { start = s; return second.Path + "\\"; };

                    Click(form, "BrowseButton");
                    Assert.Equal(first.Path, start);
                    Click(form, "BrowseButton");

                    Assert.Equal(new[] { first.Path, second.Path }, folders.Items.Cast<string>());
                    Assert.Equal(second.Path, folders.SelectedItem);
                    Assert.Equal(PatchKind.Exe, form.Install.Kind);
                    // Only the arrow fix's code is in this exe.
                    Assert.True(ArrowInput(form).Enabled);
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
                    Assert.True(ArrowInput(form).Checked);
                    Assert.True(ApplyButton(form).Enabled);

                    Find<ComboBox>(form, "FolderInput").SelectedIndex = 1;
                    Assert.True(form.Install.DllOutdated);
                    Assert.False(ApplyButton(form).Enabled);
                    ArrowInput(form).Checked = true;
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
