using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace A2GPatch
{
    /// <summary>What the <see cref="BusyIndicator"/> shows.</summary>
    internal enum BusyState
    {
        /// <summary>Nothing.</summary>
        Idle,
        /// <summary>A turning arc: work is running.</summary>
        Busy,
        /// <summary>A green tick: the work succeeded.</summary>
        Done,
    }

    /// <summary>
    /// A small square that draws a turning arc while busy and a tick once done. Drawn by hand: the
    /// fonts of Windows XP have no tick character.
    /// </summary>
    internal sealed class BusyIndicator : Control
    {
        private const int StepDegrees = 30;
        private readonly Timer timer = new Timer { Interval = 60 };
        private BusyState state;

        /// <summary>A hidden indicator.</summary>
        public BusyIndicator()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            this.BackColor = Color.Transparent;
            this.Visible = false;
            this.timer.Tick += (s, e) => this.Turn();
            this.Disposed += (s, e) => this.timer.Dispose();
        }

        /// <summary>Where the arc starts, in degrees; advances while busy.</summary>
        internal int Angle { get; private set; }

        /// <summary>What it shows; hidden when idle, the arc turns only while busy.</summary>
        public BusyState State
        {
            get => this.state;
            set
            {
                this.state = value;
                this.Visible = value != BusyState.Idle;
                this.timer.Enabled = value == BusyState.Busy;
                this.Invalidate();
            }
        }

        private void Turn()
        {
            this.Angle = (this.Angle + StepDegrees) % 360;
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float pen = Math.Max(2f, this.Width / 8f);
            RectangleF box = new RectangleF(pen / 2, pen / 2, this.Width - pen, this.Height - pen);
            if (this.state == BusyState.Done)
            {
                using (Pen tick = new Pen(Color.FromArgb(0x10, 0x8A, 0x3E), pen) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    e.Graphics.DrawLines(tick, new[]
                    {
                        new PointF(box.Left + box.Width * 0.08f, box.Top + box.Height * 0.55f),
                        new PointF(box.Left + box.Width * 0.38f, box.Bottom - box.Height * 0.1f),
                        new PointF(box.Right - box.Width * 0.05f, box.Top + box.Height * 0.12f),
                    });
                return;
            }
            using (Pen track = new Pen(SystemColors.ControlLight, pen))
                e.Graphics.DrawEllipse(track, box);
            using (Pen arc = new Pen(SystemColors.Highlight, pen) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                e.Graphics.DrawArc(arc, box, this.Angle, 100);
        }
    }
}
