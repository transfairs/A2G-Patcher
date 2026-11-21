using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace A2GPatch
{
    /// <summary>One entry of a <see cref="GroupList"/>: a group's name and colour and the state of its tiles.</summary>
    internal sealed class GroupEntry
    {
        /// <summary>The group's name.</summary>
        public string Title = "";
        /// <summary>The group's colour: the bar, the tint of the chosen entry and the count.</summary>
        public Color Accent = SystemColors.Highlight;
        /// <summary>How many of the group's patches are switched on.</summary>
        public int On;
        /// <summary>Whether one of the group's switches differs from the game folder.</summary>
        public bool Pending;
        /// <summary>Whether any of the group's patches can be applied to the game folder.</summary>
        public bool Enabled = true;
    }

    /// <summary>
    /// The navigation left of the tiles: one entry per group with its name, how many of its patches are
    /// on and, while one of them still has to be applied, a dot. The chosen entry is tinted in its
    /// group's colour and marked by a bar that slides over when another is chosen. A click, the arrow
    /// keys, Home and End choose. Drawn by hand like the tiles.
    /// </summary>
    internal sealed class GroupList : Control
    {
        private const int Top96 = 4, Pitch96 = 38, ItemHeight96 = 36, Inset96 = 8;

        private readonly Timer timer = new Timer { Interval = 16 };
        private readonly float u;
        private Theme theme = Theme.Light;
        private int selected, hovered = -1;

        /// <summary>An empty list, the first entry chosen.</summary>
        public GroupList()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            this.BackColor = Color.Transparent;
            this.TabStop = true;
            this.Cursor = Cursors.Hand;
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero))
                this.u = screen.DpiX / 96f;
            this.timer.Tick += (s, e) => this.Animate();
            this.Disposed += (s, e) => this.timer.Dispose();
        }

        /// <summary>Raised when <see cref="Selected"/> changes.</summary>
        public event EventHandler SelectedChanged;

        /// <summary>The groups, top to bottom.</summary>
        public List<GroupEntry> Entries { get; } = new List<GroupEntry>();

        /// <summary>The colours the list is drawn in.</summary>
        public Theme Theme
        {
            get => this.theme;
            set
            {
                this.theme = value;
                this.Invalidate();
            }
        }

        /// <summary>The index of the chosen entry; the bar slides there.</summary>
        public int Selected
        {
            get => this.selected;
            set
            {
                if (value == this.selected)
                    return;
                this.selected = value;
                this.timer.Enabled = true;
                this.Invalidate();
                this.SelectedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Where the bar is, in entries from the top; follows <see cref="Selected"/>.</summary>
        internal float Bar { get; private set; }

        /// <summary>Whether the bar is still sliding.</summary>
        internal bool Moving => this.timer.Enabled;

        /// <summary>The entry under the mouse, -1 for none.</summary>
        internal int Hovered => this.hovered;

        /// <summary>Moves the bar a part of the way to the chosen entry (fast at first, slowing down); the timer stops once it is there.</summary>
        internal void Animate()
        {
            float distance = this.selected - this.Bar;
            this.Bar = Math.Abs(distance) < 0.02f ? this.selected : this.Bar + distance * 0.3f;
            this.timer.Enabled = this.Bar != this.selected;
            this.Invalidate();
        }

        /// <summary>Chooses the entry <paramref name="step"/> further down, from the last back to the first and the other way round.</summary>
        internal void Cycle(int step) => this.Selected = (this.selected + step + this.Entries.Count) % this.Entries.Count;

        private int Px(float pixels) => (int)Math.Round(pixels * this.u);

        /// <summary>Where entry <paramref name="index"/> is drawn.</summary>
        internal Rectangle ItemBounds(int index) =>
            new Rectangle(this.Px(Inset96), this.Px(Top96) + index * this.Px(Pitch96), this.Width - 2 * this.Px(Inset96), this.Px(ItemHeight96));

        // The entry at a point, -1 for none.
        private int IndexAt(Point point)
        {
            for (int i = 0; i < this.Entries.Count; i++)
                if (this.ItemBounds(i).Contains(point))
                    return i;
            return -1;
        }

        /// <summary>As wide as the longest name with its count needs, as tall as all entries.</summary>
        public override Size GetPreferredSize(Size proposedSize)
        {
            int widest = 0;
            foreach (GroupEntry entry in this.Entries)
                widest = Math.Max(widest, TextRenderer.MeasureText(entry.Title, this.Font).Width);
            return new Size(widest + this.Px(2 * Inset96 + 14 + 8 + 7 + 6 + 24 + 8), 2 * this.Px(Top96) + this.Entries.Count * this.Px(Pitch96));
        }

        /// <summary>Chooses the entry clicked.</summary>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int index = this.IndexAt(e.Location);
            if (index >= 0)
                this.Selected = index;
        }

        /// <summary>Tints the entry under the mouse.</summary>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            this.SetHovered(this.IndexAt(e.Location));
        }

        /// <summary>Takes the tint away.</summary>
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            this.SetHovered(-1);
        }

        private void SetHovered(int index)
        {
            this.hovered = index;
            this.Invalidate();
        }

        /// <summary>The arrow keys up and down move through the entries instead of to the next control.</summary>
        protected override bool IsInputKey(Keys keyData) => keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);

        /// <summary>Up and down choose the neighbour, Home and End the first and last entry.</summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int last = this.Entries.Count - 1;
            switch (e.KeyCode)
            {
                case Keys.Up:
                    this.Selected = Math.Max(0, this.selected - 1);
                    break;
                case Keys.Down:
                    this.Selected = Math.Min(last, this.selected + 1);
                    break;
                case Keys.Home:
                    this.Selected = 0;
                    break;
                case Keys.End:
                    this.Selected = last;
                    break;
            }
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

        /// <summary>Tells screen readers that the list is a row of tabs and which one is chosen.</summary>
        protected override AccessibleObject CreateAccessibilityInstance() => new ListAccessibility(this);

        /// <summary>The list as screen readers see it: a tab list whose value is the chosen group.</summary>
        private sealed class ListAccessibility : ControlAccessibleObject
        {
            private readonly GroupList list;

            public ListAccessibility(GroupList list) : base(list) => this.list = list;

            public override AccessibleRole Role => AccessibleRole.PageTabList;

            public override string Value
            {
                get => this.list.Entries[this.list.Selected].Title;
                set { }
            }
        }

        // Not a const: coverlet's Cecil can't resolve System.Windows.Forms to write a constant of its enum.
        private static readonly TextFormatFlags TitleFlags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        private static readonly TextFormatFlags CountFlags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter;

        /// <summary>Draws the entries with their counts and dots, the bar and the focus ring.</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme t = this.theme;
            for (int i = 0; i < this.Entries.Count; i++)
            {
                GroupEntry entry = this.Entries[i];
                Rectangle item = this.ItemBounds(i);
                bool chosen = i == this.selected;
                Color fill = chosen ? PatchTile.Blend(t.Surface, entry.Accent, 0.12f) : i == this.hovered ? PatchTile.Blend(t.Page, t.Text, 0.06f) : t.Page;
                using (GraphicsPath shape = PatchTile.Rounded(item, 5 * this.u))
                {
                    using (Brush brush = new SolidBrush(fill))
                        g.FillPath(brush, shape);
                    if (chosen && this.Focused)
                        using (Pen ring = new Pen(entry.Accent, 2 * this.u))
                            g.DrawPath(ring, shape);
                }

                // From the right: the count of patches switched on, the dot of changes not yet applied.
                int right = item.Right - this.Px(8);
                if (entry.On > 0)
                {
                    string count = entry.On.ToString();
                    int width = Math.Max(this.Px(20), TextRenderer.MeasureText(count, this.Font).Width + this.Px(8));
                    Rectangle badge = new Rectangle(right - width, item.Y + (item.Height - this.Px(18)) / 2, width, this.Px(18));
                    using (GraphicsPath pill = PatchTile.Rounded(badge, badge.Height / 2f))
                    using (Brush brush = new SolidBrush(entry.Accent))
                        g.FillPath(brush, pill);
                    TextRenderer.DrawText(g, count, this.Font, badge, Color.White, CountFlags);
                    right = badge.Left - this.Px(6);
                }
                if (entry.Pending)
                {
                    using (Brush brush = new SolidBrush(t.Pending))
                        g.FillEllipse(brush, right - 7 * this.u, item.Y + item.Height / 2f - 3.5f * this.u, 7 * this.u, 7 * this.u);
                    right -= this.Px(13);
                }

                Rectangle title = new Rectangle(item.X + this.Px(14), item.Y, right - item.X - this.Px(14), item.Height);
                TextRenderer.DrawText(g, entry.Title, this.Font, title, !entry.Enabled ? t.DisabledText : chosen ? t.Title : t.TileText, TitleFlags);
            }

            // The bar at the left edge of the chosen entry, in its colour, on its way there while sliding.
            if (this.Entries.Count > 0)
            {
                Rectangle first = this.ItemBounds(0);
                RectangleF bar = new RectangleF(first.X + 4 * this.u, first.Y + this.Bar * this.Px(Pitch96) + (first.Height - 16 * this.u) / 2, 3 * this.u, 16 * this.u);
                using (GraphicsPath pill = PatchTile.Rounded(bar, bar.Width / 2))
                using (Brush brush = new SolidBrush(this.Entries[this.selected].Accent))
                    g.FillPath(brush, pill);
            }
        }
    }
}
