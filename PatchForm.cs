using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace A2GPatch
{
    /// <summary>The program's window: game folder, how it is patched, one switch per patch, and apply.</summary>
    internal sealed class PatchForm : Form
    {
        private readonly ComboBox folderInput;
        private readonly Button browseButton, applyButton, closeButton;
        private readonly Label statusLabel;
        private readonly Dictionary<Patch, CheckBox> switches = new Dictionary<Patch, CheckBox>();
        private GameInstall install;

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
                box.CheckedChanged += (s, e) => this.UpdateApplyButton();
                this.switches[patch] = box;
                this.Controls.Add(box);
                y += 26;
            }

            this.applyButton = new Button { Name = "ApplyButton", Text = "Übernehmen", Location = new Point(342, y + 8), Size = new Size(90, 28), UseVisualStyleBackColor = true };
            this.closeButton = new Button { Name = "CloseButton", Text = "Schließen", DialogResult = DialogResult.Cancel, Location = new Point(438, y + 8), Size = new Size(90, 28), UseVisualStyleBackColor = true };
            this.Controls.AddRange(new Control[] { this.applyButton, this.closeButton });
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

            if (folders.Count > 0)
                this.folderInput.SelectedIndex = 0;
            else
                this.LoadFolder();
        }

        /// <summary>The game in the chosen folder; null without one or if it couldn't be read.</summary>
        internal GameInstall Install => this.install;

        private void LoadFolder()
        {
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

        // Applies the switches; the game must not run (its exe or the DLL would be in use). Without
        // write access (e.g. under Program Files) it offers to start again as administrator.
        private void ApplyChoice()
        {
            if (GameInstall.IsGameRunning(this.install.Folder))
            {
                this.ShowMessage("Bitte zuerst das Spiel beenden.", MessageBoxButtons.OK);
                return;
            }
            try
            {
                this.install.Apply(this.switches.Where(s => s.Value.Checked).Select(s => s.Key).ToList());
            }
            catch (UnauthorizedAccessException)
            {
                if (this.ShowMessage("Für diesen Ordner fehlen die Schreibrechte. Als Administrator neu starten?", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    this.Elevate();
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException)
            {
                this.ShowMessage(ex.Message, MessageBoxButtons.OK);
            }
            if (!this.IsDisposed)
                this.LoadFolder();
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
