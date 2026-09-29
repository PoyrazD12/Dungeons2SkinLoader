using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;

namespace Dungeons2SkinLoader
{
    // =================================================================== theme
    public static class Theme
    {
        // blue / black
        public static readonly Color Bg = Color.FromArgb(7, 9, 15), Panel = Color.FromArgb(12, 16, 26), Card = Color.FromArgb(20, 27, 43),
            CardHi = Color.FromArgb(29, 39, 62), Line = Color.FromArgb(38, 52, 82), Text = Color.FromArgb(234, 240, 255),
            Dim = Color.FromArgb(134, 150, 184), Accent = Color.FromArgb(56, 142, 255), AccentHi = Color.FromArgb(98, 170, 255),
            AccentDeep = Color.FromArgb(22, 82, 180), Gold = Color.FromArgb(246, 190, 70), Warn = Color.FromArgb(240, 184, 72),
            Bad = Color.FromArgb(236, 98, 92), Blue = Color.FromArgb(120, 184, 255), OnAccent = Color.FromArgb(255, 255, 255);
        public static Font F(float size, FontStyle st = FontStyle.Regular) { return new Font("Segoe UI", size, st); }
        public static Font Semi(float size) { return new Font("Segoe UI Semibold", size); }

        // blocky display font (Edit Undo BRK), embedded in the exe; falls back to Segoe UI
        static System.Drawing.Text.PrivateFontCollection pfc; static FontFamily pixel; static bool pixelTried;
        public static Font P(float size)
        {
            if (!pixelTried)
            {
                pixelTried = true;
                try
                {
                    using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("font.ttf"))
                    {
                        var data = new byte[s.Length]; s.Read(data, 0, data.Length);
                        var mem = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(data.Length);   // kept for the app's lifetime
                        System.Runtime.InteropServices.Marshal.Copy(data, 0, mem, data.Length);
                        pfc = new System.Drawing.Text.PrivateFontCollection(); pfc.AddMemoryFont(mem, data.Length);
                        pixel = pfc.Families[0];
                    }
                }
                catch { pixel = null; }
            }
            return pixel != null ? new Font(pixel, size, FontStyle.Regular) : Semi(size);
        }
        public static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
        public static Color Shade(Color c, float k) { return k < 0 ? Lerp(c, Color.FromArgb(c.A, 0, 0, 0), -k) : Lerp(c, Color.FromArgb(c.A, 255, 255, 255), k); }
        public static bool Square = true;   // blocky Minecraft look: no rounded corners
        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = rad * 2;
            if (Square || d <= 0) { p.AddRectangle(new RectangleF((float)Math.Round(r.X), (float)Math.Round(r.Y), (float)Math.Round(r.Width), (float)Math.Round(r.Height))); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        public static void Hq(Graphics g) { g.SmoothingMode = SmoothingMode.None; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.Half; }

        /// <summary>Minecraft-style block: 2px black outline, light top/left and dark bottom/right bevel
        /// (flipped when pressed so it looks pushed in).</summary>
        public static void Bevel(Graphics g, RectangleF r, Color face, bool pressed)
        {
            var rr = Rectangle.Round(r);
            var hi = Shade(face, 0.28f); var lo = Shade(face, -0.35f);
            if (pressed) { var tmp = hi; hi = lo; lo = tmp; }
            using (var b = new SolidBrush(hi)) { g.FillRectangle(b, rr.X + 2, rr.Y + 2, rr.Width - 4, 2); g.FillRectangle(b, rr.X + 2, rr.Y + 2, 2, rr.Height - 4); }
            using (var b = new SolidBrush(lo)) { g.FillRectangle(b, rr.X + 2, rr.Bottom - 4, rr.Width - 4, 2); g.FillRectangle(b, rr.Right - 4, rr.Y + 2, 2, rr.Height - 4); }
            using (var p = new Pen(Color.Black, 2)) g.DrawRectangle(p, rr.X + 1, rr.Y + 1, rr.Width - 2, rr.Height - 2);
        }
    }

    // ============================================================= animation
    /// <summary>Base for controls that ease their hover/press state every frame.</summary>
    public class AnimControl : Control
    {
        static readonly Timer clock = new Timer { Interval = 15 };
        static readonly List<AnimControl> live = new List<AnimControl>();
        static AnimControl() { clock.Tick += (s, e) => { foreach (var c in live.ToArray()) c.Step(); }; clock.Start(); }

