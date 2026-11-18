using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace A2GPatch
{
    /// <summary>
    /// The program's window: a banner, the game folder and how it is patched, one tile per patch in
    /// groups, and apply.
    /// </summary>
    internal sealed class PatchForm : Form
    {
        /// <summary>The entries of the "Darstellung" menu, by <see cref="ThemeChoice"/>.</summary>
        internal static readonly string[] ThemeTitles = { "Wie Windows", "Hell", "Dunkel" };
        /// <summary>The headings of the groups, by <see cref="PatchGroup"/>.</summary>
        internal static readonly string[] GroupTitles = { "Patches", "Balancing", "Nur für die GOG-Version" };
        // The banner and the apply button look the same in both themes.
        private static readonly Color Grass = Color.FromArgb(0x1B, 0x7A, 0x45), DarkGrass = Color.FromArgb(0x0C, 0x4D, 0x2A),
            Apply = Color.FromArgb(0x1E, 0x8E, 0x4E), ApplyHover = Color.FromArgb(0x18, 0x78, 0x41), ApplyDown = Color.FromArgb(0x12, 0x62, 0x35);
        private const int WmSettingChange = 0x1A;
        private const int Width96 = 900, MinWidth96 = 480, Margin96 = 16, Gap96 = 10, TileHeight96 = 70, MinTileWidth96 = 240, MaxColumns = 3;

        private readonly ComboBox folderInput;
        private readonly Button browseButton, applyButton, closeButton;
        private readonly Label statusLabel, progressLabel;
        private readonly BusyIndicator progress;
        private readonly TilePanel tilePanel;
        private readonly Dictionary<Patch, PatchTile> switches = new Dictionary<Patch, PatchTile>();
        private readonly List<KeyValuePair<Label, PatchTile[]>> groups = new List<KeyValuePair<Label, PatchTile[]>>();
        private readonly ToolTip tips = new ToolTip();
        private readonly ContextMenuStrip themeMenu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
        private readonly float scale;
        private readonly int bannerHeight;
        private GameInstall install;
        private bool busy;
        private Theme theme;
        private ThemeChoice themeChoice;

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
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = SystemFonts.MessageBoxFont;
            this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;
            this.Disposed += (s, e) =>
            {
                this.tips.Dispose();
                this.themeMenu.Dispose();
            };

            // Laid out for 96 dpi and scaled to the screen's (the program is dpiAware, see app.manifest).
            // Header and footer stick to the window's edges; the tiles scroll in between and arrange
            // themselves for the window's width (LayOutTiles).
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero))
                this.scale = screen.DpiX / 96f;
            int width = this.Px(Width96), margin = this.Px(Margin96);
            this.bannerHeight = this.Px(60);
            int buttonHeight = this.Px(30), footerHeight = this.Px(12) + buttonHeight + this.Px(14), tilesTop = this.bannerHeight + this.Px(84);
            // A first size for the anchors to measure from; the real one follows once the tiles are in.
            this.ClientSize = new Size(width, tilesTop + this.Px(200) + footerHeight);

            int y = this.bannerHeight + this.Px(14);
            Label folderLabel = new Label { Text = "Spielordner:", AutoSize = true, Location = new Point(margin, y + this.Px(4)) };
            int browseWidth = this.Px(120), inputLeft = margin + this.Px(84);
            this.folderInput = new ComboBox
            {
                Name = "FolderInput", DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Location = new Point(inputLeft, y), Width = width - margin - browseWidth - this.Px(8) - inputLeft,
            };
            this.folderInput.Items.AddRange(folders.ToArray());
            this.browseButton = new Button
            {
                Name = "BrowseButton", Text = "Durchsuchen …", UseVisualStyleBackColor = true, Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Bounds = new Rectangle(width - margin - browseWidth, y - 1, browseWidth, this.folderInput.Height + 2),
            };
            y += this.Px(32);
            this.statusLabel = new Label
            {
                Name = "StatusLabel", AutoSize = false, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Location = new Point(margin, y), Size = new Size(width - 2 * margin, this.Px(34)),
            };
            this.Controls.AddRange(new Control[] { folderLabel, this.folderInput, this.browseButton, this.statusLabel });

            // "Über" in the banner, left of the penalty area (transparent, so the pitch shows through).
            LinkLabel aboutLink = new LinkLabel
            {
                Name = "AboutLink", Text = "Über", AutoSize = true, BackColor = Color.Transparent, Anchor = AnchorStyles.Top | AnchorStyles.Right,
                LinkColor = Color.White, ActiveLinkColor = Color.White, VisitedLinkColor = Color.White, LinkBehavior = LinkBehavior.HoverUnderline,
            };
            aboutLink.Location = new Point(width - this.Px(80) - aboutLink.PreferredWidth, (this.bannerHeight - aboutLink.PreferredHeight) / 2);
            aboutLink.LinkClicked += (s, e) => this.ShowAbout();
            this.tips.SetToolTip(aboutLink, "Version und Links zum Projekt");
            this.Controls.Add(aboutLink);

            // "Darstellung" left of it: a menu of light, dark or as Windows says.
            LinkLabel themeLink = new LinkLabel
            {
                Name = "ThemeLink", Text = "Darstellung", AutoSize = true, BackColor = Color.Transparent, Anchor = AnchorStyles.Top | AnchorStyles.Right,
                LinkColor = Color.White, ActiveLinkColor = Color.White, VisitedLinkColor = Color.White, LinkBehavior = LinkBehavior.HoverUnderline,
            };
            themeLink.Location = new Point(aboutLink.Left - this.Px(16) - themeLink.PreferredWidth, aboutLink.Top);
            foreach (ThemeChoice choice in Enum.GetValues(typeof(ThemeChoice)))
                this.themeMenu.Items.Add(new ToolStripMenuItem(ThemeTitles[(int)choice], null, (s, e) => this.ChooseTheme(choice)) { Name = choice + "Theme" });
            themeLink.LinkClicked += (s, e) => this.themeMenu.Show(themeLink, 0, themeLink.Height);
            this.tips.SetToolTip(themeLink, "Hell, dunkel oder wie Windows");
            this.Controls.Add(themeLink);

            // The tiles by group, in the order of Patch.All within a group, in a panel that scrolls.
            this.tilePanel = new TilePanel
            {
                Name = "TilePanel", AutoScroll = true, AutoScrollMargin = new Size(0, this.Px(Gap96 + 6)),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Bounds = new Rectangle(0, tilesTop, width, this.ClientSize.Height - tilesTop - footerHeight),
            };
            foreach (PatchGroup group in Enum.GetValues(typeof(PatchGroup)))
            {
                Label heading = new Label
                {
                    Name = group + "Label", Text = GroupTitles[(int)group], AutoSize = true,
                    Font = new Font(this.Font, FontStyle.Bold),
                };
                this.tilePanel.Controls.Add(heading);
                List<PatchTile> tiles = new List<PatchTile>();
                foreach (Patch patch in Patch.All.Where(p => p.Group == group))
                {
                    PatchTile tile = new PatchTile
                    {
                        Name = patch.GetType().Name + "Input", Title = patch.Title, Text = patch.Name,
                        AccessibleName = patch.Title, AccessibleDescription = patch.Name,
                    };
                    tile.CheckedChanged += (s, e) =>
                    {
                        this.ShowProgress(BusyState.Idle);
                        this.UpdateApplyButton();
                    };
                    this.tips.SetToolTip(tile, patch.Name);
                    this.switches[patch] = tile;
                    this.tilePanel.Controls.Add(tile);
                    tiles.Add(tile);
                }
                this.groups.Add(new KeyValuePair<Label, PatchTile[]>(heading, tiles.ToArray()));
            }
            this.tilePanel.Arrange = this.ArrangeTiles;
            this.tilePanel.Paint += (s, e) => this.PaintRules(e.Graphics);
            this.Controls.Add(this.tilePanel);

            y = this.tilePanel.Bottom + this.Px(12);
            int closeWidth = this.Px(100), applyWidth = this.Px(140);
            this.closeButton = new Button
            {
                Name = "CloseButton", Text = "Schließen", DialogResult = DialogResult.Cancel, UseVisualStyleBackColor = true,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right, Bounds = new Rectangle(width - margin - closeWidth, y, closeWidth, buttonHeight),
            };
            this.applyButton = new Button
            {
                Name = "ApplyButton", Text = "Übernehmen", FlatStyle = FlatStyle.Flat, BackColor = Apply, ForeColor = Color.White,
                Font = new Font(this.Font, FontStyle.Bold), Cursor = Cursors.Hand, Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Bounds = new Rectangle(this.closeButton.Left - this.Px(8) - applyWidth, y, applyWidth, buttonHeight),
            };
            this.applyButton.FlatAppearance.BorderSize = 0;
            this.applyButton.FlatAppearance.MouseOverBackColor = ApplyHover;
            this.applyButton.FlatAppearance.MouseDownBackColor = ApplyDown;
            this.applyButton.EnabledChanged += (s, e) => this.applyButton.BackColor = this.applyButton.Enabled ? Apply : this.theme.ApplyDisabled;
            this.progress = new BusyIndicator
            {
                Name = "Progress", Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                Location = new Point(margin, y + this.Px(5)), Size = new Size(this.Px(20), this.Px(20)),
            };
            this.progressLabel = new Label
            {
                Name = "ProgressLabel", AutoSize = true, Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                Location = new Point(margin + this.Px(28), y + this.Px(8)),
            };
            this.Controls.AddRange(new Control[] { this.progress, this.progressLabel, this.applyButton, this.closeButton });
            this.CancelButton = this.closeButton;
            this.themeChoice = Theme.LoadChoice();
            this.ApplyTheme(Theme.For(this.themeChoice));

            // As tall as the tiles need at full width, but no larger than the screen; the user may
            // shrink it down to one column with a few tiles in view.
            Rectangle screenArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size frame = this.SizeFromClientSize(Size.Empty);
            this.MinimumSize = this.SizeFromClientSize(new Size(this.Px(MinWidth96), tilesTop + this.Px(120) + footerHeight));
            this.ClientSize = new Size(Math.Min(width, screenArea.Width - frame.Width),
                Math.Min(tilesTop + this.LayOutTiles(width) + footerHeight, screenArea.Height - frame.Height));

            this.BrowseFolder = start =>
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "Ordner von Anstoss 2 Gold", SelectedPath = start ?? "" })
                    return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
            };
            this.ShowMessage = (text, buttons) => MessageBox.Show(this, text, this.Text, buttons, buttons == MessageBoxButtons.OK ? MessageBoxIcon.Warning : MessageBoxIcon.Question);

            this.folderInput.SelectedIndexChanged += (s, e) => this.LoadFolder();
            this.folderInput.HandleCreated += (s, e) =>
            {
                if (this.theme.IsDark)
                    this.StyleFolderInput();
            };
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

        /// <summary>The colours the window is drawn in: light or dark, as chosen or as Windows' app mode says.</summary>
        internal Theme Theme => this.theme;

        /// <summary>The "Darstellung" menu.</summary>
        internal ContextMenuStrip ThemeMenu => this.themeMenu;

        /// <summary>Switches to light, dark or Windows' app mode and remembers it for the next start.</summary>
        internal void ChooseTheme(ThemeChoice choice)
        {
            this.themeChoice = choice;
            Theme.SaveChoice(choice);
            this.ApplyTheme(Theme.For(choice));
        }

        // Colours everything for the theme; the frame and the scroll bars once they have a window handle.
        private void ApplyTheme(Theme theme)
        {
            this.theme = theme;
            this.BackColor = theme.Page;
            this.ForeColor = theme.Text;
            theme.Style(this.folderInput);
            theme.Style(this.browseButton);
            theme.Style(this.closeButton);
            this.applyButton.BackColor = this.applyButton.Enabled ? Apply : theme.ApplyDisabled;
            for (int group = 0; group < this.groups.Count; group++)
            {
                this.groups[group].Key.ForeColor = theme.Groups[group];
                foreach (PatchTile tile in this.groups[group].Value)
                {
                    tile.Accent = theme.Groups[group];
                    tile.Theme = theme;
                }
            }
            this.progress.Theme = theme;
            theme.Style(this.themeMenu);
            foreach (ToolStripMenuItem item in this.themeMenu.Items)
                item.Checked = item.Name == this.themeChoice + "Theme";
            this.tilePanel.Dark = theme.IsDark;
            if (this.folderInput.IsHandleCreated)
                this.StyleFolderInput();
            if (this.IsHandleCreated)
                Theme.StyleTitleBar(this.Handle, theme.IsDark);
            this.Invalidate(true);
        }

        // The closed folder list as Windows draws it, dark or light (BackColor only reaches the list that drops down).
        private void StyleFolderInput() => Theme.StyleNative(this.folderInput.Handle, this.theme.IsDark, Theme.DarkComboBox);

        /// <summary>Darkens the title bar if the window opens dark.</summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (this.theme.IsDark)
                Theme.StyleTitleBar(this.Handle, true);
        }

        /// <summary>Follows Windows when the user switches between light and dark app mode while the window is open.</summary>
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WmSettingChange && Marshal.PtrToStringUni(m.LParam) == "ImmersiveColorSet")
                this.ApplyTheme(Theme.For(this.themeChoice));
        }

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
            foreach (KeyValuePair<Patch, PatchTile> s in this.switches)
            {
                s.Value.Enabled = this.install != null && this.install.CanApply(s.Key);
                s.Value.Checked = this.install != null && this.install.IsApplied(s.Key);
            }
            this.UpdateApplyButton();
        }

        // Only a choice that differs from the folder's state can be applied, or any choice that keeps
        // a msvfw32.dll from another version of A2G-Patch (applying replaces it). A disabled switch is
        // never checked (LoadFolder), and with A2G-Patch's dll in the folder every switch is enabled.
        // The tiles that differ get their dot, the button counts them.
        private void UpdateApplyButton()
        {
            int changes = 0;
            foreach (KeyValuePair<Patch, PatchTile> s in this.switches)
            {
                s.Value.Pending = this.install != null && s.Value.Enabled && s.Value.Checked != this.install.IsApplied(s.Key);
                changes += s.Value.Pending ? 1 : 0;
            }
            this.applyButton.Enabled = changes > 0 || (this.install != null && this.install.DllOutdated && this.switches.Values.Any(tile => tile.Checked));
            this.applyButton.Text = changes > 0 ? $"Übernehmen ({changes})" : "Übernehmen";
        }

        /// <summary>Draws the banner (a strip of pitch with its markings and the program's name) and the lines under the headings and above the buttons.</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int width = this.ClientSize.Width, height = this.bannerHeight, stripe = this.Px(36);
            using (LinearGradientBrush grass = new LinearGradientBrush(new Rectangle(0, 0, width, height), Grass, DarkGrass, LinearGradientMode.Vertical))
                g.FillRectangle(grass, 0, 0, width, height);
            using (Brush mown = new SolidBrush(Color.FromArgb(16, Color.White)))
                for (int x = 0; x < width; x += 2 * stripe)
                    g.FillRectangle(mown, x, 0, stripe, height);
            // Halfway line, centre circle and spot on the right; penalty and goal area at the edge.
            using (Pen chalk = new Pen(Color.FromArgb(80, Color.White), 1.5f * this.scale))
            {
                float middle = width * 0.68f, radius = height * 0.42f, half = height / 2f;
                g.DrawLine(chalk, middle, 0, middle, height);
                g.DrawEllipse(chalk, middle - radius, half - radius, 2 * radius, 2 * radius);
                g.FillEllipse(Brushes.White, middle - 2 * this.scale, half - 2 * this.scale, 4 * this.scale, 4 * this.scale);
                g.DrawRectangle(chalk, width - this.Px(64), height * 0.12f, this.Px(80), height * 0.76f);
                g.DrawRectangle(chalk, width - this.Px(24), height * 0.32f, this.Px(40), height * 0.36f);
            }
            using (Font name = new Font(this.Font.FontFamily, this.Font.SizeInPoints * 1.6f, FontStyle.Bold))
                TextRenderer.DrawText(g, "A2G-Patch", name, new Point(this.Px(14), this.Px(7)), Color.White);
            TextRenderer.DrawText(g, "Fehlerbehebungen für Anstoss 2 Gold", this.Font, new Point(this.Px(17), this.Px(36)), Color.FromArgb(0xD4, 0xEE, 0xDD));

            using (Pen rule = new Pen(this.theme.Line))
                g.DrawLine(rule, 0, this.tilePanel.Bottom, width, this.tilePanel.Bottom);
        }

        // The line right of each heading, up to the right edge of its tiles.
        private void PaintRules(Graphics g)
        {
            foreach (KeyValuePair<Label, PatchTile[]> group in this.groups)
            {
                Label heading = group.Key;
                int middle = heading.Top + heading.Height / 2;
                using (Pen rule = new Pen(PatchTile.Blend(heading.ForeColor, this.theme.Page, 0.75f)))
                    g.DrawLine(rule, heading.Right + this.Px(6), middle, group.Value.Max(t => t.Right), middle);
            }
        }

        // Lays the tiles out for the panel's width, leaving room for the scroll bar if they don't all fit.
        private void ArrangeTiles()
        {
            if (this.LayOutTiles(this.tilePanel.Width) > this.tilePanel.Height)
                this.LayOutTiles(this.tilePanel.Width - SystemInformation.VerticalScrollBarWidth);
        }

        /// <summary>
        /// Places headings and tiles for a panel <paramref name="width"/> wide: as many columns as fit
        /// (one to three), each row as tall as its longest text needs. Returns the height they take.
        /// </summary>
        private int LayOutTiles(int width)
        {
            int margin = this.Px(Margin96), gap = this.Px(Gap96), minHeight = this.Px(TileHeight96);
            int columns = Math.Max(1, Math.Min(MaxColumns, (width - 2 * margin + gap) / (this.Px(MinTileWidth96) + gap)));
            int tileWidth = (width - 2 * margin - (columns - 1) * gap) / columns;
            int scrolled = this.tilePanel.AutoScrollPosition.Y, y = 0;
            foreach (KeyValuePair<Label, PatchTile[]> group in this.groups)
            {
                group.Key.Location = new Point(margin - 2, y + scrolled);
                y += this.Px(26);
                for (int first = 0; first < group.Value.Length; first += columns)
                {
                    PatchTile[] row = group.Value.Skip(first).Take(columns).ToArray();
                    int height = Math.Max(minHeight, row.Max(t => t.GetPreferredSize(new Size(tileWidth, 0)).Height));
                    for (int i = 0; i < row.Length; i++)
                        row[i].Bounds = new Rectangle(margin + i * (tileWidth + gap), y + scrolled, tileWidth, height);
                    y += height + gap;
                }
                y += this.Px(6);
            }
            return y;
        }

        /// <summary>The tiles' scrolling area: double buffered, and it has the window place the tiles before it works out how far to scroll.</summary>
        private sealed class TilePanel : Panel
        {
            private bool dark;
            /// <summary>Creates the panel, double buffered and redrawn when resized.</summary>
            public TilePanel()
            {
                this.DoubleBuffered = true;
                this.ResizeRedraw = true;
            }

            /// <summary>Places the tiles for the panel's current size.</summary>
            public Action Arrange;

            /// <summary>Whether the scroll bar is dark.</summary>
            public bool Dark
            {
                set
                {
                    this.dark = value;
                    if (this.IsHandleCreated)
                        Theme.StyleNative(this.Handle, value, Theme.DarkScrollBars);
                }
            }

            /// <summary>Darkens the scroll bar if the panel is created dark.</summary>
            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                if (this.dark)
                    Theme.StyleNative(this.Handle, true, Theme.DarkScrollBars);
            }

            /// <summary>Has the tiles placed before the panel lays itself out and works out its scroll range.</summary>
            protected override void OnLayout(LayoutEventArgs levent)
            {
                this.Arrange?.Invoke();
                base.OnLayout(levent);
            }
        }

        private int Px(int pixels) => (int)Math.Round(pixels * this.scale);

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
                foreach (PatchTile tile in this.switches.Values)
                    tile.Enabled = false;
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

        /// <summary>Shows the about dialog over the window.</summary>
        internal void ShowAbout()
        {
            using (AboutDialog dialog = new AboutDialog(this.theme))
                dialog.ShowDialog(this);
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
