// Draws the A2G-Patch logo (a sticking plaster on a pitch-green tile) and writes A2G-Patch.ico
// (16-256 px) and a 256 px PNG. Same family as A2G-Scaler's tools\MakeIcon.cs.
// Not part of the build. Run from the repo folder (Windows PowerShell 5.1):
//   Add-Type -Path tools\MakeIcon.cs -ReferencedAssemblies System.Drawing
//   [MakeIcon]::Write("A2G-Patch.ico", "site\logo.png")
//   Copy-Item A2G-Patch.ico site\favicon.ico
// Write also creates site\logo.small.png as a preview of the small sizes; do not commit it.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class MakeIcon
{
    static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath(); float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    public static Bitmap Draw(int s)
    {
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float m = s <= 16 ? 0 : s * 0.03f;
            var bg = new RectangleF(m, m, s - 2 * m, s - 2 * m);
            using (var path = Round(bg, s * 0.2f))
            using (var br = new LinearGradientBrush(bg, Color.FromArgb(58, 160, 72), Color.FromArgb(24, 100, 40), 90f))
                g.FillPath(br, path);
            // pitch stripes
            if (s >= 32)
                using (var path = Round(bg, s * 0.2f))
                {
                    g.SetClip(path);
                    using (var stripe = new SolidBrush(Color.FromArgb(22, 255, 255, 255)))
                        for (int i = 0; i < 6; i += 2) g.FillRectangle(stripe, bg.X + bg.Width * i / 6f, bg.Y, bg.Width / 6f, bg.Height);
                    g.ResetClip();
                }

            // the plaster, drawn horizontally around the centre and rotated by -45 degrees
            float c = s / 2f;
            float len = s * (s <= 16 ? 1.02f : 0.92f), wid = s * (s <= 16 ? 0.40f : 0.34f);
            g.TranslateTransform(c, c);
            g.RotateTransform(-45f);
            var strip = new RectangleF(-len / 2, -wid / 2, len, wid);
            if (s >= 32)
                using (var path = Round(new RectangleF(strip.X, strip.Y + s * 0.025f, strip.Width, strip.Height), wid / 2))
                using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 30, 10)))
                    g.FillPath(shadow, path);
            using (var path = Round(strip, wid / 2))
            using (var br = new SolidBrush(Color.FromArgb(250, 250, 246)))
                g.FillPath(br, path);
            // gold pad in the middle
            float padW = len * 0.36f, padH = wid * (s <= 16 ? 1f : 0.7f);
            var pad = new RectangleF(-padW / 2, -padH / 2, padW, padH);
            using (var path = Round(pad, Math.Max(0.5f, padH * 0.18f)))
            using (var br = new LinearGradientBrush(pad, Color.FromArgb(255, 216, 90), Color.FromArgb(235, 175, 40), 90f))
                g.FillPath(br, path);
            // air holes on the adhesive ends
            if (s >= 32)
            {
                float r = Math.Max(1f, s * 0.018f);
                using (var dot = new SolidBrush(Color.FromArgb(190, 200, 205, 195)))
                    foreach (float sx in new[] { -1f, 1f })
                        for (int col = 0; col < 2; col++)
                            for (int row = -1; row <= 1; row += 2)
                            {
                                float x = sx * (padW / 2 + len * (0.07f + 0.075f * col));
                                float y = row * wid * 0.17f;
                                g.FillEllipse(dot, x - r, y - r, 2 * r, 2 * r);
                            }
            }
            g.ResetTransform();
        }
        return bmp;
    }

    static byte[] Dib(Bitmap b)
    {
        int s = b.Width; var ms = new MemoryStream(); var w = new BinaryWriter(ms);
        int mask = ((s + 31) / 32) * 4 * s;
        w.Write(40); w.Write(s); w.Write(s * 2); w.Write((short)1); w.Write((short)32);
        w.Write(0); w.Write(s * s * 4 + mask); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (int y = s - 1; y >= 0; y--)
            for (int x = 0; x < s; x++) { var c = b.GetPixel(x, y); w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A); }
        w.Write(new byte[mask]);
        return ms.ToArray();
    }

    public static void Write(string icoPath, string previewPath)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 256 };
        var data = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
            using (var b = Draw(sizes[i]))
            {
                if (sizes[i] == 256) { var ms = new MemoryStream(); b.Save(ms, ImageFormat.Png); data[i] = ms.ToArray(); b.Save(previewPath, ImageFormat.Png); }
                else data[i] = Dib(b);
            }
        using (var w = new BinaryWriter(File.Create(icoPath)))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] & 255)); w.Write((byte)(sizes[i] & 255)); w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32); w.Write(data[i].Length); w.Write(offset);
                offset += data[i].Length;
            }
            foreach (var d in data) w.Write(d);
        }
        // preview strip of the small sizes, 4x enlarged
        using (var strip = new Bitmap(16 * 4 + 24 * 4 + 32 * 4 + 48 * 4 + 50, 48 * 4, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(strip))
        {
            g.Clear(Color.FromArgb(240, 240, 240)); g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
            int x = 0;
            foreach (int s in new[] { 16, 24, 32, 48 }) using (var b = Draw(s)) { g.DrawImage(b, x, 0, s * 4, s * 4); x += s * 4 + 10; }
            strip.Save(Path.ChangeExtension(previewPath, ".small.png"), ImageFormat.Png);
        }
    }
}
