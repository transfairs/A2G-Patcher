using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace A2GPatch
{
    /// <summary>
    /// One patch in the window: a rounded tile with its title, what it does (<see cref="Control.Text"/>)
    /// and a sliding switch. A click or the space bar turns it on or off; the switch slides and the
    /// tile takes on the group's colour, hovering tints it. A dot marks a choice not yet applied.
    /// Drawn by hand, so it looks the same from Windows XP on.
    /// </summary>
    internal sealed class PatchTile : Control
    {
        private const float Step = 0.125f;  // per tick of the timer: 8 ticks, about 130 ms, from one end to the other
        private static readonly Color Surface = Color.White, DisabledSurface = Color.FromArgb(0xF1, 0xF3, 0xF2),
            Border = Color.FromArgb(0xD9, 0xDE, 0xDB), Track = Color.FromArgb(0xC3, 0xC9, 0xC6),
            TitleColor = Color.FromArgb(0x1A, 0x1F, 0x1C), TextColor = Color.FromArgb(0x58, 0x63, 0x5E),
            DisabledText = Color.FromArgb(0xA6, 0xAD, 0xAA), PendingColor = Color.FromArgb(0xE8, 0x8A, 0x00);

        private readonly Timer timer = new Timer { Interval = 16 };
        private bool isChecked, pending, hovered;

        /// <summary>An unchecked tile.</summary>
        public PatchTile()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            this.BackColor = Color.Transparent;
            this.TabStop = true;
            this.Cursor = Cursors.Hand;
            this.timer.Tick += (s, e) => this.Animate();
            this.Disposed += (s, e) => this.timer.Dispose();
        }

        /// <summary>Raised when <see cref="Checked"/> changes.</summary>
        public event EventHandler CheckedChanged;

        /// <summary>The heading, in bold above the text.</summary>
        public string Title { get; set; } = "";

        /// <summary>The group's colour: the switch when on, the tile's tint and edge.</summary>
        public Color Accent { get; set; } = SystemColors.Highlight;

        /// <summary>Whether the patch is chosen.</summary>
        public bool Checked
        {
            get => this.isChecked;
            set
            {
                if (value == this.isChecked)
                    return;
                this.isChecked = value;
                this.Start();
                this.CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Whether the choice differs from the game folder's state, i.e. still has to be applied.</summary>
        public bool Pending
        {
            get => this.pending;
            set
            {
                this.pending = value;
                this.Invalidate();
            }
        }

        /// <summary>Where the switch's knob is, 0 (off) to 1 (on); follows <see cref="Checked"/>.</summary>
        internal float Knob { get; private set; }

        /// <summary>How far the hover tint has faded in, 0 to 1; follows the mouse.</summary>
        internal float Hover { get; private set; }

        /// <summary>Whether knob or tint are still moving.</summary>
        internal bool Moving => this.timer.Enabled;

        private void Start()
        {
            this.timer.Enabled = true;
            this.Invalidate();
        }

        /// <summary>Moves knob and tint one step towards where they belong; the timer stops once both are there.</summary>
        internal void Animate()
        {
            this.Knob = Toward(this.Knob, this.isChecked ? 1 : 0);
            this.Hover = Toward(this.Hover, this.hovered && this.Enabled ? 1 : 0);
            this.timer.Enabled = this.Knob != (this.isChecked ? 1 : 0) || this.Hover != (this.hovered && this.Enabled ? 1 : 0);
            this.Invalidate();
        }

        private static float Toward(float value, float target) =>
            value < target ? Math.Min(target, value + Step) : Math.Max(target, value - Step);

        /// <summary>Toggles the tile (raised only while enabled) and takes the focus.</summary>
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            this.Focus();
            this.Checked = !this.Checked;
        }

        /// <summary>The space bar toggles the tile, as with a check box.</summary>
        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space)
                this.Checked = !this.Checked;
        }

        /// <summary>Fades the hover tint in.</summary>
        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            this.hovered = true;
            this.Start();
        }

        /// <summary>Fades the hover tint out.</summary>
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            this.hovered = false;
            this.Start();
        }

        /// <summary>Greys the tile out or brings its colours back.</summary>
        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            this.Start();
        }

        /// <summary>Shows the focus ring.</summary>
        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            this.Invalidate();
        }

        /// <summary>Hides the focus ring.</summary>
        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            this.Invalidate();
        }

        /// <summary>Tells screen readers that the tile is a check box and whether it is checked.</summary>
        protected override AccessibleObject CreateAccessibilityInstance() => new TileAccessibility(this);

        private sealed class TileAccessibility : ControlAccessibleObject
        {
            private readonly PatchTile tile;

            public TileAccessibility(PatchTile tile) : base(tile) => this.tile = tile;

            public override AccessibleRole Role => AccessibleRole.CheckButton;

            public override AccessibleStates State => base.State | (this.tile.Checked ? AccessibleStates.Checked : AccessibleStates.None);
        }

        /// <summary>Draws tile, accent stripe, switch, the pending dot, title and text.</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float u = g.DpiX / 96f, on = Ease(this.Knob), pad = 10 * u;
            bool enabled = this.Enabled;
            Color accent = enabled ? this.Accent : Blend(this.Accent, DisabledSurface, 0.6f);

            // The tile: tinted by the group's colour when on, a little more under the mouse.
            RectangleF box = new RectangleF(0.5f, 0.5f, this.Width - 1.5f, this.Height - 1.5f);
            using (GraphicsPath shape = Rounded(box, 7 * u))
            {
                using (Brush fill = new SolidBrush(enabled ? Blend(Surface, this.Accent, 0.07f * on + 0.04f * this.Hover) : DisabledSurface))
                    g.FillPath(fill, shape);
                using (Pen edge = new Pen(Blend(Border, accent, Math.Max(0.75f * on, 0.5f * this.Hover)), this.Focused ? 2 * u : 1))
                    g.DrawPath(edge, shape);
                // The stripe on the left edge grows in as the switch goes on.
                g.SetClip(shape);
                float stripe = 4 * u * on;
                using (Brush fill = new SolidBrush(accent))
                    g.FillRectangle(fill, 0, 0, stripe, this.Height);
                g.ResetClip();
            }

            // The switch: a pill whose track takes the accent and whose knob slides over.
            RectangleF track = new RectangleF(this.Width - pad - 34 * u, pad, 34 * u, 18 * u);
            using (GraphicsPath pill = Rounded(track, track.Height / 2))
            using (Brush fill = new SolidBrush(Blend(enabled ? Track : Border, accent, on)))
                g.FillPath(fill, pill);
            float knob = track.Height - 4 * u;
            RectangleF dot = new RectangleF(track.X + 2 * u + (track.Width - track.Height) * on, track.Y + 2 * u, knob, knob);
            using (Brush shadow = new SolidBrush(Color.FromArgb(40, Color.Black)))
                g.FillEllipse(shadow, dot.X, dot.Y + u, dot.Width, dot.Height);
            g.FillEllipse(Brushes.White, dot);

            // Not applied yet: an orange dot left of the switch.
            if (this.pending)
                using (Brush fill = new SolidBrush(PendingColor))
                    g.FillEllipse(fill, track.X - 13 * u, track.Y + track.Height / 2 - 3.5f * u, 7 * u, 7 * u);

            using (Font bold = new Font(this.Font, FontStyle.Bold))
            {
                Rectangle text = this.TextBox(u, bold, this.Width, this.Height);
                Rectangle title = new Rectangle(text.Left, (int)(pad - u), (int)(track.X - 16 * u) - text.Left, bold.Height);
                TextRenderer.DrawText(g, this.Title, bold, title, enabled ? TitleColor : DisabledText,
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                TextRenderer.DrawText(g, this.Text, this.Font, text, enabled ? TextColor : DisabledText, TextFlags);
            }
        }

        // Not a const: coverlet's Cecil can't resolve System.Windows.Forms to write a constant of its enum.
        private static readonly TextFormatFlags TextFlags = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis;

        // Where the text under the title goes in a tile of the given size (u = pixels per 96-dpi pixel).
        private Rectangle TextBox(float u, Font bold, int width, int height)
        {
            float pad = 10 * u;
            int left = (int)(pad + 4 * u), top = (int)(pad - u) + bold.Height + (int)(3 * u);
            return new Rectangle(left, top, width - left - (int)pad, height - top - (int)(pad / 2));
        }

        /// <summary>The proposed width, and the height title and the whole text need at that width.</summary>
        public override Size GetPreferredSize(Size proposedSize)
        {
            using (Graphics g = this.CreateGraphics())
            using (Font bold = new Font(this.Font, FontStyle.Bold))
            {
                Rectangle box = this.TextBox(g.DpiX / 96f, bold, proposedSize.Width, 0);
                int text = TextRenderer.MeasureText(g, this.Text, this.Font, new Size(box.Width, int.MaxValue), TextFlags).Height;
                return new Size(proposedSize.Width, text - box.Height);
            }
        }

        // Smooth start and end (smoothstep).
        private static float Ease(float t) => t * t * (3 - 2 * t);

        /// <summary><paramref name="from"/> moved by <paramref name="amount"/> (0 to 1) towards <paramref name="to"/>.</summary>
        internal static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
            (int)(from.R + (to.R - from.R) * amount), (int)(from.G + (to.G - from.G) * amount), (int)(from.B + (to.B - from.B) * amount));

        /// <summary>A rectangle with corners rounded by <paramref name="radius"/>.</summary>
        internal static GraphicsPath Rounded(RectangleF box, float radius)
        {
            float d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(box.X, box.Y, d, d, 180, 90);
            path.AddArc(box.Right - d, box.Y, d, d, 270, 90);
            path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
            path.AddArc(box.X, box.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