        protected float hover, press, extra; protected float hoverT, pressT, extraT;
        public AnimControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            live.Add(this); Disposed += (s, e) => live.Remove(this);
        }
        protected virtual void Step()
        {
            bool ch = false;
            ch |= Ease(ref hover, hoverT, 0.22f); ch |= Ease(ref press, pressT, 0.35f); ch |= Ease(ref extra, extraT, 0.14f);
            if (ch) Invalidate();
        }
        protected static bool Ease(ref float v, float t, float k)
        {
            if (Math.Abs(v - t) < 0.004f) { if (v == t) return false; v = t; return true; }
            v += (t - v) * k; return true;
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); if (Enabled) hoverT = 1; }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoverT = 0; pressT = 0; }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (Enabled && e.Button == MouseButtons.Left) pressT = 1; }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressT = 0; }
        protected void PaintParentBg(Graphics g) { g.Clear(Parent != null ? Parent.BackColor : Theme.Panel); }
    }

    public enum BtnKind { Primary, Secondary }

    /// <summary>Rounded button with hover glow, a press that sinks into its base, and a click ripple.</summary>
    public class TactileButton : AnimControl
    {
        public BtnKind Kind; public string Glyph;
        PointF rippleAt; float ripple = 1;
        public TactileButton(string text, BtnKind kind = BtnKind.Secondary, string glyph = null)
        {
            Text = text; Kind = kind; Glyph = glyph; Cursor = Cursors.Hand; Height = 44; Width = 160;
            Font = Theme.P(Kind == BtnKind.Primary ? 14f : 12.5f);
        }
        protected override void Step() { base.Step(); if (ripple < 1) { ripple = Math.Min(1, ripple + 0.045f); Invalidate(); } }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); rippleAt = e.Location; ripple = 0; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; PaintParentBg(g); Theme.Hq(g);
            Color baseC, hiC, fg, under;
            if (Kind == BtnKind.Primary) { baseC = Theme.Accent; hiC = Theme.AccentHi; fg = Theme.OnAccent; under = Theme.AccentDeep; }
            else { baseC = Theme.Card; hiC = Theme.CardHi; fg = Theme.Text; under = Color.FromArgb(4, 6, 11); }
            float depth = 4f, sink = depth * 0.8f * press;
            // the "base" the button sits on; pressing pushes the face down into it
            using (var path = Theme.Round(new RectangleF(1, 1 + depth, Width - 3, Height - depth - 2), 10))
            using (var b = new SolidBrush(under)) g.FillPath(b, path);
            var body = new RectangleF(1, 1 + sink, Width - 3, Height - depth - 2);
            var c = Theme.Shade(Theme.Lerp(baseC, hiC, hover), -0.12f * press);
            using (var path = Theme.Round(body, 10))
            {
                using (var b = new SolidBrush(c)) g.FillPath(b, path);
                Theme.Bevel(g, body, c, press > 0.5f);
                if (hover > 0.02f && Kind != BtnKind.Primary) using (var p = new Pen(Color.FromArgb((int)(200 * hover), Theme.Accent), 2)) g.DrawRectangle(p, body.X + 1, body.Y + 1, body.Width - 2, body.Height - 2);
                if (ripple < 1)
                {
                    g.SetClip(path);
                    float r = 20 + ripple * Width;
                    using (var b = new SolidBrush(Color.FromArgb((int)(80 * (1 - ripple)), 255, 255, 255)))
                        g.FillEllipse(b, rippleAt.X - r, rippleAt.Y - r, 2 * r, 2 * r);
                    g.ResetClip();
                }

            }
            var txt = (Glyph != null ? Glyph + "   " : "") + Text;
            var sz = g.MeasureString(txt, Font);
            using (var b = new SolidBrush(fg)) g.DrawString(txt, Font, b, (Width - sz.Width) / 2, body.Y + (body.Height - sz.Height) / 2);
        }
    }

    /// <summary>Selectable card with a radio dot that pops in when chosen.</summary>
    public class OptionCard : AnimControl
    {
        public string Title, Desc; public event EventHandler Picked;
        public bool Checked { get { return extraT > 0.5f; } set { extraT = value ? 1 : 0; Invalidate(); } }
        public OptionCard(string t, string d) { Title = t; Desc = d; Height = 74; Cursor = Cursors.Hand; }
        protected override void OnClick(EventArgs e) { base.OnClick(e); if (Picked != null) Picked(this, e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; PaintParentBg(g); Theme.Hq(g);
            var r = new RectangleF(1, 1 + 1.5f * press, Width - 3, Height - 4);
            using (var path = Theme.Round(r, 11))
            {
                using (var b = new SolidBrush(Theme.Lerp(Theme.Lerp(Theme.Card, Theme.CardHi, hover * 0.7f), Color.FromArgb(22, 38, 72), extra))) g.FillPath(b, path);
                using (var p = new Pen(Theme.Lerp(Theme.Lerp(Theme.Line, Theme.Dim, hover * 0.5f), Theme.Accent, extra), 1 + extra)) g.DrawPath(p, path);
            }
            float cy = r.Y + r.Height / 2;
            using (var b = new SolidBrush(Theme.Bg)) g.FillRectangle(b, 17, cy - 10, 20, 20);
            using (var p = new Pen(Theme.Lerp(Theme.Dim, Theme.Accent, extra), 2)) g.DrawRectangle(p, 17, cy - 10, 20, 20);
            float d = (float)Math.Round(10 * extra * (1 + 0.25f * (float)Math.Sin(Math.PI * extra)));
            if (d > 0.5f) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 27 - d / 2, cy - d / 2, d, d);
            using (var b = new SolidBrush(Theme.Text)) g.DrawString(Title, Theme.P(14.3f), b, 52, r.Y + 10);
            using (var b = new SolidBrush(Theme.Dim)) g.DrawString(Desc, Theme.F(9f), b, new RectangleF(52, r.Y + 33, Width - 62, 36));
        }
    }

    /// <summary>One entry of "Your skins": the game's hero, an arrow, then your skin.</summary>
    public class SlotCard : AnimControl
    {
        public SkinSlot Slot; public Hero Hero; public Bitmap Custom; public string ModeText, FileName;
        public bool Selected { get { return extraT > 0.5f; } set { extraT = value ? 1 : 0; Invalidate(); } }
        public SlotCard() { Height = 132; Cursor = Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; PaintParentBg(g); Theme.Hq(g);
            var r = new RectangleF(2, 3 + 1.5f * press, Width - 5, Height - 9);
            using (var path = Theme.Round(r, 12))
            {
                using (var b = new SolidBrush(Theme.Lerp(Theme.Lerp(Theme.Card, Theme.CardHi, hover * 0.6f), Color.FromArgb(22, 38, 72), extra))) g.FillPath(b, path);
                using (var p = new Pen(Theme.Lerp(Theme.Lerp(Theme.Line, Theme.Dim, hover * 0.4f), Theme.Accent, extra), 1 + 1.2f * extra)) g.DrawPath(p, path);
            }
            float s = 78, x0 = r.X + 12, y0 = r.Y + 34;
            // hero name above its original look, arrow, then the custom skin
            using (var b = new SolidBrush(Theme.Text)) g.DrawString(Hero != null ? Hero.Display : "?", Theme.P(14.3f), b, x0, r.Y + 8);
            using (var b = new SolidBrush(Theme.Lerp(Theme.Dim, Theme.Accent, extra))) g.DrawString(ModeText, Theme.F(8.5f), b, r.Right - 12 - g.MeasureString(ModeText, Theme.F(8.5f)).Width, r.Y + 11);
            if (Hero != null && Hero.Original != null) g.DrawImage(Hero.Original, x0, y0, s, s);
            DrawArrow(g, x0 + s + 8, y0 + s / 2, 34, Theme.Lerp(Theme.Dim, Theme.Accent, 0.35f + 0.65f * Math.Max(extra, hover)));
            if (Custom != null) g.DrawImage(Custom, x0 + s + 50, y0, s, s);
            using (var b = new SolidBrush(Theme.Dim))
                g.DrawString(FileName, Theme.F(8f), b, new RectangleF(x0 + 2 * s + 58, y0 + 24, r.Right - (x0 + 2 * s + 64), 40));
        }
        public static void DrawArrow(Graphics g, float x, float cy, float w, Color c)
        {
            using (var p = new Pen(c, 4f) { StartCap = LineCap.Square, EndCap = LineCap.Square, LineJoin = LineJoin.Miter })
            {
                g.DrawLine(p, x, cy, x + w, cy);
                g.DrawLines(p, new[] { new PointF(x + w - 9, cy - 8), new PointF(x + w, cy), new PointF(x + w - 9, cy + 8) });
            }
        }
    }

    /// <summary>Big before/after banner: the game's hero -> your skin (drag to turn).</summary>
    public class SwapBanner : AnimControl
    {
        public Hero Hero; public Func<int, double, Bitmap> RenderCustom; public double Yaw = -22;
        int dragX; bool dragging; Bitmap cache; double cacheYaw = double.NaN; int cacheSize; float nudge;
        public void Refresh3D() { cache = null; extraT = 1; extra = 0; Invalidate(); }
        public SwapBanner() { Height = 340; Cursor = Cursors.SizeWE; }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); dragging = true; dragX = e.X; }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (!dragging) return; Yaw -= (e.X - dragX) * 0.9; dragX = e.X; Invalidate(); }
        protected override void Step() { base.Step(); nudge += 0.06f; if (hover > 0.01f || extra < 0.99f) Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; PaintParentBg(g); Theme.Hq(g);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var path = Theme.Round(r, 16))
            {
                using (var b = new LinearGradientBrush(r, Color.FromArgb(22, 32, 56), Color.FromArgb(10, 13, 22), 90f)) g.FillPath(b, path);
                using (var p = new Pen(Theme.Line)) g.DrawPath(p, path);
            }
            int size = (int)Math.Min(Height - 76, (Width - 130) / 2);
            float half = Width / 2f, lx = (half - 45 - size) / 2 + 12, rx = half + 45 + (half - 45 - size) / 2 - 12, iy = 62;
            using (var b = new SolidBrush(Theme.Dim)) g.DrawString("GAME'S HERO", Theme.P(11.7f), b, lx, 12);
            using (var b = new SolidBrush(Theme.Text)) g.DrawString(Hero != null ? Hero.Display : "", Theme.P(19.5f), b, lx, 28);
            using (var b = new SolidBrush(Theme.Accent)) g.DrawString("YOUR SKIN", Theme.P(11.7f), b, rx, 12);
            using (var b = new SolidBrush(Theme.Dim)) g.DrawString("drag to turn it around", Theme.F(9.5f), b, rx, 32);
            // soft floor shadows
            foreach (var x in new[] { lx, rx })
                using (var b = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillEllipse(b, x + size * 0.22f, iy + size * 0.93f, size * 0.56f, size * 0.07f);
            if (Hero != null && Hero.Original != null) g.DrawImage(Hero.Original, lx, iy, size, size);
            float bob = (float)Math.Sin(nudge) * 3 * hover;
            SlotCard.DrawArrow(g, half - 30 + bob, iy + size / 2f, 60, Theme.Accent);
            if (RenderCustom != null)
            {
                if (cache == null || cacheYaw != Yaw || cacheSize != size) { cache = RenderCustom(size, Yaw); cacheYaw = Yaw; cacheSize = size; }
                if (cache != null)
                {
                    // new skins pop in with a quick scale-up
                    float k = 0.9f + 0.1f * extra, w = size * k;
                    g.DrawImage(cache, rx + (size - w) / 2, iy + (size - w), w, w);
                }
            }
        }
    }

    /// <summary>8x8 face grid (face with the outer layer drawn over it, as in the game). Click pixels to
    /// mark them as blinking eyes: left half = eye 1, right half = eye 2, each on its own layer.</summary>
    public class FacePicker : AnimControl
    {
        public Img Face, Hat; public int[] EyeLayer = { 0, 0 }; public HashSet<int> Eyes = new HashSet<int>(); public event EventHandler Changed;
        public bool Numbered = true;
        const int Cell = 30; int hot = -1;
        public FacePicker() { Width = Height = Cell * 8 + 2; Cursor = Cursors.Hand; }
        public static int Side(int code) { return (code % Converter.Outer) / 8 < 4 ? 0 : 1; }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int k = Code(e); if (k != hot) { hot = k; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = -1; Invalidate(); }
        int Code(MouseEventArgs e) { int x = e.X / Cell, y = e.Y / Cell; return x < 0 || x > 7 || y < 1 || y > 6 ? -1 : x * 8 + y; }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            int k = Code(e); if (k < 0) return;
            // toggle: remove the pixel on either layer, or add it on its eye's layer
            if (!Eyes.Remove(k) & !Eyes.Remove(k + Converter.Outer)) Eyes.Add(k + EyeLayer[Side(k)] * Converter.Outer);
            Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; PaintParentBg(g); Theme.Hq(g);
            if (Face == null) return;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    var r = new Rectangle(x * Cell + 1, y * Cell + 1, Cell - 1, Cell - 1);
                    var fc = Face.Get(x, y); var hc = Hat != null ? Hat.Get(x, y) : new byte[4];
                    var col = hc[3] > 127 ? Color.FromArgb(hc[0], hc[1], hc[2]) : fc[3] == 0 ? Theme.Card : Color.FromArgb(fc[0], fc[1], fc[2]);
                    if (y < 1 || y > 6) col = Theme.Lerp(col, Theme.Bg, 0.65f);
                    g.SmoothingMode = SmoothingMode.None;
                    using (var b = new SolidBrush(col)) g.FillRectangle(b, r);
                    if (hc[3] > 127) using (var pn = new Pen(Color.FromArgb(70, 255, 255, 255))) g.DrawRectangle(pn, r.X, r.Y, r.Width - 1, r.Height - 1);  // outer-layer pixel
                    int k = x * 8 + y;
                    if (k == hot) using (var b = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillRectangle(b, r);
                    bool onFace = Eyes.Contains(k), onOuter = Eyes.Contains(k + Converter.Outer);
                    if (onFace || onOuter)
                    {
                        using (var pn = new Pen(onOuter ? Theme.Gold : Theme.Accent, 3)) g.DrawRectangle(pn, r.X + 2, r.Y + 2, r.Width - 5, r.Height - 5);
                        if (Numbered)
                        {
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            var badge = new RectangleF(r.Right - 15, r.Y - 3, 16, 16);
                            using (var b = new SolidBrush(onOuter ? Theme.Gold : Theme.Accent)) g.FillRectangle(b, badge);
                            string n = (Side(k) + 1).ToString();
                            using (var f = Theme.F(7.5f, FontStyle.Bold))
                            {
                                var sz = g.MeasureString(n, f);
                                using (var b = new SolidBrush(Theme.OnAccent)) g.DrawString(n, f, b, badge.X + (16 - sz.Width) / 2, badge.Y + (16 - sz.Height) / 2);
                            }
                        }
                    }
                }
        }
    }

    /// <summary>Two-option switch with a sliding highlight.</summary>
    public class Segmented : AnimControl
    {
        public string[] Options; public event EventHandler Picked;
        public int Index { get { return (int)Math.Round(extraT); } set { extraT = value; Invalidate(); } }
        public Segmented(params string[] o) { Options = o; Height = 34; Width = 260; Cursor = Cursors.Hand; }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = Math.Min(Options.Length - 1, e.X * Options.Length / Width);
            if (i != Index) { Index = i; if (Picked != null) Picked(this, EventArgs.Empty); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; PaintParentBg(g); Theme.Hq(g);
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            using (var path = Theme.Round(r, 9))
            {
                using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, path);
                using (var pn = new Pen(Theme.Lerp(Theme.Line, Theme.Dim, hover * 0.5f))) g.DrawPath(pn, path);
            }
            float w = r.Width / Options.Length;
            using (var path = Theme.Round(new RectangleF(r.X + 3 + extra * w, r.Y + 3 + press, w - 6, r.Height - 6), 7))
            using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
            for (int i = 0; i < Options.Length; i++)
            {
                var sz = g.MeasureString(Options[i], Theme.P(12.3f));
                float near = 1 - Math.Min(1, Math.Abs(extra - i));
                using (var b = new SolidBrush(Theme.Lerp(Theme.Text, Theme.OnAccent, near)))
                    g.DrawString(Options[i], Theme.P(12.3f), b, r.X + i * w + (w - sz.Width) / 2, r.Y + (r.Height - sz.Height) / 2);
            }
        }
    }

    /// <summary>Hero chooser showing each hero's original look.</summary>
    public class HeroPicker : AnimControl
    {
        public class Item { public Hero H; public bool Used; }
        public List<Item> Items = new List<Item>(); public Item Selected; public event EventHandler Picked;
        public HeroPicker() { Height = 56; Cursor = Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; PaintParentBg(g); Theme.Hq(g);
            var r = new RectangleF(1, 1 + 1.5f * press, Width - 3, Height - 4);
            using (var path = Theme.Round(r, 11))
            {
                using (var b = new SolidBrush(Theme.Lerp(Theme.Card, Theme.CardHi, hover))) g.FillPath(b, path);
                using (var p = new Pen(Theme.Lerp(Theme.Line, Theme.Accent, hover * 0.7f))) g.DrawPath(p, path);
            }
            if (Selected != null && Selected.H.Original != null) g.DrawImage(Selected.H.Original, r.X + 6, r.Y + 3, 46, 46);
            using (var b = new SolidBrush(Theme.Text)) g.DrawString(Selected != null ? Selected.H.Display : "Choose a hero", Theme.P(15.6f), b, r.X + 58, r.Y + 14);
            using (var b = new SolidBrush(Theme.Lerp(Theme.Dim, Theme.Text, hover)))
                g.FillPolygon(b, new[] { new PointF(Width - 30, r.Y + 22), new PointF(Width - 16, r.Y + 22), new PointF(Width - 23, r.Y + 30) });
        }
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), BackColor = Theme.Card, ForeColor = Theme.Text, Font = Theme.F(10.5f), ImageScalingSize = new Size(44, 44), MaximumSize = new Size(Width + 60, 600) };
            foreach (var it in Items)
            {
                var item = it;
                var mi = new ToolStripMenuItem(it.H.Display + (it.Used ? "     · has a custom skin" : ""))
                {
                    ForeColor = it.Used ? Theme.Dim : Theme.Text, Image = it.H.Original, ImageScaling = ToolStripItemImageScaling.SizeToFit,
                    Font = it == Selected ? Theme.F(10.5f, FontStyle.Bold) : Theme.F(10.5f)
                };
                mi.Click += (s, a) => { Selected = item; Invalidate(); if (Picked != null) Picked(this, EventArgs.Empty); };
                menu.Items.Add(mi);
            }
            menu.Show(this, new Point(0, Height));
        }
    }

    class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.ForeColor; base.OnRenderItemText(e); }
    }
    class DarkColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Theme.CardHi; } }
        public override Color MenuItemBorder { get { return Theme.Accent; } }
        public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
        public override Color MenuBorder { get { return Theme.Line; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Panel; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Panel; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Panel; } }
    }

    /// <summary>Pill that slides up at the bottom, then fades away.</summary>
    public class Toast : AnimControl
    {
        Timer life = new Timer { Interval = 4200 }; Color col = Theme.Accent;
        public Toast() { Visible = false; Height = 48; life.Tick += (s, e) => { life.Stop(); extraT = 0; }; }
        public void Show(string text, Color c)
        {
            Text = text; col = c; Visible = true; BringToFront(); extra = 0; extraT = 1; life.Stop(); life.Start(); Invalidate();
        }
        protected override void Step() { base.Step(); if (extraT == 0 && extra < 0.02f && Visible) Visible = false; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; PaintParentBg(g); Theme.Hq(g);
            float a = extra, dy = (1 - a) * 18;
            var r = new RectangleF(1, 1 + dy, Width - 3, Height - 3);
            using (var path = Theme.Round(r, 22))
            {
                using (var b = new SolidBrush(Color.FromArgb((int)(250 * a), Theme.Lerp(col, Color.Black, 0.6f)))) g.FillPath(b, path);
                using (var p = new Pen(Color.FromArgb((int)(255 * a), col), 1.6f)) g.DrawPath(p, path);
            }
            using (var b = new SolidBrush(Color.FromArgb((int)(255 * a), Theme.Text)))
            {
                var sz = g.MeasureString(Text, Theme.Semi(10.5f));
                g.DrawString(Text, Theme.Semi(10.5f), b, (Width - sz.Width) / 2, r.Y + (r.Height - sz.Height) / 2);
            }
        }
    }

    /// <summary>Pill switch whose knob slides over.</summary>
    public class ToggleSwitch : AnimControl
    {
        public event EventHandler Toggled;
        public bool On { get { return extraT > 0.5f; } set { extraT = value ? 1 : 0; Invalidate(); } }
        public ToggleSwitch() { Width = 46; Height = 26; Cursor = Cursors.Hand; }
        protected override void OnClick(EventArgs e) { base.OnClick(e); On = !On; if (Toggled != null) Toggled(this, e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; PaintParentBg(g); Theme.Hq(g);
            var r = new RectangleF(1, 2, Width - 3, Height - 5);
            using (var path = Theme.Round(r, r.Height / 2))
            {
                using (var b = new SolidBrush(Theme.Lerp(Theme.Lerp(Theme.Card, Theme.CardHi, hover), Theme.Accent, extra))) g.FillPath(b, path);
                using (var pen = new Pen(Theme.Lerp(Theme.Line, Theme.AccentDeep, extra))) g.DrawPath(pen, path);
            }
            float d = r.Height - 6 - 2 * press, x = r.X + 3 + extra * (r.Width - d - 6);
            using (var b = new SolidBrush(Theme.Text)) g.FillRectangle(b, (float)Math.Round(x), r.Y + 3 + press, d, d);
            using (var p = new Pen(Color.Black, 2)) g.DrawRectangle(p, (float)Math.Round(x), r.Y + 3 + press, d, d);
        }
    }

    /// <summary>Colour square that opens a colour picker.</summary>
    public class Swatch : AnimControl
    {
        public int Rgb; public event EventHandler ColorChanged;
        public Func<Img> SkinSource; public string Title = "Choose a colour";
        public Swatch() { Width = 44; Height = 30; Cursor = Cursors.Hand; }
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (!Enabled) return;
            Img skinImg = null;
            try { if (SkinSource != null) skinImg = SkinSource(); } catch { }
            using (var d = new ColorPickerDialog(Rgb, skinImg, Title))
                if (d.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    Rgb = d.Rgb; Invalidate();
                    if (ColorChanged != null) ColorChanged(this, EventArgs.Empty);
                }
        }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; PaintParentBg(g); Theme.Hq(g);
            var r = new RectangleF(1, 1 + 1.5f * press, Width - 3, Height - 4);
            var col = Color.FromArgb(Rgb >> 16 & 255, Rgb >> 8 & 255, Rgb & 255);
            if (!Enabled) col = Theme.Lerp(col, Theme.Bg, 0.75f);
            using (var path = Theme.Round(r, 7))
            {
                using (var b = new SolidBrush(col)) g.FillPath(b, path);
                using (var pen = new Pen(Theme.Lerp(Theme.Line, Theme.Text, hover), 1.5f)) g.DrawPath(pen, path);
            }
        }
    }

    /// <summary>Right-aligned status line: coloured dots between the parts, text in blue.</summary>
    public class StatusLine : Control
    {
        public string[] Parts = new string[0]; public Color Dot = Color.FromArgb(88, 206, 110), Ink = Theme.Accent;
        public StatusLine() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
        public void Set(Color dot, params string[] parts) { Dot = dot; Parts = parts; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            // build: ● part · part   (measured right to left so it hugs the right edge)
            var pieces = new List<Tuple<string, Color>>();
            pieces.Add(Tuple.Create("■", Dot));
            for (int i = 0; i < Parts.Length; i++)
            {
                if (i > 0) pieces.Add(Tuple.Create("■", Dot));
                pieces.Add(Tuple.Create(Parts[i], Ink));
            }
            float x = Width;
            var widths = pieces.Select(p => g.MeasureString(p.Item1, Font).Width + 6).ToList();
            x -= widths.Sum();
            for (int i = 0; i < pieces.Count; i++)
            {
                bool dot = pieces[i].Item1 == "■";
                using (var f = dot ? new Font(Font.FontFamily, Font.Size * 0.6f) : null)
                using (var b = new SolidBrush(pieces[i].Item2))
                {
                    var font = f ?? Font; var sz = g.MeasureString(pieces[i].Item1, font);
                    g.DrawString(pieces[i].Item1, font, b, x + (dot ? (widths[i] - sz.Width) / 2 : 0), (Height - sz.Height) / 2);
                }
                x += widths[i];
            }
        }
    }

    public class Section : Label
    {
        public Section(string t) { Text = t.ToUpperInvariant(); Font = Theme.P(13f); ForeColor = Theme.Dim; AutoSize = true; UseCompatibleTextRendering = true; }
    }

    class DoubleBufferedPanel : Panel { public DoubleBufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; } }

    // Hosts a scrolling panel but clips off its bright native scrollbars; the wheel still scrolls.
    class ClipHost : DoubleBufferedPanel
    {
        public readonly Panel Inner;
        public ClipHost(Panel inner, Color bg)
        {
            Inner = inner; BackColor = bg; inner.AutoScroll = true; Controls.Add(inner);
            Resize += (s, e) => inner.Bounds = new Rectangle(0, 0, Width + SystemInformation.VerticalScrollBarWidth, Height + SystemInformation.HorizontalScrollBarHeight);
        }
    }

    // ================================================================ window
    public class MainForm : Form
    {
        GameData gd; string gameDir;
        List<SkinSlot> slots = new List<SkinSlot>(); bool layers = true; int current = -1;
        Dictionary<SkinSlot, Img> texCache = new Dictionary<SkinSlot, Img>();
        Dictionary<SkinSlot, Bitmap> thumbCache = new Dictionary<SkinSlot, Bitmap>();

        Panel listPanel, editor, header; List<SlotCard> cards = new List<SlotCard>();
        SwapBanner banner; HeroPicker heroPicker; OptionCard[] modeCards; FacePicker picker; Panel eyePanel, gamePanel; Label eyeInfo;
        ToggleSwitch[] gfOn; Swatch[] gfCol; int partsTop; Segmented[] eyeSeg; Label[] eyeLbl; ToggleSwitch lidOn, lidSplit; Swatch lidCol, lidCol2; Panel lidRow; Label lidLbl, lidLbl2;
        Label fileLabel, emptyHint; StatusLine statusPill; PictureBox flatTex; CheckBox layersBox; Toast toast; bool loading;

        public MainForm()
        {
            Text = App.Name + "  -  by " + App.Author; BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.F(9.5f);
            ClientSize = new Size(1300, 840); MinimumSize = new Size(1140, 780); StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            DoubleBuffered = true; AllowDrop = true;
            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) => { foreach (var f in (string[])e.Data.GetData(DataFormats.FileDrop)) AddSkin(f); };
            gd = App.LoadData();
            BuildUi();
            LoadConfig();
            DetectGame(Game.Find());
            RebuildCards(); Select(slots.Count > 0 ? 0 : -1);
        }

        SkinSlot Current { get { return current >= 0 && current < slots.Count ? slots[current] : null; } }

        // ------------------------------------------------------------------ UI
        void BuildUi()
        {
            header = new DoubleBufferedPanel { Dock = DockStyle.Top, Height = 92, BackColor = Theme.Panel };
            header.Paint += (s, e) =>
            {
                var g = e.Graphics; Theme.Hq(g);
                using (var b = new LinearGradientBrush(header.ClientRectangle, Color.FromArgb(16, 40, 86), Theme.Panel, 0f)) g.FillRectangle(b, header.ClientRectangle);
                if (Icon != null) using (var ic = new Icon(Icon, 64, 64)) g.DrawIcon(ic, new Rectangle(22, 18, 56, 56));
                using (var b = new SolidBrush(Theme.Text)) g.DrawString(App.Name, Theme.P(27.3f), b, 88, 8);
                // author credit: gold, with a dark drop shadow so it stands out
                var author = "by " + App.Author;
                using (var b = new SolidBrush(Color.Black)) g.DrawString(author, Theme.P(19f), b, 93, 49);
                using (var b = new SolidBrush(Theme.Gold)) g.DrawString(author, Theme.P(19f), b, 91, 47);
                var sz = g.MeasureString(author, Theme.P(19f));
                using (var b = new SolidBrush(Theme.Dim)) g.DrawString("·   your own Minecraft skins on the heroes   ·   cosmetic only", Theme.F(9.5f), b, 93 + sz.Width + 4, 55);
                using (var p = new Pen(Theme.Line)) g.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1);
            };
            statusPill = new StatusLine { Height = 30, Width = 420, Font = Theme.P(13.7f), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            var help = MakeLink("How it works", ShowHelp); var change = MakeLink("Change game folder", PickGameFolder);
            header.Controls.AddRange(new Control[] { statusPill, help, change });
            header.Resize += (s, e) =>
            {
                statusPill.Location = new Point(header.Width - statusPill.Width - 24, 16);
                change.Location = new Point(header.Width - change.Width - 24, 56); help.Location = new Point(change.Left - help.Width - 18, 56);
            };

            // bottom bar
            var bottom = new DoubleBufferedPanel { Dock = DockStyle.Bottom, Height = 86, BackColor = Theme.Panel };
            bottom.Paint += (s, e) => { using (var p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, 0, 0, bottom.Width, 0); };
            layersBox = new CheckBox { Text = "3D outer layers (jacket, sleeves, pants)", AutoSize = true, Checked = true, ForeColor = Theme.Text, Location = new Point(26, 22), Font = Theme.F(10f), Cursor = Cursors.Hand };
            layersBox.CheckedChanged += (s, e) => { if (loading) return; layers = layersBox.Checked; thumbCache.Clear(); SaveConfig(); RefreshCards(); banner.Refresh3D(); };
            var hint = new Label { Text = "Applies to every hero; skins without outer-layer pixels look the same.", AutoSize = true, ForeColor = Theme.Dim, Location = new Point(45, 48), Font = Theme.F(8.5f) };
            var install = new TactileButton("Install to game", BtnKind.Primary, "⬇") { Width = 250, Height = 54, Anchor = AnchorStyles.Right | AnchorStyles.Top };
            var remove = new TactileButton("Uninstall mod", BtnKind.Secondary) { Width = 180, Height = 54, Anchor = AnchorStyles.Right | AnchorStyles.Top };
            var export = new TactileButton("Export for a friend", BtnKind.Secondary, "⇪") { Width = 246, Height = 54, Anchor = AnchorStyles.Right | AnchorStyles.Top };
            foreach (var b in new[] { install, remove, export }) b.BackColor = Theme.Panel;
            install.Click += (s, e) => Install(); remove.Click += (s, e) => RemoveMod(); export.Click += (s, e) => Export();
            bottom.Controls.AddRange(new Control[] { layersBox, hint, install, remove, export });
            bottom.Resize += (s, e) =>
            {
                install.Location = new Point(bottom.Width - 274, 16); remove.Location = new Point(install.Left - 192, 16); export.Location = new Point(remove.Left - 258, 16);
            };

            // left: skin list
            var left = new DoubleBufferedPanel { Dock = DockStyle.Left, Width = 380, BackColor = Theme.Panel };
            left.Paint += (s, e) => { using (var p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, left.Width - 1, 0, left.Width - 1, left.Height); };
            var lt = new Section("Your skins") { Location = new Point(22, 18) };
            var lt2 = new Label { Text = "game's hero  →  your skin", AutoSize = true, ForeColor = Theme.Dim, Location = new Point(160, 20), Font = Theme.F(8.5f) };
            listPanel = new DoubleBufferedPanel { BackColor = Theme.Panel };
            var listHost = new ClipHost(listPanel, Theme.Panel) { Location = new Point(12, 42) };
            var add = new TactileButton("Add skin", BtnKind.Primary, "+") { Height = 52 };
            var del = new TactileButton("Remove", BtnKind.Secondary) { Width = 112, Height = 52 };
            foreach (var b in new[] { add, del }) b.BackColor = Theme.Panel;
            add.Click += (s, e) => BrowseAdd(); del.Click += (s, e) => RemoveSlot();
            emptyHint = new Label { Text = "No skins yet.\r\n\r\nClick \"Add skin\" or drop skin\r\nPNG files anywhere on this window.", ForeColor = Theme.Dim, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Font = Theme.F(10.5f) };
            left.Controls.AddRange(new Control[] { lt, lt2, emptyHint, listHost, add, del });
            left.Resize += (s, e) =>
            {
                listHost.Size = new Size(left.Width - 20, left.Height - 42 - 80);
                add.Location = new Point(18, left.Height - 68); add.Width = left.Width - 36 - 124; del.Location = new Point(left.Width - 18 - 112, left.Height - 68);
                emptyHint.Bounds = new Rectangle(12, 120, left.Width - 24, 140);
                foreach (var c in cards) c.Width = listHost.Width - 4;
            };

            // right: editor
            editor = new DoubleBufferedPanel { BackColor = Theme.Bg };
            var editorHost = new ClipHost(editor, Theme.Bg) { Dock = DockStyle.Fill };
            int y = 22, X = 30;
            banner = new SwapBanner { Location = new Point(X, y), BackColor = Theme.Bg };
            banner.RenderCustom = (size, yaw) =>
            {
                var s = Current; var t = s != null ? Tex(s) : null; if (t == null) return null;
                return Renderer.RenderIcon(gd.IconGeo[layers ? 0 : 1], t, size, gd.IconCam, yaw + 22, 2).ToBitmap();
            };
            editor.Controls.Add(banner); y += banner.Height + 24;

            editor.Controls.Add(new Section("Skin image") { Location = new Point(X, y) }); y += 26;
            flatTex = new PictureBox { Location = new Point(X, y), Size = new Size(112, 112), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Card };
            var choose = new TactileButton("Choose PNG…", BtnKind.Secondary) { Location = new Point(X + 130, y), Width = 180, BackColor = Theme.Bg };
            choose.Click += (s, e) => ChangeImage();
            fileLabel = new Label { Location = new Point(X + 130, y + 56), AutoSize = false, Width = 380, Height = 56, ForeColor = Theme.Dim };
            editor.Controls.AddRange(new Control[] { flatTex, choose, fileLabel }); y += 134;

            editor.Controls.Add(new Section("Hero to replace") { Location = new Point(X, y) }); y += 26;
            heroPicker = new HeroPicker { Location = new Point(X, y), Width = 380, BackColor = Theme.Bg };
            heroPicker.Picked += (s, e) =>
            {
                var cur = Current; if (loading || cur == null || heroPicker.Selected == null) return;
                var key = heroPicker.Selected.H.Key;
                var other = slots.FirstOrDefault(o => o != cur && o.HeroKey == key);
                if (other != null) other.HeroKey = cur.HeroKey;       // swap the two heroes
                cur.HeroKey = key; Changed();
            };
            var heroHint = new Label { Location = new Point(X + 396, y + 10), AutoSize = false, Width = 260, Height = 40, ForeColor = Theme.Dim, Text = "In the game, pick this hero in the Locker to wear your skin." };
            editor.Controls.AddRange(new Control[] { heroPicker, heroHint }); y += 78;

            editor.Controls.Add(new Section("Face") { Location = new Point(X, y) }); y += 26;
            modeCards = new[] {
                new OptionCard("As drawn", "Your face exactly as in the skin. No animation."),
                new OptionCard("Game face", "The game's own animated eyes and brows (blinking, looking around), coloured from your skin."),
                new OptionCard("Blinking eyes", "Your own eye pixels blink in place, without moving. Choose them below.") };
            for (int i = 0; i < 3; i++)
            {
                var c = modeCards[i]; int mode = i; c.Location = new Point(X, y); c.BackColor = Theme.Bg;
                c.Picked += (s, e) =>
                {
                    var cur = Current; if (cur == null) return;
                    cur.Mode = (FaceMode)mode;
                    if ((cur.Mode == FaceMode.Blink || cur.Mode == FaceMode.Game) && cur.Eyes.Count == 0) AutoEyes(cur.Mode == FaceMode.Blink);
                    Changed();
                };
                editor.Controls.Add(c); y += 82;
            }
            partsTop = y + 6;
            gamePanel = new Panel { Location = new Point(X, partsTop), Size = new Size(600, 176), BackColor = Theme.Bg };
            var gTitle = new Label { Text = "The game draws animated eyes and brows over the face. Both eyes always share one colour. Switch parts off or click a colour to change it.", Location = new Point(0, 0), Size = new Size(600, 38), ForeColor = Theme.Dim };
            gamePanel.Controls.Add(gTitle);
            string[] parts = { "Eyes (irises)", "Eye whites", "Eyebrows" };
            gfOn = new ToggleSwitch[3]; gfCol = new Swatch[3];
            for (int i = 0; i < 3; i++)
            {
                int k = i, ry = 44 + i * 38;
                gfOn[i] = new ToggleSwitch { Location = new Point(0, ry), BackColor = Theme.Bg };
                gfCol[i] = new Swatch { Location = new Point(58, ry - 2), BackColor = Theme.Bg };
                var lbl = new Label { Text = parts[i], Location = new Point(114, ry + 3), AutoSize = true, ForeColor = Theme.Text, Font = Theme.F(10f) };
                gfOn[i].Toggled += (s, e) => GameFaceEdited(k);
                gfCol[i].ColorChanged += (s, e) => { gfOn[k].On = true; GameFaceEdited(k); };
                gamePanel.Controls.AddRange(new Control[] { gfOn[i], gfCol[i], lbl });
            }
            var autoCols = new TactileButton("Auto colours", BtnKind.Secondary) { Location = new Point(300, 44), Width = 150, BackColor = Theme.Bg };
            autoCols.Click += (s, e) => { var cur = Current; if (cur == null) return; cur.Face = null; Changed(); };
            gamePanel.Controls.Add(autoCols);
            for (int i = 0; i < 3; i++) { gfCol[i].SkinSource = CurrentSkinImage; gfCol[i].Title = "Colour: " + parts[i]; }
            editor.Controls.Add(gamePanel);
            eyePanel = new Panel { Location = new Point(X, partsTop), Size = new Size(600, 290), BackColor = Theme.Bg };
            picker = new FacePicker { Location = new Point(0, 0), BackColor = Theme.Bg };
            picker.Changed += (s, e) => { var cur = Current; if (cur == null) return; cur.Eyes = picker.Eyes.OrderBy(v => v).ToList(); Changed(); };
            eyeSeg = new Segmented[2]; eyeLbl = new Label[2];
            for (int i = 0; i < 2; i++)
            {
                int side = i;
                eyeLbl[i] = new Label { Text = i == 0 ? "Eye 1 (left)" : "Eye 2 (right)", Location = new Point(264, 64 + i * 42 + 8), AutoSize = true, ForeColor = Theme.Text, Font = Theme.P(12.3f), UseCompatibleTextRendering = true };
                eyeSeg[i] = new Segmented("Face layer", "Outer layer") { Location = new Point(370, 64 + i * 42), Width = 230, BackColor = Theme.Bg };
                eyeSeg[i].Picked += (s, e) =>
                {
                    var cur = Current; if (cur == null) return;
                    picker.EyeLayer[side] = eyeSeg[side].Index;
                    // move this eye's pixels to the chosen layer
                    cur.Eyes = cur.Eyes.Select(v => FacePicker.Side(v) == side ? v % Converter.Outer + eyeSeg[side].Index * Converter.Outer : v).Distinct().OrderBy(v => v).ToList();
                    Changed();
                };
            }
            lidRow = new Panel { Location = new Point(264, 152), Size = new Size(360, 72), BackColor = Theme.Bg };
            lidOn = new ToggleSwitch { Location = new Point(0, 4), BackColor = Theme.Bg };
            lidCol = new Swatch { Location = new Point(56, 2), BackColor = Theme.Bg };
            lidLbl = new Label { Text = "Eyelid colour (what the eye closes to)", Location = new Point(108, 8), AutoSize = true, ForeColor = Theme.Text };
            lidSplit = new ToggleSwitch { Location = new Point(0, 42), BackColor = Theme.Bg };
            lidCol2 = new Swatch { Location = new Point(56, 40), BackColor = Theme.Bg };
            lidLbl2 = new Label { Text = "Different colour for each eye", Location = new Point(108, 46), AutoSize = true, ForeColor = Theme.Text };
            Action applyLid = () =>
            {
                var cur = Current; if (cur == null || loading) return;
                cur.LidColor = lidOn.On ? (int?)lidCol.Rgb : null;
                cur.LidColor2 = lidOn.On && lidSplit.On ? (int?)lidCol2.Rgb : null;
                Changed();
            };
            lidOn.Toggled += (s, e) => applyLid();
            lidSplit.Toggled += (s, e) => { if (lidSplit.On) lidOn.On = true; applyLid(); };
            lidCol.ColorChanged += (s, e) => { lidOn.On = true; applyLid(); };
            lidCol2.ColorChanged += (s, e) => { lidOn.On = true; lidSplit.On = true; applyLid(); };
            lidRow.Controls.AddRange(new Control[] { lidOn, lidCol, lidLbl, lidSplit, lidCol2, lidLbl2 });
            lidCol.SkinSource = lidCol2.SkinSource = CurrentSkinImage;
            lidCol.Title = "Eyelid colour"; lidCol2.Title = "Eyelid colour, eye 2 (right)";
            eyeInfo = new Label { Location = new Point(264, 0), Width = 320, Height = 60, ForeColor = Theme.Dim,
                Text = "Click the face pixels that are the eyes (they get a green frame). Each chosen pixel squashes shut on the game's blink, then reopens.\r\n\r\nThe dimmed top and bottom rows can't blink." };
            var auto = new TactileButton("Auto-detect", BtnKind.Secondary) { Location = new Point(264, 232), Width = 144, BackColor = Theme.Bg };
            var clear = new TactileButton("Clear", BtnKind.Secondary) { Location = new Point(420, 232), Width = 100, BackColor = Theme.Bg };
            auto.Click += (s, e) => { AutoEyes(); Changed(); };
            clear.Click += (s, e) => { var cur = Current; if (cur == null) return; cur.Eyes.Clear(); Changed(); };
            eyePanel.Controls.AddRange(new Control[] { picker, eyeInfo, eyeSeg[0], eyeSeg[1], eyeLbl[0], eyeLbl[1], lidRow, auto, clear });
            editor.Controls.Add(eyePanel);
            editor.Controls.Add(new Label { Location = new Point(X, y + 520), Height = 16, Width = 4, BackColor = Theme.Bg });   // bottom breathing room
            editorHost.Resize += (s, e) =>
            {
                int w = Math.Max(560, editorHost.Width - 2 * X);
                banner.Width = w; foreach (var mc in modeCards) mc.Width = w; eyePanel.Width = w; eyeInfo.Width = Math.Max(200, w - 272);
                gamePanel.Width = w; gTitle.Width = w;
                heroHint.Width = Math.Max(160, w - 400);
            };

            toast = new Toast { Width = 620, BackColor = Theme.Bg };

            Controls.Add(toast); Controls.Add(editorHost); Controls.Add(left); Controls.Add(bottom); Controls.Add(header);
            EventHandler place = (s, e) => toast.Location = new Point(380 + (ClientSize.Width - 380 - toast.Width) / 2, ClientSize.Height - 86 - toast.Height - 16);
            Resize += place; place(null, null);
        }

        LinkLabel MakeLink(string t, Action a)
        {
            var l = new LinkLabel { Text = t, AutoSize = true, LinkColor = Theme.Blue, ActiveLinkColor = Theme.Text, LinkBehavior = LinkBehavior.HoverUnderline, Font = Theme.F(9.5f), BackColor = Color.Transparent, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            l.LinkClicked += (s, e) => a(); return l;
        }

        // ------------------------------------------------------------ rendering
        string SkinName(SkinSlot s) { var m = s.ImagePath + ".name"; return File.Exists(m) ? File.ReadAllText(m) : Path.GetFileName(s.ImagePath); }
        static string ModeText(FaceMode m) { return m == FaceMode.Blink ? "blinking eyes" : m == FaceMode.Game ? "game face" : "face as drawn"; }

        Img Tex(SkinSlot s)
        {
            Img t;
            if (!texCache.TryGetValue(s, out t))
            {
                try { t = Converter.Convert(gd, Img.FromFile(s.ImagePath), s.Mode, s.Eyes, s.Face, s.LidColor, s.LidColor2); } catch { t = null; }
                texCache[s] = t;
            }
            return t;
        }
        Bitmap Thumb(SkinSlot s)
        {
            Bitmap b;
            if (!thumbCache.TryGetValue(s, out b))
            {
                var t = Tex(s);
                b = t == null ? null : Renderer.RenderIcon(gd.IconGeo[layers ? 0 : 1], t, 160, gd.IconCam, 0, 2).ToBitmap();
                thumbCache[s] = b;
            }
            return b;
        }

        // -------------------------------------------------------------- list
        void RebuildCards()
        {
            listPanel.SuspendLayout();
            foreach (var c in cards) c.Dispose();
            cards.Clear(); listPanel.Controls.Clear();
            for (int i = 0; i < slots.Count; i++)
            {
                int idx = i;
                var c = new SlotCard { Slot = slots[i], Width = listPanel.Parent.Width - 4, Location = new Point(0, i * 138), BackColor = Theme.Panel };
                c.Click += (s, e) => Select(idx);
                cards.Add(c); listPanel.Controls.Add(c);
            }
            listPanel.ResumeLayout();
            RefreshCards();
            emptyHint.Visible = slots.Count == 0;
        }
        void RefreshCards()
        {
            foreach (var c in cards)
            {
                c.Hero = gd.FindHero(c.Slot.HeroKey); c.Custom = Thumb(c.Slot); c.ModeText = ModeText(c.Slot.Mode); c.FileName = SkinName(c.Slot);
                c.Selected = c.Slot == Current; c.Invalidate();
            }
        }

        void Select(int i)
        {
            loading = true;
            current = i;
            var s = Current;
            editor.Parent.Visible = s != null;
            if (s != null)
            {
                try { flatTex.Image = Img.FromFile(s.ImagePath).ToBitmap(); } catch { flatTex.Image = null; }
                fileLabel.Text = SkinName(s) + "\r\nAny Minecraft skin works: 64x64 or old 64x32, classic or slim arms.";
                heroPicker.Items.Clear(); heroPicker.Selected = null;
                foreach (var h in gd.Heroes.OrderBy(h => h.Name.StartsWith("_")).ThenBy(h => h.Name).ThenBy(h => h.Deluxe))
                {
                    var it = new HeroPicker.Item { H = h, Used = slots.Any(o => o != s && o.HeroKey == h.Key) };
                    heroPicker.Items.Add(it); if (h.Key == s.HeroKey) heroPicker.Selected = it;
                }
                heroPicker.Invalidate();
                for (int k = 0; k < 3; k++) modeCards[k].Checked = (int)s.Mode == k;
                gamePanel.Visible = s.Mode == FaceMode.Game;
                eyePanel.Visible = s.Mode == FaceMode.Blink || s.Mode == FaceMode.Game;
                eyePanel.Top = partsTop + (s.Mode == FaceMode.Game ? gamePanel.Height + 8 : 0) + editor.AutoScrollPosition.Y;
                gamePanel.Top = partsTop + editor.AutoScrollPosition.Y;
                eyeInfo.Text = s.Mode == FaceMode.Game
                    ? "Your own drawn eyes (blue frames) are hidden, so only the game's eyes show. Click pixels to add or remove them."
                    : "Click the eye pixels: left half = eye 1, right half = eye 2. Pick each eye's layer: face (blue) or the outer hat layer (gold).";
                if (s.Mode == FaceMode.Game)
                {
                    var gf = s.Face ?? GameFace.Auto(Converter.To64(Img.FromFile(s.ImagePath)).Crop(8, 8, 8, 8), s.Eyes);
                    var vals = new[] { gf.Iris, gf.White, gf.Brow };
                    var auto = GameFace.Auto(Converter.To64(Img.FromFile(s.ImagePath)).Crop(8, 8, 8, 8), s.Eyes);
                    var fallback = new[] { auto.Iris ?? 0x1E1E1E, auto.White ?? 0xF4F4F4, auto.Brow ?? 0x3A2A20 };
                    for (int k = 0; k < 3; k++) { gfOn[k].On = vals[k].HasValue; gfCol[k].Rgb = vals[k] ?? fallback[k]; gfCol[k].Enabled = vals[k].HasValue; gfCol[k].Invalidate(); }
                }
                try { var im64 = Converter.To64(Img.FromFile(s.ImagePath)); picker.Face = im64.Crop(8, 8, 8, 8); picker.Hat = im64.Crop(40, 8, 8, 8); }
                catch { picker.Face = null; picker.Hat = null; }
                bool blinkUi = s.Mode == FaceMode.Blink;
                lidRow.Visible = eyeSeg[0].Visible = eyeSeg[1].Visible = eyeLbl[0].Visible = eyeLbl[1].Visible = blinkUi;
                picker.Numbered = blinkUi;
                for (int side = 0; side < 2; side++)
                {
                    int sd = side;
                    var mine = s.Eyes.Where(v => FacePicker.Side(v) == sd).ToList();
                    if (mine.Count > 0) picker.EyeLayer[sd] = mine[0] >= Converter.Outer ? 1 : 0;
                    else if (current != lastLayerSlot) picker.EyeLayer[sd] = 0;
                    eyeSeg[sd].Index = picker.EyeLayer[sd];
                }
                lastLayerSlot = current;
                lidOn.On = s.LidColor.HasValue;
                lidSplit.On = s.LidColor2.HasValue;
                lidCol.Rgb = s.LidColor ?? AutoLid(s, 0); lidCol.Invalidate();
                lidCol2.Rgb = s.LidColor2 ?? s.LidColor ?? AutoLid(s, 1); lidCol2.Invalidate();
                lidLbl.Text = lidSplit.On ? "Eyelid colour, eye 1 (left)" : "Eyelid colour (what the eye closes to)";
                lidLbl2.Text = lidSplit.On ? "Eyelid colour, eye 2 (right)" : "Different colour for each eye";
                lidCol2.Visible = lidSplit.On;
                lidLbl2.Left = lidSplit.On ? 108 : 56;
                picker.Eyes = new HashSet<int>(s.Eyes); picker.Invalidate();
                banner.Hero = gd.FindHero(s.HeroKey); banner.Refresh3D();
            }
            loading = false;
            RefreshCards();
        }

        int lastLayerSlot = -2;

        /// <summary>The selected skin as a 64x64 Minecraft skin (for the colour picker's eyedropper).</summary>
        Img CurrentSkinImage()
        {
            var s = Current; if (s == null) return null;
            return Converter.To64(Img.FromFile(s.ImagePath));
        }

        int AutoLid(SkinSlot s, int side = -1)
        {
            // the colour next to the first eye on its layer: what the eye closes to by default
            try
            {
                var im = Converter.To64(Img.FromFile(s.ImagePath));
                foreach (var e in s.Eyes.Where(v => side < 0 || FacePicker.Side(v) == side))
                {
                    int bx = e >= Converter.Outer ? 40 : 8, k = e % Converter.Outer, col = k / 8, row = k % 8;
                    foreach (var dx in new[] { 1, -1, 2, -2 })
                    {
                        int x = col + dx; if (x < 0 || x > 7 || s.Eyes.Contains(e - k + x * 8 + row)) continue;
                        var c = im.Get(bx + x, 8 + row); if (c[3] != 0) return (c[0] << 16) | (c[1] << 8) | c[2];
                    }
                }
                var f = im.Get(8 + 3, 8 + 4); return (f[0] << 16) | (f[1] << 8) | f[2];
            }
            catch { return 0xC89070; }
        }

        void GameFaceEdited(int k)
        {
            var s = Current; if (s == null || loading) return;
            var gf = new GameFace();
            Func<int, int?> v = i => gfOn[i].On ? (int?)gfCol[i].Rgb : null;
            gf.Iris = v(0); gf.White = v(1); gf.Brow = v(2); gf.Mouth = null;
            s.Face = gf; Changed();
        }

        void Changed()
        {
            if (Current == null) return;
            texCache.Clear(); thumbCache.Clear();
            SaveConfig(); Select(current);
        }

        // -------------------------------------------------------------- slots
        static readonly string[] HeroOrder = { "Darian", "Eshe", "Greta", "Javier", "Nuru", "Violet", "Qamar", "Esperanza", "Healer", "Ranger", "Tank", "Valorie", "PizzaChef", "Steve", "Alex" };

        void BrowseAdd()
        {
            using (var d = new OpenFileDialog { Filter = "Minecraft skin (*.png)|*.png", Multiselect = true, Title = "Choose Minecraft skin PNG files" })
                if (d.ShowDialog(this) == DialogResult.OK) foreach (var f in d.FileNames) AddSkin(f);
        }

        void AddSkin(string file)
        {
            Img im;
            try { im = Converter.To64(Img.FromFile(file)); }
            catch (Exception ex) { Msg("That file can't be used as a skin:\r\n\r\n" + ex.Message, true); return; }
            var used = new HashSet<string>(slots.Select(s => s.HeroKey));
            var free = HeroOrder.Select(n => n + "|0").Concat(gd.Heroes.Select(h => h.Key)).FirstOrDefault(k => !used.Contains(k) && gd.FindHero(k) != null);
            if (free == null) { Msg("Every hero already has a custom skin. Remove one first.", true); return; }
            var dir = Path.Combine(App.DataDir, "skins"); Directory.CreateDirectory(dir);
            var dst = Path.Combine(dir, Guid.NewGuid().ToString("N").Substring(0, 12) + ".png");
            File.Copy(file, dst); File.WriteAllText(dst + ".name", Path.GetFileName(file));
            var slot = new SkinSlot { HeroKey = free, ImagePath = dst };
            var eyes = Converter.DetectEyes(im);
            if (eyes.Count > 0) { slot.Mode = FaceMode.Blink; slot.Eyes = eyes; }
            slots.Add(slot); SaveConfig(); RebuildCards(); Select(slots.Count - 1);
            listPanel.ScrollControlIntoView(cards[cards.Count - 1]);
            toast.Show("Added " + Path.GetFileName(file) + " as " + gd.FindHero(free).Display + ". Install when you're ready!", Theme.Blue);
        }

        void ChangeImage()
        {
            var s = Current; if (s == null) return;
            using (var d = new OpenFileDialog { Filter = "Minecraft skin (*.png)|*.png", Title = "Choose a Minecraft skin PNG" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { Converter.To64(Img.FromFile(d.FileName)); }
                catch (Exception ex) { Msg("That file can't be used as a skin:\r\n\r\n" + ex.Message, true); return; }
                File.Copy(d.FileName, s.ImagePath, true); File.WriteAllText(s.ImagePath + ".name", Path.GetFileName(d.FileName));
                s.Eyes.Clear(); if (s.Mode == FaceMode.Blink) AutoEyes();
                Changed();
            }
        }

        void AutoEyes(bool warn = true)
        {
            var s = Current; if (s == null) return;
            try { s.Eyes = Converter.DetectEyes(Converter.To64(Img.FromFile(s.ImagePath))); } catch { }
            if (s.Eyes.Count == 0 && warn) toast.Show("Couldn't spot the eyes automatically. Click them on the face grid.", Theme.Warn);
        }

        void RemoveSlot()
        {
            var s = Current; if (s == null) return;
            slots.Remove(s); texCache.Remove(s); thumbCache.Remove(s);
            try { File.Delete(s.ImagePath); File.Delete(s.ImagePath + ".name"); } catch { }
            SaveConfig(); RebuildCards(); Select(Math.Min(current, slots.Count - 1));
            toast.Show("Skin removed. Click \"Install to game\" to apply.", Theme.Dim);
        }

        // -------------------------------------------------------------- config
        string ConfigPath { get { return Path.Combine(App.DataDir, "config.txt"); } }

        void SaveConfig()
        {
            var lines = new List<string> { "layers=" + (layers ? 1 : 0) };
            foreach (var s in slots)
                lines.Add(string.Join("|", s.HeroKey, s.ImagePath, s.Mode.ToString(), string.Join(" ", s.Eyes.Select(e => (e / 8) + "." + (e % 8))), s.Face != null ? s.Face.Serialize() : "", s.LidColor.HasValue ? "lid=" + s.LidColor.Value.ToString("x6") + (s.LidColor2.HasValue ? "," + s.LidColor2.Value.ToString("x6") : "") : ""));
            File.WriteAllLines(ConfigPath, lines);
            File.WriteAllText(Path.Combine(App.DataDir, "gamefolder.txt"), gameDir ?? "");
        }

        void LoadConfig()
        {
            loading = true;
            if (File.Exists(ConfigPath))
                foreach (var l in File.ReadAllLines(ConfigPath))
                {
                    if (l.StartsWith("layers=")) { layers = l.EndsWith("1"); continue; }
                    var p = l.Split('|'); if (p.Length < 5 || !File.Exists(p[2])) continue;
                    var s = new SkinSlot { HeroKey = p[0] + "|" + p[1], ImagePath = p[2] };
                    FaceMode m; if (Enum.TryParse(p[3], out m)) s.Mode = m;
                    if (p.Length > 5) s.Face = GameFace.Parse(p[5]);
                    if (p.Length > 6 && p[6].StartsWith("lid="))
                    {
                        var lc = p[6].Substring(4).Split(',');
                        s.LidColor = Convert.ToInt32(lc[0], 16);
                        if (lc.Length > 1) s.LidColor2 = Convert.ToInt32(lc[1], 16);
                    }
                    if (p[4].Trim() != "") s.Eyes = p[4].Split(' ').Select(e => int.Parse(e.Split('.')[0]) * 8 + int.Parse(e.Split('.')[1])).ToList();
                    if (gd.FindHero(s.HeroKey) != null) slots.Add(s);
                }
            layersBox.Checked = layers;
            var gf = Path.Combine(App.DataDir, "gamefolder.txt");
            if (File.Exists(gf)) { var g = File.ReadAllText(gf).Trim(); if (g != "" && Game.IsGame(g)) gameDir = g; }
            loading = false;
        }

        // ---------------------------------------------------------------- game
        void DetectGame(string found)
        {
            if (gameDir == null) gameDir = found;
            var green = Color.FromArgb(88, 206, 110);
            if (gameDir == null) statusPill.Set(Theme.Bad, "Game not found", "set the folder below");
            else if (!Game.VersionMatches(gd, gameDir)) statusPill.Set(Theme.Warn, "Different game version");
            else
            {
                string ver; var st = Game.Detect(gameDir, out ver);
                if (st == Game.State.Current) statusPill.Set(green, "Game found", "skins installed");
                else if (st == Game.State.Old) statusPill.Set(Theme.Warn, "Old version installed", "install to update");
                else statusPill.Set(green, "Game found", "ready");
            }
            SaveConfig();
        }

        void PickGameFolder()
        {
            using (var d = new FolderBrowserDialog { Description = "Select the \"Minecraft Dungeons II\" folder (the one that contains Dungeons.exe)." })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (!Game.IsGame(d.SelectedPath)) { Msg("That folder doesn't look like Minecraft Dungeons II. Pick the folder that contains Dungeons.exe.", true); return; }
                gameDir = d.SelectedPath; DetectGame(null);
            }
        }

        bool ReadyForGame()
        {
            if (gameDir == null) { Msg("Minecraft Dungeons II wasn't found. Use \"Change game folder\" at the top right.", true); return false; }
            if (Game.Running()) { Msg("Close Minecraft Dungeons II first, then try again.", true); return false; }
            return true;
        }

        Dictionary<string, byte[]> BuildMod()
        {
            Cursor = Cursors.WaitCursor;
            try { return ModBuilder.Build(gd, slots, layers, null); }
            finally { Cursor = Cursors.Default; }
        }

        void Install()
        {
            if (slots.Count == 0) { Msg("Add at least one skin first.", true); return; }
            if (!ReadyForGame()) return;
            if (!Game.VersionMatches(gd, gameDir) &&
                MessageBox.Show(this, "Your game is a different version than this app was made for (probably a game update).\r\n\r\nThe mod may not load, or could look broken. You can always remove it again with \"Uninstall mod\".\r\n\r\nInstall anyway?",
                    App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                int removed = Game.Install(gameDir, BuildMod(), slots.Select(s => gd.FindHero(s.HeroKey).Display));
                DetectGame(null);
                toast.Show((removed > 0 ? "✔   Replaced the previous install. " : "✔   ") + "Installed " + slots.Count + " skin" + (slots.Count == 1 ? "" : "s") + "!  Pick the hero in the Locker.", Theme.Accent);
            }
            catch (UnauthorizedAccessException) { Msg("Windows didn't allow writing to the game folder. Try running the app as administrator.", true); }
            catch (Exception ex) { Msg("Install failed:\r\n\r\n" + ex.Message, true); }
        }

        void RemoveMod()
        {
            if (!ReadyForGame()) return;
            if (!Game.Installed(gameDir)) { toast.Show("The mod isn't installed. The game is using its normal skins.", Theme.Dim); return; }
            if (MessageBox.Show(this, "Remove the skin mod from the game? Your skins stay in this app, so you can install them again later.", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { Game.Uninstall(gameDir); DetectGame(null); toast.Show("Mod removed. The game is back to its normal skins.", Theme.Accent); }
            catch (Exception ex) { Msg("Couldn't remove the mod:\r\n\r\n" + ex.Message, true); }
        }

        void Export()
        {
            if (slots.Count == 0) { Msg("Add at least one skin first.", true); return; }
            using (var d = new SaveFileDialog { Filter = "Zip file (*.zip)|*.zip", FileName = "Dungeons2SkinLoader_skins.zip", Title = "Save the mod for a friend" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var files = BuildMod();
                    if (File.Exists(d.FileName)) File.Delete(d.FileName);
                    using (var z = ZipFile.Open(d.FileName, ZipArchiveMode.Create))
                    {
                        foreach (var kv in files) using (var st = z.CreateEntry(kv.Key).Open()) st.Write(kv.Value, 0, kv.Value.Length);
                        foreach (var kv in FriendScripts.Files(slots.Select(s => gd.FindHero(s.HeroKey).Display)))
                            using (var w = new StreamWriter(z.CreateEntry(kv.Key).Open())) w.Write(kv.Value);
                    }
                    toast.Show("Saved " + Path.GetFileName(d.FileName) + " - your friend unzips it and runs INSTALL.bat", Theme.Accent);
                }
                catch (Exception ex) { Msg("Export failed:\r\n\r\n" + ex.Message, true); }
            }
        }

        void ShowHelp()
        {
            Msg("How it works\r\n\r\n" +
                "1.  Add Minecraft skin PNGs (drag them in or click \"Add skin\").\r\n" +
                "2.  For each one, pick the hero it replaces and how the face should behave.\r\n" +
                "3.  Close the game and click \"Install to game\".\r\n" +
                "4.  In the game, pick that hero in the Locker.\r\n\r\n" +
                "Only the look of the heroes changes; gameplay is untouched, and only players who installed the mod see your skins. " +
                "To see each other's skins, friends need the same mod (\"Export for a friend\").\r\n\r\n" +
                "\"Uninstall mod\" puts everything back to normal. After a game update the mod may stop loading until the app is updated.\r\n\r\n" +
                "Installing again always replaces the previous install (older versions included), so there are never two copies.\r\n\r\n" +
                App.Name + " " + App.Version + " - made by " + App.Author + ". Fan-made; not affiliated with Mojang or Microsoft.", false);
        }

        void Msg(string t, bool warn) { MessageBox.Show(this, t, App.Name, MessageBoxButtons.OK, warn ? MessageBoxIcon.Warning : MessageBoxIcon.Information); }
    }

    static class FriendScripts
    {
        public static Dictionary<string, string> Files(IEnumerable<string> heroes)
        {
            var names = string.Join(",", new[] { ModBuilder.ModName }.Concat(ModBuilder.LegacyNames).Select(n => "'" + n + "'"));
            var d = new Dictionary<string, string>();
            d["install.ps1"] = string.Join("\r\n", new[] {
                "# " + App.Name + " " + App.Version + " by " + App.Author + " - installs / removes the skin mod",
                "$ErrorActionPreference = 'Stop'",
                "$steam = (Get-ItemProperty 'HKCU:\\Software\\Valve\\Steam' -ErrorAction SilentlyContinue).SteamPath",
                "$libs = @()",
                "if ($steam) { $libs += $steam; $vdf = Join-Path $steam 'steamapps\\libraryfolders.vdf'",
                "  if (Test-Path $vdf) { $libs += (Select-String -Path $vdf -Pattern '\"path\"\\s+\"(.+)\"' | ForEach-Object { $_.Matches[0].Groups[1].Value -replace '\\\\\\\\','\\' }) } }",
                "$game = $libs | ForEach-Object { Join-Path $_ 'steamapps\\common\\Minecraft Dungeons II' } | Where-Object { Test-Path $_ } | Select-Object -First 1",
                "if (-not $game) { Write-Host 'Could not find Minecraft Dungeons II in your Steam libraries.'; exit 1 }",
                "if (Get-Process Dungeons-Win64-Shipping -ErrorAction SilentlyContinue) { Write-Host 'Close the game first, then run this again.'; exit 1 }",
                "$paks = Join-Path $game 'Dungeons\\Content\\Paks'",
                "$mods = Join-Path $paks '~mods'",
                "New-Item -ItemType Directory -Force $mods | Out-Null",
                "$here = Split-Path -Parent $MyInvocation.MyCommand.Path",
                "# remove any earlier install (older versions used other file names)",
                "$removed = 0",
                "foreach ($dir in $mods, $paks) { foreach ($n in " + names + ") { foreach ($ext in 'pak','utoc','ucas','sig') {",
                "  $f = Join-Path $dir \"$n.$ext\"; if (Test-Path $f) { [IO.File]::Delete($f); $removed++ } } }",
                "  $m = Join-Path $dir '" + Game.Marker + "'; if (Test-Path $m) { [IO.File]::Delete($m) } }",
                "if ($args[0] -eq 'uninstall') { Write-Host \"Custom skins removed ($removed files).\"; exit 0 }",
                "if ($removed -gt 0) { Write-Host \"Removed the previous install ($removed files).\" }",
                "Copy-Item (Join-Path $here '" + ModBuilder.ModName + ".*') $mods -Force",
                "Set-Content (Join-Path $mods '" + Game.Marker + "') @('" + App.Name + " by " + App.Author + "', 'version=" + App.Version + "', ('installed=' + (Get-Date -Format 'yyyy-MM-dd HH:mm')), 'heroes=" + string.Join(", ", heroes).Replace("'", "''") + "')",
                "Write-Host \"Custom skins installed to $mods\"" });
            d["INSTALL.bat"] = "@echo off\r\npowershell -NoProfile -ExecutionPolicy Bypass -File \"%~dp0install.ps1\"\r\npause\r\n";
            d["UNINSTALL.bat"] = "@echo off\r\npowershell -NoProfile -ExecutionPolicy Bypass -File \"%~dp0install.ps1\" uninstall\r\npause\r\n";
            d["README.txt"] = App.Name + " " + App.Version + " by " + App.Author + " - custom skins for Minecraft Dungeons II (cosmetic only)\r\n\r\n" +
                "Changes how these heroes look on your PC:\r\n  " + string.Join(", ", heroes) +
                "\r\n\r\nInstall:  close the game, extract this zip, double-click INSTALL.bat\r\n" +
                "          (an older install of this mod is detected and replaced automatically)\r\n" +
                "Remove:   close the game, double-click UNINSTALL.bat\r\n\r\n" +
                "Nothing about gameplay changes; only players who install this see the skins.\r\n" +
                "Fan-made; not affiliated with Mojang or Microsoft.\r\n";
            return d;
        }
    }
}
