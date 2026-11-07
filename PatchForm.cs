using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace A2GPatch
{
    /// <summary>The program's window: game folder, how it is patched, one switch per patch, and apply.</summary>
    internal sealed class PatchForm : Form
    {
        private readonly ComboBox folderInput;
        private readonly Button browseButton, applyButton, closeButton;
        private readonly Label statusLabel, progressLabel;
        private readonly BusyIndicator progress;
        private readonly Dictionary<Patch, CheckBox> switches = new Dictionary<Patch, CheckBox>();
        private GameInstall install;
        private bool busy;

        /// <summary>
        /// How long the indicator turns at least, in milliseconds: patching takes a moment only, and a
        /// flash would look like nothing happened. Tests set it to 0.
        /// </summary>
        internal int MinimumBusyTime = 600;

        /// <summary>Asks for a game folder starting at the given one; null if cancelled. Tests replace it.</summary>
        internal Func<string, string> BrowseFolder;
        /// <summary>Shows a message (text, buttons) and returns the answer. Tests replace it.</summary>
        internal Func<string, MessageBoxButtons, DialogResult> ShowMessage;
        /// <summary>Starts the program again with administrator rights for the folder. Tests replace it.</summary>
        internal Action<string> RestartElevated = folder =>
            Process.Start(new ProcessStartInfo(Application.ExecutablePath, "\"" + folder + "\"") { Verb = "runas", UseShellExecute = true });

        /// <summary>Builds the window offering <paramref name="folders"/>, the first one chosen.</summary>
        public PatchForm(List<string> folders)
        {
            this.Text = "A2G-Patch";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = SystemFonts.MessageBoxFont;
            this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            Label folderLabel = new Label { Text = "Spielordner:", AutoSize = true, Location = new Point(12, 16) };
            this.folderInput = new ComboBox { Name = "FolderInput", DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(96, 12), Size = new Size(316, 23) };
            this.folderInput.Items.AddRange(folders.ToArray());
            this.browseButton = new Button { Name = "BrowseButton", Text = "Durchsuchen …", Location = new Point(418, 11), Size = new Size(110, 25), UseVisualStyleBackColor = true };
            this.statusLabel = new Label { Name = "StatusLabel", AutoSize = false, Location = new Point(12, 46), Size = new Size(516, 36) };
            this.Controls.AddRange(new Control[] { folderLabel, this.folderInput, this.browseButton, this.statusLabel });

            int y = 86;
            bool gogHeading = false;
            foreach (Patch patch in Patch.All)
            {
                // The GOG-only patches (no flag) come last, under a heading of their own.
                if (patch is FilePatch && patch.Flag == 0 && !gogHeading)
                {
                    gogHeading = true;
                    this.Controls.Add(new Label { Name = "GogLabel", Text = "Nur für die GOG-Version:", AutoSize = true, Location = new Point(12, y + 6) });
                    y += 28;
                }
                CheckBox box = new CheckBox { Name = patch.GetType().Name + "Input", Text = patch.Name, AutoSize = true, Location = new Point(14, y) };
                box.CheckedChanged += (s, e) =>
                {
                    this.ShowProgress(BusyState.Idle);
                    this.UpdateApplyButton();
                };
                this.switches[patch] = box;
                this.Controls.Add(box);
                y += 26;
            }

            this.applyButton = new Button { Name = "ApplyButton", Text = "Übernehmen", Location = new Point(342, y + 8), Size = new Size(90, 28), UseVisualStyleBackColor = true };
            this.closeButton = new Button { Name = "CloseButton", Text = "Schließen", DialogResult = DialogResult.Cancel, Location = new Point(438, y + 8), Size = new Size(90, 28), UseVisualStyleBackColor = true };
            this.progress = new BusyIndicator { Name = "Progress", Location = new Point(14, y + 12), Size = new Size(20, 20) };
            this.progressLabel = new Label { Name = "ProgressLabel", AutoSize = true, Location = new Point(40, y + 15) };
            this.Controls.AddRange(new Control[] { this.progress, this.progressLabel, this.applyButton, this.closeButton });
            this.CancelButton = this.closeButton;
            this.ClientSize = new Size(540, y + 48);

            this.BrowseFolder = start =>
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "Ordner von Anstoss 2 Gold", SelectedPath = start ?? "" })
                    return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
            };
            this.ShowMessage = (text, buttons) => MessageBox.Show(this, text, this.Text, buttons, buttons == MessageBoxButtons.OK ? MessageBoxIcon.Warning : MessageBoxIcon.Question);

            this.folderInput.SelectedIndexChanged += (s, e) => this.LoadFolder();
            this.browseButton.Click += (s, e) => this.Browse();
            this.applyButton.Click += (s, e) => this.ApplyChoice();
            this.closeButton.Click += (s, e) => this.Close();
            // Not while patching: the exe or DLL would be left half written.
            this.FormClosing += (s, e) => e.Cancel = this.busy;

            if (folders.Count > 0)
                this.folderInput.SelectedIndex = 0;
            else
                this.LoadFolder();
        }

        /// <summary>The game in the chosen folder; null without one or if it couldn't be read.</summary>
        internal GameInstall Install => this.install;

        /// <summary>Whether the switches are being applied right now.</summary>
        internal bool Busy => this.busy;

        private void LoadFolder()
        {
            this.ShowProgress(BusyState.Idle);
            string folder = this.folderInput.SelectedItem as string;
            try
            {
                this.install = folder == null ? null : GameInstall.Detect(folder);
                this.statusLabel.Text = StatusText(this.install);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // An exe another program holds open can't be read.
                this.install = null;
                this.statusLabel.Text = ex.Message;
            }
            foreach (KeyValuePair<Patch, CheckBox> s in this.switches)
            {
                s.Value.Enabled = this.install != null && this.install.CanApply(s.Key);
                s.Value.Checked = this.install != null && this.install.IsApplied(s.Key);
            }
            this.UpdateApplyButton();
        }

        // Only a choice that differs from the folder's state can be applied, or any choice that keeps
        // a msvfw32.dll from another version of A2G-Patch (applying replaces it). A disabled switch is
        // never checked (LoadFolder), and with A2G-Patch's dll in the folder every switch is enabled.
        private void UpdateApplyButton() =>
            this.applyButton.Enabled = this.install != null
                && (this.switches.Any(s => s.Value.Enabled && s.Value.Checked != this.install.IsApplied(s.Key))
                    || (this.install.DllOutdated && this.switches.Values.Any(box => box.Checked)));

        /// <summary>What the window says about the game in a folder.</summary>
        internal static string StatusText(GameInstall install)
        {
            if (install == null)
                return "Kein Spielordner gefunden.";
            string exe = Path.GetFileName(install.ExePath);
            switch (install.Kind)
            {
                case PatchKind.Exe:
                    return $"{exe} wird direkt gepatcht.";
                case PatchKind.Dll:
                    if (install.ForeignDll)
                        return $"{exe} ist gepackt, aber die {GameInstall.DllName} im Ordner gehört nicht zu A2G-Patch.";
                    return install.DllOutdated
                        ? $"{exe} ist gepackt. Die {GameInstall.DllName} im Ordner stammt von einer anderen Version von A2G-Patch; „Übernehmen“ ersetzt sie."
                        : $"{exe} ist gepackt: Die Patches wirken über die {GameInstall.DllName} ab dem Spielstart.";
                default:
                    return "Kein bekanntes Anstoss 2 Gold in diesem Ordner.";
            }
        }

        private void Browse()
        {
            string folder = this.BrowseFolder(this.folderInput.SelectedItem as string);
            if (folder == null)
                return;
            folder = Path.GetFullPath(folder).TrimEnd('\\');
            int index = this.folderInput.FindStringExact(folder);
            if (index < 0)
                index = this.folderInput.Items.Add(folder);
            this.folderInput.SelectedIndex = index;
            this.LoadFolder();
        }

        // Applies the switches in the background, the indicator turning meanwhile; the game must not
        // run (its exe or the DLL would be in use).
        private void ApplyChoice()
        {
            if (GameInstall.IsGameRunning(this.install.Folder))
            {
                this.ShowMessage("Bitte zuerst das Spiel beenden.", MessageBoxButtons.OK);
                return;
            }
            GameInstall target = this.install;
            List<Patch> chosen = this.switches.Where(s => s.Value.Checked).Select(s => s.Key).ToList();
            int minimum = this.MinimumBusyTime;
            this.SetBusy(true);
            Task.Factory.StartNew(() =>
            {
                Stopwatch watch = Stopwatch.StartNew();
                try
                {
                    target.Apply(chosen);
                }
                finally
                {
                    Thread.Sleep(Math.Max(0, minimum - (int)watch.ElapsedMilliseconds));
                }
            }).ContinueWith(this.Applied, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // Back on the window's thread: confirms success or says what went wrong. Without write access
        // (e.g. under Program Files) it offers to start again as administrator.
        private void Applied(Task work)
        {
            this.SetBusy(false);
            Exception error = work.Exception?.InnerException;
            if (error is UnauthorizedAccessException)
            {
                if (this.ShowMessage("Für diesen Ordner fehlen die Schreibrechte. Als Administrator neu starten?", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    this.Elevate();
            }
            else if (error != null)
                this.ShowMessage(error.Message, MessageBoxButtons.OK);
            if (this.IsDisposed)
                return;
            this.LoadFolder();
            if (error == null)
                this.ShowProgress(BusyState.Done);
        }

        // Locks the window while busy; afterwards LoadFolder sets the switches and the apply button again.
        private void SetBusy(bool on)
        {
            this.busy = on;
            this.UseWaitCursor = on;
            this.folderInput.Enabled = this.browseButton.Enabled = this.closeButton.Enabled = !on;
            if (on)
            {
                foreach (CheckBox box in this.switches.Values)
                    box.Enabled = false;
                this.applyButton.Enabled = false;
            }
            this.ShowProgress(on ? BusyState.Busy : BusyState.Idle);
        }

        private void ShowProgress(BusyState state)
        {
            this.progress.State = state;
            this.progressLabel.Text = state == BusyState.Busy ? "Änderungen werden übernommen …"
                : state == BusyState.Done ? "Änderungen übernommen." : "";
        }

        // Closes once the elevated copy runs; stays if the user declined the UAC prompt.
        private void Elevate()
        {
            try
            {
                this.RestartElevated(this.install.Folder);
                this.Close();
            }
            catch (Win32Exception)
            {
            }
        }
    }
}
