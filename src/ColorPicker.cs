using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Dungeons2SkinLoader
{
    /// <summary>Plain double-buffered drawing surface.</summary>
    public class Canvas : Panel
    {
        public Canvas() { DoubleBuffered = true; ResizeRedraw = true; }
    }

    /// <summary>Dark colour picker: HSV field + hue bar, hex entry, the skin's own colours,
    /// and an eyedropper over the whole skin texture.</summary>
    public class ColorPickerDialog : Form
    {
        public int Rgb { get { return rgb; } }
        int rgb, original; float h, s, v;
        Img skin;
        Canvas field, hueBar, preview, skinView, palette; TextBox hex; Label rgbLabel, hoverLabel;
        Bitmap fieldBmp; float fieldHue = -1;
        int hoverX = -1, hoverY = -1; List<int> skinColours = new List<int>();
        bool updatingHex;

        const int SkinScale = 5;

        public ColorPickerDialog(int start, Img skinTexture, string title)
        {
            rgb = original = start; skin = skinTexture;
            ToHsv(start, out h, out s, out v);
            Text = title; BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.F(9.5f);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; KeyPreview = true;
            ClientSize = new Size(skin != null ? 760 : 420, 470);
            KeyDown += (o, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } else if (e.KeyCode == Keys.Enter && !hex.Focused) Accept(); };

            // --- HSV field + hue bar
            field = new Canvas { Location = new Point(20, 20), Size = new Size(240, 240), Cursor = Cursors.Cross };
            field.Paint += PaintField; MouseEventHandler fdrag = (o, e) => { if (e.Button != MouseButtons.Left) return; s = Clamp(e.X / 239f); v = Clamp(1 - e.Y / 239f); FromHsv(); };
            field.MouseDown += fdrag; field.MouseMove += fdrag;
            hueBar = new Canvas { Location = new Point(272, 20), Size = new Size(26, 240), Cursor = Cursors.Hand };
            hueBar.Paint += PaintHue; MouseEventHandler hdrag = (o, e) => { if (e.Button != MouseButtons.Left) return; h = Clamp(e.Y / 239f) * 360f; FromHsv(); };
            hueBar.MouseDown += hdrag; hueBar.MouseMove += hdrag;

            // --- preview (old | new), hex, rgb
            preview = new Canvas { Location = new Point(312, 20), Size = new Size(90, 72) };
            preview.Paint += PaintPreview;
            preview.MouseClick += (o, e) => { if (e.Y < 36) SetRgb(original); };   // click "before" to go back
            var hexLbl = new Label { Text = "HEX", Location = new Point(312, 104), AutoSize = true, ForeColor = Theme.Dim, Font = Theme.F(8.5f, FontStyle.Bold) };
            hex = new TextBox { Location = new Point(312, 124), Width = 90, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 11f), CharacterCasing = CharacterCasing.Upper, MaxLength = 7 };
            hex.TextChanged += (o, e) =>
            {
                if (updatingHex) return;
                var t = hex.Text.Trim().TrimStart('#');
                int val;
                if (t.Length == 6 && int.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out val)) { ToHsv(val, out h, out s, out v); rgb = val; Refresh2(false); }
            };
            rgbLabel = new Label { Location = new Point(312, 158), AutoSize = true, ForeColor = Theme.Dim, Font = Theme.F(9f) };

            // --- colours used by the skin
            var palLbl = new Label { Text = "COLOURS IN THIS SKIN", Location = new Point(20, 276), AutoSize = true, ForeColor = Theme.Dim, Font = Theme.F(8.5f, FontStyle.Bold) };
            palette = new Canvas { Location = new Point(20, 298), Size = new Size(382, 88), Cursor = Cursors.Hand };
            palette.Paint += PaintPalette;
            palette.MouseClick += (o, e) => { int i = (e.Y / 22) * 17 + e.X / 22; if (i >= 0 && i < skinColours.Count) SetRgb(skinColours[i]); };
            if (skin != null)
            {
                var counts = new Dictionary<int, int>();
                for (int y = 0; y < skin.H; y++) for (int x = 0; x < skin.W; x++)
                    {
                        var c = skin.Get(x, y); if (c[3] < 128) continue;
                        int k = (c[0] << 16) | (c[1] << 8) | c[2]; int n; counts.TryGetValue(k, out n); counts[k] = n + 1;
                    }
                skinColours = counts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).Take(68).ToList();
            }

            // --- eyedropper over the skin
            if (skin != null)
            {
                var skLbl = new Label { Text = "PICK FROM THE SKIN  (click any pixel)", Location = new Point(420, 12), AutoSize = true, ForeColor = Theme.Dim, Font = Theme.F(8.5f, FontStyle.Bold) };
                skinView = new Canvas { Location = new Point(420, 32), Size = new Size(64 * SkinScale, 64 * SkinScale), Cursor = Cursors.Cross };
                skinView.Paint += PaintSkin;
                skinView.MouseMove += (o, e) => { int x = e.X / SkinScale, y = e.Y / SkinScale; if (x != hoverX || y != hoverY) { hoverX = x; hoverY = y; skinView.Invalidate(); UpdateHover(); } };
                skinView.MouseLeave += (o, e) => { hoverX = hoverY = -1; skinView.Invalidate(); UpdateHover(); };
                skinView.MouseClick += (o, e) =>
                {
                    int x = e.X / SkinScale, y = e.Y / SkinScale;
                    if (x < 0 || y < 0 || x > 63 || y > 63) return;
                    var c = skin.Get(x, y); if (c[3] < 128) return;
                    SetRgb((c[0] << 16) | (c[1] << 8) | c[2]);
                };
                hoverLabel = new Label { Location = new Point(420, 32 + 64 * SkinScale + 6), AutoSize = true, ForeColor = Theme.Dim };
                Controls.AddRange(new Control[] { skLbl, skinView, hoverLabel });
            }

            var ok = new TactileButton("Use this colour", BtnKind.Primary) { Location = new Point(ClientSize.Width - 200, ClientSize.Height - 62), Width = 180, BackColor = Theme.Bg };
            var cancel = new TactileButton("Cancel", BtnKind.Secondary) { Location = new Point(ClientSize.Width - 320, ClientSize.Height - 62), Width = 108, BackColor = Theme.Bg };
            ok.Click += (o, e) => Accept(); cancel.Click += (o, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.AddRange(new Control[] { field, hueBar, preview, hexLbl, hex, rgbLabel, palLbl, palette, ok, cancel });
            Refresh2(true);
        }

        void Accept() { DialogResult = DialogResult.OK; Close(); }
        static float Clamp(float x) { return Math.Max(0, Math.Min(1, x)); }

        void SetRgb(int c) { rgb = c; ToHsv(c, out h, out s, out v); Refresh2(true); }
        void FromHsv() { rgb = HsvToRgb(h, s, v); Refresh2(true); }
        void Refresh2(bool setHex)
        {
            if (setHex) { updatingHex = true; hex.Text = "#" + rgb.ToString("X6"); updatingHex = false; }
            rgbLabel.Text = "R " + (rgb >> 16 & 255) + "   G " + (rgb >> 8 & 255) + "   B " + (rgb & 255);
            field.Invalidate(); hueBar.Invalidate(); preview.Invalidate(); palette.Invalidate();
        }
        void UpdateHover()
        {
            if (hoverLabel == null) return;
            if (hoverX < 0 || hoverY < 0 || hoverX > 63 || hoverY > 63) { hoverLabel.Text = ""; return; }
            var c = skin.Get(hoverX, hoverY);
            hoverLabel.Text = c[3] < 128 ? "(" + hoverX + ", " + hoverY + ")  transparent"
                : "(" + hoverX + ", " + hoverY + ")  #" + ((c[0] << 16) | (c[1] << 8) | c[2]).ToString("X6");
        }

        // ---------------------------------------------------------------- paint
        void PaintField(object o, PaintEventArgs e)
        {
            if (fieldBmp == null || fieldHue != h)
            {
                fieldHue = h; if (fieldBmp != null) fieldBmp.Dispose();
                fieldBmp = new Bitmap(240, 240);
                for (int y = 0; y < 240; y++)
                    for (int x = 0; x < 240; x++)
                    {
                        int c = HsvToRgb(h, x / 239f, 1 - y / 239f);
                        fieldBmp.SetPixel(x, y, Color.FromArgb(c >> 16 & 255, c >> 8 & 255, c & 255));
                    }
            }
            var g = e.Graphics; g.DrawImageUnscaled(fieldBmp, 0, 0); g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = s * 239, cy = (1 - v) * 239;
            g.SmoothingMode = SmoothingMode.None;
            using (var p = new Pen(Color.Black, 3)) g.DrawRectangle(p, cx - 6, cy - 6, 12, 12);
            using (var p = new Pen(Color.White, 1)) g.DrawRectangle(p, cx - 6, cy - 6, 12, 12);
        }
        void PaintHue(object o, PaintEventArgs e)
        {
            var g = e.Graphics;
            for (int y = 0; y < 240; y++)
            {
                int c = HsvToRgb(y / 239f * 360f, 1, 1);
                using (var p = new Pen(Color.FromArgb(c >> 16 & 255, c >> 8 & 255, c & 255))) g.DrawLine(p, 0, y, 26, y);
            }
            float hy = h / 360f * 239; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(Color.Black, 3)) g.DrawRectangle(p, 1, hy - 3, 23, 6);
            using (var p = new Pen(Color.White, 1.5f)) g.DrawRectangle(p, 1, hy - 3, 23, 6);
        }
        void PaintPreview(object o, PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Bg);
            using (var b = new SolidBrush(ToColor(original))) g.FillRectangle(b, 0, 0, 90, 36);
            using (var b = new SolidBrush(ToColor(rgb))) g.FillRectangle(b, 0, 36, 90, 36);
            using (var p = new Pen(Theme.Line)) g.DrawRectangle(p, 0, 0, 89, 71);
            using (var b = new SolidBrush(Color.FromArgb(160, 0, 0, 0))) { g.FillRectangle(b, 2, 2, 42, 14); g.FillRectangle(b, 2, 38, 34, 14); }
            using (var f = Theme.F(7.5f, FontStyle.Bold)) using (var b = new SolidBrush(Color.White)) { g.DrawString("BEFORE", f, b, 3, 2); g.DrawString("NEW", f, b, 3, 38); }
        }
        void PaintPalette(object o, PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Bg);
            for (int i = 0; i < skinColours.Count; i++)
            {
                var r = new Rectangle((i % 17) * 22, (i / 17) * 22, 20, 20);
                using (var b = new SolidBrush(ToColor(skinColours[i]))) g.FillRectangle(b, r);
                using (var p = new Pen(skinColours[i] == rgb ? Theme.Accent : Theme.Line, skinColours[i] == rgb ? 2 : 1)) g.DrawRectangle(p, r);
            }
        }
        void PaintSkin(object o, PaintEventArgs e)
        {
            var g = e.Graphics; g.InterpolationMode = InterpolationMode.NearestNeighbor;
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    var c = skin.Get(x, y);
                    Color col = c[3] < 128 ? (((x + y) & 1) == 0 ? Color.FromArgb(34, 37, 45) : Color.FromArgb(28, 31, 38)) : Color.FromArgb(c[0], c[1], c[2]);
                    using (var b = new SolidBrush(col)) g.FillRectangle(b, x * SkinScale, y * SkinScale, SkinScale, SkinScale);
                }
            if (hoverX >= 0 && hoverY >= 0 && hoverX < 64 && hoverY < 64)
            {
                using (var p = new Pen(Color.White, 2)) g.DrawRectangle(p, hoverX * SkinScale - 1, hoverY * SkinScale - 1, SkinScale + 1, SkinScale + 1);
                // magnifier: 7x7 pixels around the cursor, drawn next to it
                int mx = Math.Min(64 * SkinScale - 78, hoverX * SkinScale + 12), my = Math.Max(0, hoverY * SkinScale - 84);
                g.FillRectangle(Brushes.Black, mx - 2, my - 2, 74, 74);
                for (int dy = -3; dy <= 3; dy++)
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        int x = hoverX + dx, y = hoverY + dy; Color col = Theme.Bg;
                        if (x >= 0 && y >= 0 && x < 64 && y < 64) { var c = skin.Get(x, y); if (c[3] >= 128) col = Color.FromArgb(c[0], c[1], c[2]); }
                        using (var b = new SolidBrush(col)) g.FillRectangle(b, mx + (dx + 3) * 10, my + (dy + 3) * 10, 10, 10);
                    }
                using (var p = new Pen(Color.White, 2)) g.DrawRectangle(p, mx + 30, my + 30, 10, 10);
            }
        }

        // ----------------------------------------------------------------- colour math
        static Color ToColor(int c) { return Color.FromArgb(c >> 16 & 255, c >> 8 & 255, c & 255); }
        public static void ToHsv(int c, out float h, out float s, out float v)
        {
            float r = (c >> 16 & 255) / 255f, g = (c >> 8 & 255) / 255f, b = (c & 255) / 255f;
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            v = max; s = max == 0 ? 0 : d / max;
            if (d == 0) h = 0;
            else if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
        }
        public static int HsvToRgb(float h, float s, float v)
        {
            float c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c, r, g, b;
            if (h < 60) { r = c; g = x; b = 0; } else if (h < 120) { r = x; g = c; b = 0; } else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; } else if (h < 300) { r = x; g = 0; b = c; } else { r = c; g = 0; b = x; }
            return ((int)Math.Round((r + m) * 255) << 16) | ((int)Math.Round((g + m) * 255) << 8) | (int)Math.Round((b + m) * 255);
        }
    }
}
