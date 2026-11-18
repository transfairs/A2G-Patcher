using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace A2GPatch
{
    /// <summary>About dialog: logo, version, what the program does and links to the project page and GitHub.</summary>
    internal sealed class AboutDialog : Form
    {
        /// <summary>The project on GitHub, linked in the dialog.</summary>
        internal const string GithubUrl = "https://github.com/transfairs/A2G-Patcher";
        /// <summary>The project page, linked in the dialog.</summary>
        internal const string SiteUrl = "https://transfairs.github.io/A2G-Patcher/";

        private readonly ToolTip tip = new ToolTip();
        private readonly float scale;
        private readonly Theme theme;

        /// <summary>Opens a link target with the shell; tests replace it to avoid opening a browser.</summary>
        internal static Action<string> Open = target => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

        /// <summary>
        /// The version the release workflow stamped into Properties/VersionInfo.cs
        /// (AssemblyInformationalVersion, e.g. "1.2.0"); local builds show "0.0.0-dev".
        /// </summary>
        internal static string Version => ((AssemblyInformationalVersionAttribute)typeof(AboutDialog).Assembly
            .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)[0]).InformationalVersion;

        /// <summary>Builds the dialog in the main window's <paramref name="theme"/>, laid out for 96 dpi and scaled to the screen's like the main window.</summary>
        public AboutDialog(Theme theme)
        {
            this.theme = theme;
            this.BackColor = theme.Page;
            this.ForeColor = theme.Text;
            this.Text = "Über A2G-Patch";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Font = SystemFonts.MessageBoxFont;
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero))
                this.scale = screen.DpiX / 96f;
            this.ClientSize = new Size(this.Px(480), this.Px(210));

            // The icon's 64 px frame (or the nearest one at other dpi), so the box doesn't scale a 32 px one up.
            int logoSize = this.Px(64);
            Bitmap logo;
            using (System.IO.Stream stream = typeof(AboutDialog).Assembly.GetManifestResourceStream("A2GPatch.Logo.ico"))
            using (Icon icon = new Icon(stream, logoSize, logoSize))
                logo = icon.ToBitmap();
            PictureBox picture = new PictureBox { Name = "Logo", Image = logo, SizeMode = PictureBoxSizeMode.Zoom };
            picture.SetBounds(this.Px(12), this.Px(12), logoSize, logoSize);
            this.Disposed += (s, e) => logo.Dispose();
            this.Controls.Add(picture);

            int x = this.Px(88), w = this.ClientSize.Width - x - this.Px(12);
            Label title = new Label { Name = "TitleLabel", Text = "A2G-Patch", Font = new Font(this.Font.FontFamily, this.Font.Size * 1.5f, FontStyle.Bold), AutoSize = false };
            title.SetBounds(x, this.Px(10), w - this.Px(110), this.Px(30));
            Label version = new Label { Name = "VersionLabel", Text = "v" + Version, AutoSize = false, TextAlign = ContentAlignment.MiddleRight, ForeColor = theme.GrayText };
            version.SetBounds(this.ClientSize.Width - this.Px(122), this.Px(14), this.Px(110), this.Px(22));
            Label description = new Label
            {
                Text = "Behebt Abstürze und Fehler von Anstoss 2 Gold und bringt ein paar Verbesserungen mit. "
                    + "Jeder Patch lässt sich wieder ausschalten.",
                AutoSize = false,
            };
            description.SetBounds(x, this.Px(44), w, this.Px(52));
            this.Controls.AddRange(new Control[] { title, version, description });

            this.AddLink(x, this.Px(104), w, "Projektseite: ", SiteUrl);
            this.AddLink(x, this.Px(126), w, "GitHub: ", GithubUrl);

            Button ok = new Button { Name = "OkButton", Text = "OK", DialogResult = DialogResult.OK, UseVisualStyleBackColor = true };
            theme.Style(ok);
            ok.SetBounds(this.ClientSize.Width - this.Px(102), this.ClientSize.Height - this.Px(40), this.Px(90), this.Px(28));
            this.Controls.Add(ok);
            this.AcceptButton = this.CancelButton = ok;
        }

        /// <summary>A label "prefix + url" where the url is clickable and opens in the browser.</summary>
        private void AddLink(int x, int y, int w, string prefix, string url)
        {
            LinkLabel link = new LinkLabel { Text = prefix + url, AutoSize = false, AutoEllipsis = true, LinkArea = new LinkArea(prefix.Length, url.Length),
                LinkColor = this.theme.Link, ActiveLinkColor = this.theme.Link, VisitedLinkColor = this.theme.Link };
            link.SetBounds(x, y, w, this.Px(20));
            link.LinkClicked += (s, e) =>
            {
                try
                {
                    Open(url);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            this.tip.SetToolTip(link, url);
            this.Controls.Add(link);
        }

        /// <summary>Darkens the title bar in the dark theme.</summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (this.theme.IsDark)
                Theme.StyleTitleBar(this.Handle, true);
        }

        private int Px(int pixels) => (int)Math.Round(pixels * this.scale);

        /// <summary>Disposes the tooltip component along with the form.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                this.tip.Dispose();
            base.Dispose(disposing);
        }
    }
}
