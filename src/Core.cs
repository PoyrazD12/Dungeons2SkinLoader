// Dungeons 2 Skin Loader core: everything needed to turn Minecraft skins into a Minecraft
// Dungeons II mod container. Ported 1:1 from the Python reference tool
// (build_skin_mod.py / mesh_layers.py / remap.py / icon_render.py).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Dungeons2SkinLoader
{
    public enum FaceMode { Drawn, Game, Blink }

    // A static mod cannot inspect the currently equipped head item.  Instead it
    // can avoid only the outer-skin texels that a chosen type of headgear covers.
    // The profiles deliberately leave the base face and all body overlays alone.
    public enum HeadCoverage { Full, FrontGuard, FullHelmet }

    public class Pkg
    {
        public byte[] ChunkId; public string Path; public byte[] Data; public int PixelOffset; public List<byte[]> Imports;
    }

    public class Hero
    {
        public string Name; public bool Deluxe; public Pkg Skin, Mres, Icon; public Bitmap Original;
        public string Key { get { return Name + (Deluxe ? "|1" : "|0"); } }
        public string Display
        {
            get
            {
                string n = Name.StartsWith("_") ? Name.Substring(1) + " (starter)" : Name;
                if (n == "PizzaChef") n = "Pizza Chef";
                return Deluxe ? n + " (Deluxe)" : n;
            }
        }
    }

    public class Geo { public float[] Pos; public float[] Uv; public ushort[] Tris; public float[] Nrm; }

    public class GameData
    {
        public ulong UtocSize, ContainerId;
        public byte[] Key;
        public List<Hero> Heroes = new List<Hero>();
        public Pkg[] Mesh = new Pkg[2];      // [0] with outer layers, [1] without
        public byte[][] Remap;               // dx, dy, sx, sy
        public Dictionary<int, int> Blink = new Dictionary<int, int>();  // col*8+row -> tu*8+tv
        public Dictionary<int, Point> BlinkOuter = new Dictionary<int, Point>();  // col*8+row -> texel (outer-layer grid)
        public float[] IconCam;   // yaw, pitch, roll, persp, scale, offx, offy (fitted to the game's icons)
        public Geo[] IconGeo = new Geo[2];
        public byte[] MresBody, MresHead;

        public static GameData Load(Stream s)
        {
            var r = new BinaryReader(s);
            if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "MD2S") throw new InvalidDataException("bad data file");
            uint version = r.ReadUInt32();
            var d = new GameData();
            d.UtocSize = r.ReadUInt64(); d.ContainerId = r.ReadUInt64();
            d.Key = r.ReadBytes(32);
            int nh = r.ReadInt32();
            for (int i = 0; i < nh; i++)
            {
                var h = new Hero(); h.Name = Str(r); h.Deluxe = r.ReadByte() != 0;
                h.Skin = ReadPkg(r, true); h.Mres = ReadPkg(r, true); h.Icon = ReadPkg(r, true);
                d.Heroes.Add(h);
            }
            for (int i = 0; i < 2; i++) d.Mesh[i] = ReadPkg(r, false);
            int nr = r.ReadInt32(); d.Remap = new byte[nr][];
            for (int i = 0; i < nr; i++) d.Remap[i] = r.ReadBytes(4);
            int nb = r.ReadInt32();
            for (int i = 0; i < nb; i++) { var b = r.ReadBytes(4); d.Blink[b[0] * 8 + b[1]] = b[2] * 8 + b[3]; }
            for (int i = 0; i < 2; i++)
            {
                var g = new Geo(); int nv = r.ReadInt32();
                g.Pos = Floats(r, nv * 3); g.Uv = Floats(r, nv * 2);
                int nt = r.ReadInt32(); g.Tris = new ushort[nt * 3];
                for (int k = 0; k < nt * 3; k++) g.Tris[k] = r.ReadUInt16();
                g.Nrm = Floats(r, nt * 3);
                d.IconGeo[i] = g;
            }
            d.MresBody = r.ReadBytes(16); d.MresHead = r.ReadBytes(16);
            if (version >= 2)
                foreach (var h in d.Heroes)
                    h.Original = new Bitmap(new MemoryStream(r.ReadBytes(r.ReadInt32())));
            if (version >= 3)
            {
                int no = r.ReadInt32();
                for (int i = 0; i < no; i++) { var b = r.ReadBytes(4); d.BlinkOuter[b[0] * 8 + b[1]] = new Point(b[2], b[3]); }
                int nc = r.ReadInt32(); d.IconCam = Floats(r, nc);
            }
            return d;
        }
        static string Str(BinaryReader r) { int n = r.ReadUInt16(); return Encoding.UTF8.GetString(r.ReadBytes(n)); }
        static float[] Floats(BinaryReader r, int n) { var a = new float[n]; for (int i = 0; i < n; i++) a[i] = r.ReadSingle(); return a; }
        static Pkg ReadPkg(BinaryReader r, bool hasOffset)
        {
            var p = new Pkg(); p.ChunkId = r.ReadBytes(12); p.Path = Str(r);
            p.Data = r.ReadBytes(r.ReadInt32());
            if (hasOffset) p.PixelOffset = r.ReadInt32();
            int ni = r.ReadInt32(); p.Imports = new List<byte[]>();
            for (int i = 0; i < ni; i++) p.Imports.Add(r.ReadBytes(8));
            return p;
        }
        public Hero FindHero(string key) { return Heroes.FirstOrDefault(h => h.Key == key); }
    }

    // ------------------------------------------------------------------ images
    /// <summary>64x64 RGBA image, row-major, 4 bytes per pixel.</summary>
    public class Img
    {
        public int W, H; public byte[] P;
        public Img(int w, int h) { W = w; H = h; P = new byte[w * h * 4]; }
        public Img Clone() { var o = new Img(W, H); Buffer.BlockCopy(P, 0, o.P, 0, P.Length); return o; }
        public int Idx(int x, int y) { return (y * W + x) * 4; }
        public byte A(int x, int y) { return P[Idx(x, y) + 3]; }
        public void Get(int x, int y, byte[] c) { Buffer.BlockCopy(P, Idx(x, y), c, 0, 4); }
        public byte[] Get(int x, int y) { var c = new byte[4]; Get(x, y, c); return c; }
        public void Set(int x, int y, byte[] c) { Buffer.BlockCopy(c, 0, P, Idx(x, y), 4); }
        public Img Crop(int x0, int y0, int w, int h)
        {
            var o = new Img(w, h);
            for (int y = 0; y < h; y++) Buffer.BlockCopy(P, Idx(x0, y0 + y), o.P, o.Idx(0, y), w * 4);
            return o;
        }
        public void Paste(Img s, int x0, int y0)
        {
            for (int y = 0; y < s.H; y++) Buffer.BlockCopy(s.P, s.Idx(0, y), P, Idx(x0, y0 + y), s.W * 4);
        }
        public static Img FromFile(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var bmp = new Bitmap(fs))
            {
                var o = new Img(bmp.Width, bmp.Height);
                for (int y = 0; y < bmp.Height; y++)
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        var c = bmp.GetPixel(x, y); int i = o.Idx(x, y);
                        o.P[i] = c.R; o.P[i + 1] = c.G; o.P[i + 2] = c.B; o.P[i + 3] = c.A;
                    }
                return o;
            }
        }
        public Bitmap ToBitmap()
        {
            var b = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = Idx(x, y);
                    b.SetPixel(x, y, Color.FromArgb(P[i + 3], P[i], P[i + 1], P[i + 2]));
                }
            return b;
        }
        /// <summary>PIL Image.alpha_composite (exact integer math).</summary>
        public void AlphaComposite(Img src)
        {
            for (int i = 0; i < P.Length; i += 4)
            {
                int sa = src.P[i + 3], da = P[i + 3];
                int blend = da * (255 - sa);
                int outa255 = sa * 255 + blend;
                if (outa255 == 0) { P[i] = P[i + 1] = P[i + 2] = P[i + 3] = 0; continue; }
                long coef1 = (long)sa * 255 * 255 * 128 / outa255;
                long coef2 = 255 * 128 - coef1;
                for (int c = 0; c < 3; c++)
                {
                    long tmp = src.P[i + c] * coef1 + P[i + c] * coef2 + (0x80 << 7);
                    P[i + c] = (byte)((((tmp >> 8) + tmp) >> 8) >> 7);
                }
                int ta = outa255 + 0x80;
                P[i + 3] = (byte)(((ta >> 8) + ta) >> 8);
            }
        }
    }

    // ------------------------------------------------------------ conversion
    public static class Converter
    {
        static readonly int[][] ARMS = { new[] { 40, 16 }, new[] { 40, 32 }, new[] { 32, 48 }, new[] { 48, 48 } };
        static readonly int[][] OVERLAYS = { new[] { 0, 32, 0, 16 }, new[] { 16, 32, 16, 16 }, new[] { 40, 32, 40, 16 },
                                             new[] { 0, 48, 16, 48 }, new[] { 48, 48, 32, 48 } };

        public static Img To64(Img im)
        {
            if (im.W == 64 && im.H == 32)
            {
                var n = new Img(64, 64); n.Paste(im, 0, 0);
                n.Paste(MirrorLimb(im.Crop(0, 16, 16, 16)), 16, 48);
                n.Paste(MirrorLimb(im.Crop(40, 16, 16, 16)), 32, 48);
                return n;
            }
            if (im.W != 64 || im.H != 64) throw new InvalidDataException("A Minecraft skin must be 64x64 (or old-style 64x32) pixels; this image is " + im.W + "x" + im.H + ".");
            return im.Clone();
        }

        static Img MirrorLimb(Img l)
        {
            var o = new Img(16, 16);
            int[][] m = { new[] { 4, 0, 4, 4, 4, 0 }, new[] { 8, 0, 4, 4, 8, 0 }, new[] { 4, 4, 4, 12, 4, 4 },
                          new[] { 12, 4, 4, 12, 12, 4 }, new[] { 0, 4, 4, 12, 8, 4 }, new[] { 8, 4, 4, 12, 0, 4 } };
            foreach (var r in m)
                for (int y = 0; y < r[3]; y++)
                    for (int x = 0; x < r[2]; x++)
                        o.Set(r[4] + x, r[5] + y, l.Get(r[0] + r[2] - 1 - x, r[1] + y));
            return o;
        }

        public static bool IsSlim(Img im) { return im.A(50, 16) == 0 && im.A(54, 20) == 0; }

        static void ArmToSlim(Img s, int u, int v)
        {
            var a = s.Crop(u, v, 16, 16); var o = new Img(16, 16);
            int[] kf = { 0, 1, 3 }, kb = { 0, 2, 3 };
            for (int y = 0; y < 4; y++) for (int i = 0; i < 3; i++)
                {
                    o.Set(4 + i, y, a.Get(4 + kf[i], y));   // top
                    o.Set(7 + i, y, a.Get(8 + kf[i], y));   // bottom
                }
            for (int y = 4; y < 16; y++)
            {
                for (int i = 0; i < 4; i++) { o.Set(i, y, a.Get(i, y)); o.Set(7 + i, y, a.Get(8 + i, y)); }
                for (int i = 0; i < 3; i++) { o.Set(4 + i, y, a.Get(4 + kf[i], y)); o.Set(11 + i, y, a.Get(12 + kb[i], y)); }
            }
            s.Paste(o, u, v);
        }

        static double Lum(byte[] p) { return 0.3 * p[0] + 0.59 * p[1] + 0.11 * p[2]; }

        public const int Outer = 64;   // eye codes >= 64 are on the outer (hat) layer

        /// <summary>Guess 1-px eyes: mirrored dark pairs in face rows 2..4 on the face layer,
        /// or failing that on the outer layer. Codes are col*8+row (+64 for the outer layer).</summary>
        public static List<int> DetectEyes(Img skin64)
        {
            var e = DetectEyesIn(skin64.Crop(8, 8, 8, 8));
            if (e.Count == 0) e = DetectEyesIn(skin64.Crop(40, 8, 8, 8)).Select(v => v + Outer).ToList();
            return e;
        }

        static List<int> DetectEyesIn(Img f)
        {
            var vals = new List<double>();
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) if (f.A(x, y) != 0) vals.Add(Lum(f.Get(x, y)));
            vals.Sort();
            double med = vals.Count > 0 ? vals[vals.Count / 2] : 128;
            var dark = new HashSet<int>();
            for (int y = 2; y < 5; y++) for (int x = 1; x < 7; x++)
                    if (f.A(x, y) != 0 && Lum(f.Get(x, y)) < Math.Min(70, med * 0.45)) dark.Add(x * 8 + y);
            return dark.Where(p => dark.Contains((7 - p / 8) * 8 + p % 8)).OrderBy(p => p).ToList();
        }

        static byte[] FillOrNull(Img f, HashSet<int> eyes, int col, int row)
        {
            int[][] d = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 }, new[] { 2, 0 }, new[] { -2, 0 } };
            foreach (var o in d)
            {
                int x = col + o[0], y = row + o[1];
                if (x >= 0 && x < 8 && y >= 0 && y < 8 && !eyes.Contains(x * 8 + y) && f.A(x, y) != 0) return f.Get(x, y);
            }
            return null;
        }

        static byte[] FillColour(Img f, HashSet<int> eyes, int col, int row)
        {
            int[][] d = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 }, new[] { 2, 0 }, new[] { -2, 0 } };
            foreach (var o in d)
            {
                int x = col + o[0], y = row + o[1];
                if (x >= 0 && x < 8 && y >= 0 && y < 8 && !eyes.Contains(x * 8 + y) && f.A(x, y) != 0) return f.Get(x, y);
            }
            return f.Get(col, row);
        }

        static byte[] Darkest(byte[] a, byte[] b) { return a[0] + a[1] + a[2] <= b[0] + b[1] + b[2] ? a : b; }

        /// <summary>Minecraft skin -> the game's hero texture layout (see build_skin_mod.convert_skin).</summary>
        public static Img Convert(GameData gd, Img user, FaceMode mode, ICollection<int> eyePixels, GameFace gameFace = null, int? lidColor = null, int? lidColor2 = null, HeadCoverage headCoverage = HeadCoverage.Full)
        {
            var s = To64(user);
            // Minecraft's outer-head UVs are: top/bottom y0..7, then right,
            // front, left and back at x32..63/y8..15.  A front guard hides only
            // the upper front pixels so side hair/art and beard pixels remain.
            // Full helmet is intentionally broader, but preserves the lower
            // three front rows where many Minecraft beards live.
            if (headCoverage == HeadCoverage.FrontGuard)
            {
                var clear = new byte[4];
                for (int y = 8; y <= 12; y++) for (int x = 40; x < 48; x++) s.Set(x, y, clear);
            }
            else if (headCoverage == HeadCoverage.FullHelmet)
            {
                var lowerFace = new byte[8 * 3 * 4];
                for (int y = 0; y < 3; y++) for (int x = 0; x < 8; x++)
                    Buffer.BlockCopy(s.P, s.Idx(40 + x, 13 + y), lowerFace, (y * 8 + x) * 4, 4);
                var clear = new byte[4];
                for (int y = 0; y < 16; y++) for (int x = 32; x < 64; x++) s.Set(x, y, clear);
                for (int y = 0; y < 3; y++) for (int x = 0; x < 8; x++)
                    Buffer.BlockCopy(lowerFace, (y * 8 + x) * 4, s.P, s.Idx(40 + x, 13 + y), 4);
            }
            if (!IsSlim(s)) foreach (var a in ARMS) ArmToSlim(s, a[0], a[1]);
            foreach (var o in OVERLAYS)
            {
                var b = s.Crop(o[2], o[3], 16, 16); b.AlphaComposite(s.Crop(o[0], o[1], 16, 16)); s.Paste(b, o[2], o[3]);
            }
            var snap = s.Clone();
            foreach (var e in gd.Remap) s.Set(e[0], e[1], snap.Get(e[2], e[3]));
            var face = s.Crop(8, 8, 8, 8); var hat = s.Crop(40, 8, 8, 8);
            var zero = new byte[4];
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) s.Set(x, y, zero);
            byte alpha = 0;
            Func<int, int, byte[]> c = (x, y) => face.Get(x, y);
            var pal = new List<Tuple<int, int, byte[]>> {
                Tuple.Create(6, 5, c(2, 4)), Tuple.Create(7, 5, c(5, 4)),
                Tuple.Create(6, 6, c(1, 4)), Tuple.Create(7, 6, c(6, 4)),
                Tuple.Create(3, 7, Darkest(c(1, 3), c(2, 3))), Tuple.Create(4, 7, Darkest(c(5, 3), c(6, 3))),
                Tuple.Create(6, 7, c(3, 6)), Tuple.Create(7, 7, c(4, 6)) };
            // Python iterates this dict in insertion order; later keys never collide
            foreach (var p in pal) s.Set(p.Item1, p.Item2, new byte[] { p.Item3[0], p.Item3[1], p.Item3[2], alpha });
            if (mode == FaceMode.Game)
            {
                // the game's animated eyes/brows/mouth, one colour per part (both eyes always match);
                // a part set to null stays hidden
                var gf = gameFace ?? GameFace.Auto(face, eyePixels);
                Action<int, int, int?> put = (x, y, v) => s.Set(x, y, v.HasValue
                    ? new byte[] { (byte)(v.Value >> 16), (byte)(v.Value >> 8), (byte)v.Value, 255 } : new byte[4]);
                put(6, 5, gf.Iris); put(7, 5, gf.Iris); put(6, 6, gf.White); put(7, 6, gf.White);
                put(3, 7, gf.Brow); put(4, 7, gf.Brow); put(6, 7, null); put(7, 7, null);   // the game's mouth stays hidden
                // hide the skin's own drawn eyes so only the game's eyes show
                if (eyePixels != null)
                {
                    var eyes = new HashSet<int>(eyePixels.Where(e => e < Outer));
                    foreach (int e in eyes) s.Set(8 + e / 8, 8 + e % 8, FillColour(face, eyes, e / 8, e % 8));
                    var outerEyes = new HashSet<int>(eyePixels.Where(e => e >= Outer).Select(e => e - Outer));
                    foreach (int e in outerEyes) s.Set(40 + e / 8, 8 + e % 8, FillOrNull(hat, outerEyes, e / 8, e % 8) ?? zero);
                }
            }
            foreach (var tx in gd.BlinkOuter.Values) s.Set(tx.X, tx.Y, zero);   // outer eye grid: off unless used
            if (mode == FaceMode.Blink && eyePixels != null)
            {
                Func<int?, byte[]> rgba = v => v.HasValue ? new byte[] { (byte)(v.Value >> 16), (byte)(v.Value >> 8), (byte)v.Value, 255 } : null;
                byte[] lidL = rgba(lidColor), lidR = rgba(lidColor2) ?? lidL;
                Func<int, byte[]> lidFor = col => col < 4 ? lidL : lidR;   // eye 1 = left half, eye 2 = right half
                var eyes = new HashSet<int>(eyePixels.Where(e => e < Outer && gd.Blink.ContainsKey(e)));
                foreach (int e in eyes)
                {
                    int col = e / 8, row = e % 8, t = gd.Blink[e];
                    var ec = c(col, row);
                    s.Set(t / 8, t % 8, new byte[] { ec[0], ec[1], ec[2], 255 });
                    s.Set(8 + col, 8 + row, lidFor(col) ?? FillColour(face, eyes, col, row));
                }
                // eyes on the outer layer: drawn by the grid in front of the hat layer; the
                // hat pixel becomes the eyelid (or clears, showing the face underneath)
                var outer = new HashSet<int>(eyePixels.Where(e => e >= Outer && gd.BlinkOuter.ContainsKey(e - Outer)).Select(e => e - Outer));
                foreach (int e in outer)
                {
                    int col = e / 8, row = e % 8; var tx = gd.BlinkOuter[e];
                    var ec = hat.Get(col, row); if (ec[3] == 0) continue;
                    s.Set(tx.X, tx.Y, new byte[] { ec[0], ec[1], ec[2], 255 });
                    s.Set(40 + col, 8 + row, lidFor(col) ?? FillOrNull(hat, outer, col, row) ?? zero);
                }
            }
            var pf = face.Clone(); pf.AlphaComposite(hat); s.Paste(pf, 56, 20);
            return s;
        }
    }

    /// <summary>Colours for the game's animated face parts (RGB ints); null hides that part.</summary>
    public class GameFace
    {
        public int? Iris, White, Brow, Mouth;
        public GameFace Clone() { return (GameFace)MemberwiseClone(); }
        public string Serialize()
        {
            return "g=" + string.Join(",", new[] { Iris, White, Brow, Mouth }.Select(v => v.HasValue ? v.Value.ToString("x6") : "-"));
        }
        public static GameFace Parse(string s)
        {
            if (s == null || !s.StartsWith("g=")) return null;
            var p = s.Substring(2).Split(',');
            if (p.Length != 4) return null;
            Func<string, int?> v = x => x == "-" ? (int?)null : System.Convert.ToInt32(x, 16);
            return new GameFace { Iris = v(p[0]), White = v(p[1]), Brow = v(p[2]), Mouth = v(p[3]) };
        }
        static int Rgb(byte[] c) { return (c[0] << 16) | (c[1] << 8) | c[2]; }
        static double Lum(byte[] c) { return 0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2]; }

        /// <summary>Sensible colours from the face: your eye colour (or near-black) for both
        /// irises, white eye whites, and brows/mouth only where the face really has them.</summary>
        public static GameFace Auto(Img face, ICollection<int> eyes)
        {
            var eyeSet = new HashSet<int>((eyes ?? new int[0]).Where(e => e < 64));
            // most common opaque face colour = skin
            var counts = new Dictionary<int, int>();
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                {
                    var c = face.Get(x, y); if (c[3] == 0) continue;
                    int k = Rgb(c); int n; counts.TryGetValue(k, out n); counts[k] = n + 1;
                }
            int skinRgb = counts.Count > 0 ? counts.OrderByDescending(kv => kv.Value).First().Key : 0xC08060;
            var skin = new byte[] { (byte)(skinRgb >> 16), (byte)(skinRgb >> 8), (byte)skinRgb, 255 };
            double sl = Lum(skin);
            var g = new GameFace();
            if (eyeSet.Count > 0) { var e = eyeSet.OrderBy(x => x).First(); g.Iris = Rgb(face.Get(e / 8, e % 8)); }
            else
            {
                var a = face.Get(2, 4); var b = face.Get(5, 4);
                var d = Lum(a) <= Lum(b) ? a : b;
                g.Iris = Lum(d) < sl - 40 ? Rgb(d) : 0x1E1E1E;
            }
            var w = face.Get(1, 4);
            bool whitish = Lum(w) > 200 && Math.Max(w[0], Math.Max(w[1], w[2])) - Math.Min(w[0], Math.Min(w[1], w[2])) < 30;
            g.White = whitish && !eyeSet.Contains(1 * 8 + 4) ? Rgb(w) : 0xF4F4F4;
            byte[] brow = null;
            foreach (var x in new[] { 1, 2, 5, 6 })
            {
                var c = face.Get(x, 3);
                if (c[3] != 0 && !eyeSet.Contains(x * 8 + 3) && Lum(c) < sl - 50 && (brow == null || Lum(c) < Lum(brow))) brow = c;
            }
            g.Brow = brow != null ? (int?)Rgb(brow) : null;
            g.Mouth = null;   // mouth option removed: the skin's own drawn mouth is used
            return g;
        }
    }

    // --------------------------------------------------------------- renderer
    public static class Renderer
    {
        static double[,] Rot(double ax, double ay, double az, double deg)
        {
            double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            return new double[,] {
                { c + ax*ax*(1-c), ax*ay*(1-c) - az*s, ax*az*(1-c) + ay*s },
                { ay*ax*(1-c) + az*s, c + ay*ay*(1-c), ay*az*(1-c) - ax*s },
                { az*ax*(1-c) - ay*s, az*ay*(1-c) + ax*s, c + az*az*(1-c) } };
        }
        static double[,] Mul(double[,] a, double[,] b)
        {
            var o = new double[3, 3];
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) for (int k = 0; k < 3; k++) o[i, j] += a[i, k] * b[k, j];
            return o;
        }

        /// <summary>Old orthographic view (used when the data file has no fitted icon camera).</summary>
        public static Img Render(Geo g, Img tex, int size, int ss = 3, double yaw = -22, double pitch = -14, double fill = 0.845)
        {
            var M = Mul(Rot(1, 0, 0, pitch), Rot(0, 0, 1, yaw));
            int nv = g.Pos.Length / 3, S = size * ss;
            var sx = new double[nv]; var sy = new double[nv]; var dp = new double[nv];
            double minY = 1e9, maxY = -1e9, minX = 1e9, maxX = -1e9;
            for (int i = 0; i < nv; i++)
            {
                double x = g.Pos[i * 3], y = g.Pos[i * 3 + 1], z = g.Pos[i * 3 + 2];
                sx[i] = M[0, 0] * x + M[0, 1] * y + M[0, 2] * z; sy[i] = -(M[2, 0] * x + M[2, 1] * y + M[2, 2] * z); dp[i] = -(M[1, 0] * x + M[1, 1] * y + M[1, 2] * z);
                minY = Math.Min(minY, sy[i]); maxY = Math.Max(maxY, sy[i]); minX = Math.Min(minX, sx[i]); maxX = Math.Max(maxX, sx[i]);
            }
            double scale = fill * S / (maxY - minY), cx = (maxX + minX) / 2;
            for (int i = 0; i < nv; i++) { sx[i] = (sx[i] - cx) * scale + S / 2.0; sy[i] = (sy[i] - minY) * scale + S * (1 - fill) / 2; }
            return Raster(g, tex, size, ss, M, sx, sy, dp);
        }

        /// <summary>Locker-icon render: the game's own pose and camera (fitted to its hero icons).
        /// extraYaw turns the model on the spot for the interactive preview.</summary>
        public static Img RenderIcon(Geo g, Img tex, int size, float[] cam, double extraYaw = 0, int ss = 3)
        {
            if (cam == null) return Render(g, tex, size, ss, -22 + extraYaw);
            var M = Mul(Mul(Rot(1, 0, 0, cam[1]), Rot(0, 1, 0, cam[2])), Rot(0, 0, 1, cam[0] + extraYaw));
            int nv = g.Pos.Length / 3, S = size * ss;
            double dist = 40.0 + 400.0 * (1 - Math.Max(0, Math.Min(1, cam[3]))), s = cam[4] * S / 36.0;
            var sx = new double[nv]; var sy = new double[nv]; var dp = new double[nv];
            for (int i = 0; i < nv; i++)
            {
                double x = g.Pos[i * 3], y = g.Pos[i * 3 + 1], z = g.Pos[i * 3 + 2] - 16.0;
                double vx = M[0, 0] * x + M[0, 1] * y + M[0, 2] * z, vy = M[1, 0] * x + M[1, 1] * y + M[1, 2] * z, vz = M[2, 0] * x + M[2, 1] * y + M[2, 2] * z;
                double f = dist / (dist - vy);
                sx[i] = vx * f * s + S / 2.0 + cam[5] * S; sy[i] = -vz * f * s + S / 2.0 + cam[6] * S; dp[i] = -vy;
            }
            return Raster(g, tex, size, ss, M, sx, sy, dp);
        }

        static Img Raster(Geo g, Img tex, int size, int ss, double[,] M, double[] sx, double[] sy, double[] dp)
        {
            int S = size * ss;
            double lx = -0.5, ly = 0.7, lz = 0.55, ll = Math.Sqrt(lx * lx + ly * ly + lz * lz); lx /= ll; ly /= ll; lz /= ll;
            var img = new float[S * S * 4]; var zb = new double[S * S];
            for (int i = 0; i < zb.Length; i++) zb[i] = double.PositiveInfinity;
            int nt = g.Tris.Length / 3;
            for (int k = 0; k < nt; k++)
            {
                double nx = g.Nrm[k * 3], ny = g.Nrm[k * 3 + 1], nz = g.Nrm[k * 3 + 2];
                double vny = M[1, 0] * nx + M[1, 1] * ny + M[1, 2] * nz;
                if (vny <= 0.02) continue;
                double vnx = M[0, 0] * nx + M[0, 1] * ny + M[0, 2] * nz, vnz = M[2, 0] * nx + M[2, 1] * ny + M[2, 2] * nz;
                double shade = 0.55 + 0.45 * Math.Max(vnx * lx + vny * ly + vnz * lz, 0);
                int a = g.Tris[k * 3], b = g.Tris[k * 3 + 1], c = g.Tris[k * 3 + 2];
                double x0 = sx[a], x1 = sx[b], x2 = sx[c], y0 = sy[a], y1 = sy[b], y2 = sy[c];
                double d = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2);
                if (Math.Abs(d) < 1e-9) continue;
                int bx0 = Math.Max(0, (int)Math.Min(x0, Math.Min(x1, x2))), bx1 = Math.Min(S - 1, (int)Math.Max(x0, Math.Max(x1, x2)) + 1);
                int by0 = Math.Max(0, (int)Math.Min(y0, Math.Min(y1, y2))), by1 = Math.Min(S - 1, (int)Math.Max(y0, Math.Max(y1, y2)) + 1);
                for (int py = by0; py <= by1; py++)
                    for (int px = bx0; px <= bx1; px++)
                    {
                        double X = px + .5, Y = py + .5;
                        double l0 = ((y1 - y2) * (X - x2) + (x2 - x1) * (Y - y2)) / d;
                        double l1 = ((y2 - y0) * (X - x2) + (x0 - x2) * (Y - y2)) / d;
                        double l2 = 1 - l0 - l1;
                        if (l0 < -1e-4 || l1 < -1e-4 || l2 < -1e-4) continue;
                        double Z = l0 * dp[a] + l1 * dp[b] + l2 * dp[c];
                        int zi = py * S + px;
                        if (Z >= zb[zi]) continue;
                        double U = l0 * g.Uv[a * 2] + l1 * g.Uv[b * 2] + l2 * g.Uv[c * 2];
                        double V = l0 * g.Uv[a * 2 + 1] + l1 * g.Uv[b * 2 + 1] + l2 * g.Uv[c * 2 + 1];
                        int tu = Math.Max(0, Math.Min(tex.W - 1, (int)(U / 64 * tex.W))), tv = Math.Max(0, Math.Min(tex.H - 1, (int)(V / 64 * tex.H)));
                        int ti = tex.Idx(tu, tv);
                        if (tex.P[ti + 3] <= 127) continue;
                        zb[zi] = Z;
                        img[zi * 4] = (float)(tex.P[ti] * shade); img[zi * 4 + 1] = (float)(tex.P[ti + 1] * shade);
                        img[zi * 4 + 2] = (float)(tex.P[ti + 2] * shade); img[zi * 4 + 3] = 255;
                    }
            }
            var o = new Img(size, size);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    double r = 0, gg = 0, bb = 0, aa = 0;
                    for (int j = 0; j < ss; j++) for (int i = 0; i < ss; i++)
                        {
                            int q = ((y * ss + j) * S + x * ss + i) * 4; double al = img[q + 3] / 255.0;
                            r += img[q] * al; gg += img[q + 1] * al; bb += img[q + 2] * al; aa += al;
                        }
                    int oi = o.Idx(x, y);
                    if (aa > 0) { o.P[oi] = (byte)Math.Min(255, r / aa + .5); o.P[oi + 1] = (byte)Math.Min(255, gg / aa + .5); o.P[oi + 2] = (byte)Math.Min(255, bb / aa + .5); }
                    o.P[oi + 3] = (byte)Math.Min(255, aa * 255 / (ss * ss) + .5);
                }
            return o;
        }
    }

    // ---------------------------------------------------------------- encoders
    public static class Encoders
    {
        public static byte[] Bgra(Img s)
        {
            var o = new byte[s.W * s.H * 4];
            for (int i = 0; i < o.Length; i += 4) { o[i] = s.P[i + 2]; o[i + 1] = s.P[i + 1]; o[i + 2] = s.P[i]; o[i + 3] = s.P[i + 3]; }
            return o;
        }

        public static byte[] Bc3(Img im)
        {
            var o = new MemoryStream();
            var blk = new int[16, 4];
            for (int by = 0; by < im.H; by += 4)
                for (int bx = 0; bx < im.W; bx += 4)
                {
                    for (int i = 0; i < 16; i++) { int q = im.Idx(bx + i % 4, by + i / 4); for (int c = 0; c < 4; c++) blk[i, c] = im.P[q + c]; }
                    Alpha(blk, o); Colour(blk, o);
                }
            return o.ToArray();
        }

        static void Alpha(int[,] b, Stream o)
        {
            int a0 = 0, a1 = 255;
            for (int i = 0; i < 16; i++) { a0 = Math.Max(a0, b[i, 3]); a1 = Math.Min(a1, b[i, 3]); }
            o.WriteByte((byte)a0); o.WriteByte((byte)a1);
            ulong bits = 0;
            if (a0 != a1)
            {
                var pal = new int[8]; pal[0] = a0; pal[1] = a1;
                for (int i = 1; i < 7; i++) pal[i + 1] = ((7 - i) * a0 + i * a1) / 7;
                for (int i = 0; i < 16; i++)
                {
                    int best = 0, bd = int.MaxValue;
                    for (int k = 0; k < 8; k++) { int dd = Math.Abs(b[i, 3] - pal[k]); if (dd < bd) { bd = dd; best = k; } }
                    bits |= (ulong)best << (3 * i);
                }
            }
            for (int i = 0; i < 6; i++) o.WriteByte((byte)(bits >> (8 * i)));
        }

        static int To565(int r, int g, int b) { return ((r * 31 + 127) / 255 << 11) | ((g * 63 + 127) / 255 << 5) | ((b * 31 + 127) / 255); }
        static int[] From565(int v) { return new[] { ((v >> 11) & 31) * 255 / 31, ((v >> 5) & 63) * 255 / 63, (v & 31) * 255 / 31 }; }

        static void Colour(int[,] b, Stream o)
        {
            double mr = 0, mg = 0, mb = 0;
            for (int i = 0; i < 16; i++) { mr += b[i, 0]; mg += b[i, 1]; mb += b[i, 2]; }
            mr /= 16; mg /= 16; mb /= 16;
            // principal axis by power iteration on the covariance
            var cov = new double[3, 3];
            for (int i = 0; i < 16; i++)
            {
                double[] d = { b[i, 0] - mr, b[i, 1] - mg, b[i, 2] - mb };
                for (int p = 0; p < 3; p++) for (int q = 0; q < 3; q++) cov[p, q] += d[p] * d[q];
            }
            double ax = 1, ay = 1, az = 1;
            for (int it = 0; it < 16; it++)
            {
                double nx = cov[0, 0] * ax + cov[0, 1] * ay + cov[0, 2] * az, ny = cov[1, 0] * ax + cov[1, 1] * ay + cov[1, 2] * az, nz = cov[2, 0] * ax + cov[2, 1] * ay + cov[2, 2] * az;
                double l = Math.Sqrt(nx * nx + ny * ny + nz * nz); if (l < 1e-9) break;
                ax = nx / l; ay = ny / l; az = nz / l;
            }
            int lo = 0, hi = 0; double tmin = 1e9, tmax = -1e9;
            for (int i = 0; i < 16; i++)
            {
                double t = (b[i, 0] - mr) * ax + (b[i, 1] - mg) * ay + (b[i, 2] - mb) * az;
                if (t < tmin) { tmin = t; lo = i; }
                if (t > tmax) { tmax = t; hi = i; }
            }
            int c0 = To565(b[hi, 0], b[hi, 1], b[hi, 2]), c1 = To565(b[lo, 0], b[lo, 1], b[lo, 2]);
            if (c0 < c1) { int t = c0; c0 = c1; c1 = t; }
            uint bits = 0;
            if (c0 != c1)
            {
                var p0 = From565(c0); var p1 = From565(c1);
                var pal = new int[4][] { p0, p1, new int[3], new int[3] };
                for (int k = 0; k < 3; k++) { pal[2][k] = (2 * p0[k] + p1[k]) / 3; pal[3][k] = (p0[k] + 2 * p1[k]) / 3; }
                for (int i = 0; i < 16; i++)
                {
                    int best = 0; long bd = long.MaxValue;
                    for (int k = 0; k < 4; k++)
                    {
                        long dd = 0; for (int ch = 0; ch < 3; ch++) { long e = b[i, ch] - pal[k][ch]; dd += e * e; }
                        if (dd < bd) { bd = dd; best = k; }
                    }
                    bits |= (uint)best << (2 * i);
                }
            }
            var w = new BinaryWriter(o); w.Write((ushort)c0); w.Write((ushort)c1); w.Write(bits);
        }
    }

    // --------------------------------------------------------------- container
    public class SkinSlot
    {
        public string HeroKey; public string ImagePath; public FaceMode Mode = FaceMode.Drawn; public List<int> Eyes = new List<int>(); public GameFace Face;
        public int? LidColor;   // blinking eyes close to this colour (null = matching face colour)
        public int? LidColor2;  // optional separate colour for the right eye (eye 2)
    }

    public static class ModBuilder
    {
        public const string ModName = "zzz_Dungeons2SkinLoader_P";
        /// <summary>File names used by earlier builds; found and removed on install/uninstall.</summary>
        public static readonly string[] LegacyNames = { "zzz_CustomSkin_P" };

        static byte[] Patch(byte[] tmpl, int off, byte[] data)
        {
            var o = (byte[])tmpl.Clone(); Buffer.BlockCopy(data, 0, o, off, data.Length); return o;
        }

        public static byte[] MresData(GameData gd)
        {
            var o = new byte[4096]; int k = 0;
            for (int by = 0; by < 16; by++) for (int bx = 0; bx < 16; bx++, k += 16)
                    Buffer.BlockCopy(by < 4 && bx < 8 ? gd.MresHead : gd.MresBody, 0, o, k, 16);
            return o;
        }

        /// <summary>Builds the three mod files (pak, utoc, ucas) for the given skins.</summary>
        public static Dictionary<string, byte[]> Build(GameData gd, IList<SkinSlot> slots, bool layers, Action<string> log, bool includeMesh = true, HeadCoverage headCoverage = HeadCoverage.Full)
        {
            var chunks = new List<Tuple<byte[], byte[]>>(); var paths = new List<Tuple<string, int>>(); var imports = new List<List<byte[]>>();
            Action<Pkg, byte[]> add = (p, data) =>
            {
                if (chunks.Any(c => c.Item1.SequenceEqual(p.ChunkId))) return;
                paths.Add(Tuple.Create(p.Path, chunks.Count)); chunks.Add(Tuple.Create(p.ChunkId, data)); imports.Add(p.Imports);
            };
            var geo = gd.IconGeo[layers ? 0 : 1];
            foreach (var s in slots)
            {
                var h = gd.FindHero(s.HeroKey);
                if (h == null) throw new InvalidDataException("Unknown hero " + s.HeroKey);
                var tex = Converter.Convert(gd, Img.FromFile(s.ImagePath), s.Mode, s.Eyes, s.Face, s.LidColor, s.LidColor2, headCoverage);
                add(h.Skin, Patch(h.Skin.Data, h.Skin.PixelOffset, Encoders.Bgra(tex)));
                add(h.Mres, Patch(h.Mres.Data, h.Mres.PixelOffset, MresData(gd)));
                var icon = Renderer.RenderIcon(geo, tex, 256, gd.IconCam);
                add(h.Icon, Patch(h.Icon.Data, h.Icon.PixelOffset, Encoders.Bc3(icon)));
                if (log != null) log(h.Display + "  <-  " + Path.GetFileName(s.ImagePath));
            }
            var mesh = gd.Mesh[layers ? 0 : 1];
            add(mesh, mesh.Data);
            ulong cont = BitConverter.ToUInt64(SHA1.Create().ComputeHash(Encoding.ASCII.GetBytes(ModName)), 0);
            var hdr = ContainerHeader(cont, chunks.Select(c => c.Item1.Take(8).ToArray()).ToList(), imports);
            var hid = new byte[12]; Buffer.BlockCopy(BitConverter.GetBytes(cont), 0, hid, 0, 8); hid[11] = 6;
            chunks.Add(Tuple.Create(hid, hdr));
            var res = WriteContainer(gd.Key, chunks, paths);
            res[ModName + ".pak"] = EmptyPak();
            return res;
        }

        static byte[] FStr(string s) { var e = Encoding.ASCII.GetBytes(s + "\0"); var o = new byte[4 + e.Length]; Buffer.BlockCopy(BitConverter.GetBytes(e.Length), 0, o, 0, 4); Buffer.BlockCopy(e, 0, o, 4, e.Length); return o; }

        static byte[] ContainerHeader(ulong cid, List<byte[]> pkgs, List<List<byte[]>> imps)
        {
            int k = pkgs.Count; var blob = new byte[16 * k]; var tail = new List<byte>();
            for (int i = 0; i < k; i++)
            {
                int e = 16 * i, ni = imps[i].Count;
                int io = 16 * k + tail.Count; foreach (var id in imps[i]) tail.AddRange(id);
                Buffer.BlockCopy(BitConverter.GetBytes(ni), 0, blob, e, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(ni > 0 ? io - e : 0), 0, blob, e + 4, 4);
            }
            var all = blob.Concat(tail).ToArray();
            var m = new MemoryStream(); var w = new BinaryWriter(m);
            w.Write(0x496F436E); w.Write(5); w.Write(cid);
            w.Write(k); foreach (var p in pkgs) w.Write(p);
            w.Write(all.Length); w.Write(all);
            w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            long off = m.Length + 16; w.Write(off); w.Write(4L); w.Write(0);
            return m.ToArray();
        }

        static byte[] DirIndex(string mount, List<Tuple<string, int>> paths)
        {
            var strings = new List<string>(); var sidx = new Dictionary<string, int>();
            Func<string, int> S = x => { int v; if (!sidx.TryGetValue(x, out v)) { v = strings.Count; sidx[x] = v; strings.Add(x); } return v; };
            const uint NONE = 0xFFFFFFFF;
            var dirs = new List<uint[]> { new[] { NONE, NONE, NONE, NONE } }; var files = new List<uint[]>();
            Func<int, string, int> child = (di, name) =>
            {
                uint c = dirs[di][1];
                while (c != NONE) { if (strings[(int)dirs[(int)c][0]] == name) return (int)c; c = dirs[(int)c][2]; }
                dirs.Add(new[] { (uint)S(name), NONE, dirs[di][1], NONE }); dirs[di][1] = (uint)(dirs.Count - 1);
                return dirs.Count - 1;
            };
            foreach (var p in paths)
            {
                var parts = p.Item1.Split('/'); int d = 0;
                for (int i = 0; i < parts.Length - 1; i++) d = child(d, parts[i]);
                files.Add(new[] { (uint)S(parts[parts.Length - 1]), dirs[d][3], (uint)p.Item2 }); dirs[d][3] = (uint)(files.Count - 1);
            }
            var m = new MemoryStream(); var w = new BinaryWriter(m);
            w.Write(FStr(mount));
            w.Write(dirs.Count); foreach (var x in dirs) foreach (var v in x) w.Write(v);
            w.Write(files.Count); foreach (var x in files) foreach (var v in x) w.Write(v);
            w.Write(strings.Count); foreach (var x in strings) w.Write(FStr(x));
            return m.ToArray();
        }

        static ulong ChunkHash(ulong seed, byte[] data)
        {
            ulong x = seed != 0 ? seed : 0xCBF29CE484222325UL;
            foreach (var b in data) x = (x * 0x100000001B3UL) ^ b;
            return x;
        }

        static byte[] Aes(byte[] key, byte[] data)
        {
            using (var a = new AesManaged { Mode = CipherMode.ECB, Padding = PaddingMode.None, KeySize = 256, Key = key })
            using (var t = a.CreateEncryptor()) return t.TransformFinalBlock(data, 0, data.Length);
        }
        static byte[] Pad16(byte[] d) { var o = new byte[(d.Length + 15) / 16 * 16]; Buffer.BlockCopy(d, 0, o, 0, d.Length); return o; }
        static void Be5(BinaryWriter w, long v) { for (int i = 4; i >= 0; i--) w.Write((byte)(v >> (8 * i))); }
        static void Le(BinaryWriter w, long v, int n) { for (int i = 0; i < n; i++) w.Write((byte)(v >> (8 * i))); }

        static Dictionary<string, byte[]> WriteContainer(byte[] key, List<Tuple<byte[], byte[]>> chunks, List<Tuple<string, int>> paths)
        {
            const int BS = 0x10000;
            var ucas = new MemoryStream(); var offlen = new List<long[]>(); var blocks = new List<long[]>(); long uoff = 0;
            foreach (var c in chunks)
            {
                var data = c.Item2; offlen.Add(new[] { uoff, data.Length });
                for (int i = 0; i < Math.Max(data.Length, 1); i += BS)
                {
                    int n = Math.Min(BS, data.Length - i); var part = new byte[n]; Buffer.BlockCopy(data, i, part, 0, n);
                    blocks.Add(new[] { ucas.Length, n, n });
                    var enc = Aes(key, Pad16(part)); ucas.Write(enc, 0, enc.Length);
                }
                uoff += (data.Length + BS - 1) / BS * BS;
            }
            var di = Aes(key, Pad16(DirIndex("../../../", paths)));
            int nn = chunks.Count, nseeds = nn;
            while (chunks.Select(c => ChunkHash(0, c.Item1) % (ulong)nseeds).Distinct().Count() < nn) nseeds++;
            var seeds = new int[nseeds];
            for (int i = 0; i < nn; i++) seeds[(int)(ChunkHash(0, chunks[i].Item1) % (ulong)nseeds)] = -i - 1;
            var hdr = new byte[144];
            Buffer.BlockCopy(Encoding.ASCII.GetBytes("-==--==--==--==-"), 0, hdr, 0, 16);
            hdr[16] = 8;
            int[] f = { 144, nn, blocks.Count, 12, 0, 32, BS, di.Length, 1 };
            for (int i = 0; i < f.Length; i++) Buffer.BlockCopy(BitConverter.GetBytes(f[i]), 0, hdr, 20 + 4 * i, 4);
            Buffer.BlockCopy(chunks[nn - 1].Item1, 0, hdr, 0x38, 8);
            hdr[0x50] = 8 | 2;
            Buffer.BlockCopy(BitConverter.GetBytes(nseeds), 0, hdr, 0x54, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(ulong.MaxValue), 0, hdr, 0x58, 8);
            var m = new MemoryStream(); var w = new BinaryWriter(m);
            w.Write(hdr);
            foreach (var c in chunks) w.Write(c.Item1);
            foreach (var ol in offlen) { Be5(w, ol[0]); Be5(w, ol[1]); }
            foreach (var s in seeds) w.Write(s);
            foreach (var b in blocks) { Le(w, b[0], 5); Le(w, b[1], 3); Le(w, b[2], 3); w.Write((byte)0); }
            w.Write(di);
            var sha = SHA1.Create();
            foreach (var c in chunks) { w.Write(sha.ComputeHash(c.Item2)); w.Write(0); }
            var res = new Dictionary<string, byte[]>();
            res[ModName + ".utoc"] = m.ToArray(); res[ModName + ".ucas"] = ucas.ToArray();
            return res;
        }

        static byte[] EmptyPak()
        {
            var sha = SHA1.Create();
            var fdi = new MemoryStream(); var fw = new BinaryWriter(fdi); fw.Write(1); fw.Write(FStr("/")); fw.Write(0);
            var phi = new byte[8];
            int primLen = FStr("../../../").Length + 4 + 8 + 4 + 16 + 20 + 4 + 16 + 20 + 4 + 4;
            long phiOff = primLen, fdiOff = phiOff + phi.Length;
            var pm = new MemoryStream(); var pw = new BinaryWriter(pm);
            pw.Write(FStr("../../../")); pw.Write(0); pw.Write(0UL);
            pw.Write(1); pw.Write(phiOff); pw.Write((long)phi.Length); pw.Write(sha.ComputeHash(phi));
            pw.Write(1); pw.Write(fdiOff); pw.Write(fdi.Length); pw.Write(sha.ComputeHash(fdi.ToArray()));
            pw.Write(0); pw.Write(0);
            var prim = pm.ToArray();
            var o = new MemoryStream(); var w = new BinaryWriter(o);
            w.Write(prim); w.Write(phi); w.Write(fdi.ToArray());
            w.Write(new byte[16]); w.Write((byte)0); w.Write(0x5A6F12E1); w.Write(11); w.Write(0L); w.Write((long)prim.Length);
            w.Write(sha.ComputeHash(prim)); w.Write(new byte[160]);
            return o.ToArray();
        }
    }

    // -------------------------------------------------------------------- game
    public static class Game
    {
        public const string Exe = "Dungeons-Win64-Shipping";

        public static List<string> Candidates()
        {
            var libs = new List<string>();
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    var sp = k != null ? k.GetValue("SteamPath") as string : null;
                    if (sp != null)
                    {
                        libs.Add(sp.Replace('/', '\\'));
                        var vdf = Path.Combine(sp, @"steamapps\libraryfolders.vdf");
                        if (File.Exists(vdf))
                            foreach (System.Text.RegularExpressions.Match mm in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"(.+?)\""))
                                libs.Add(mm.Groups[1].Value.Replace(@"\\", @"\"));
                    }
                }
            }
            catch { }
            foreach (var d in DriveInfo.GetDrives()) { libs.Add(Path.Combine(d.Name, "SteamLibrary")); libs.Add(Path.Combine(d.Name, @"Program Files (x86)\Steam")); }
            return libs.Select(l => Path.Combine(l, @"steamapps\common\Minecraft Dungeons II")).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string Paks(string game) { return Path.Combine(game, @"Dungeons\Content\Paks"); }
        public static bool IsGame(string game) { return File.Exists(Path.Combine(Paks(game), "Dungeons-Windows.utoc")); }
        public static string Find() { return Candidates().FirstOrDefault(IsGame); }

        /// <summary>True when the installed game is the build this app's data was made from.</summary>
        public static bool VersionMatches(GameData gd, string game)
        {
            var f = Path.Combine(Paks(game), "Dungeons-Windows.utoc");
            if (new FileInfo(f).Length != (long)gd.UtocSize) return false;
            using (var s = File.OpenRead(f)) { var b = new byte[0x40]; s.Read(b, 0, b.Length); return BitConverter.ToUInt64(b, 0x38) == gd.ContainerId; }
        }

        public static bool Running() { return System.Diagnostics.Process.GetProcessesByName(Exe).Length > 0; }
        public static string ModDir(string game) { return Path.Combine(Paks(game), "~mods"); }
        public const string Marker = "Dungeons2SkinLoader.txt";   // written next to the mod files: version + date

        public enum State { None, Current, Old }

        /// <summary>Every mod file from this app (any version / file name) in ~mods or the Paks folder.</summary>
        public static List<string> ModFiles(string game)
        {
            var found = new List<string>();
            foreach (var dir in new[] { ModDir(game), Paks(game) })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var name in new[] { ModBuilder.ModName }.Concat(ModBuilder.LegacyNames))
                    foreach (var ext in new[] { ".pak", ".utoc", ".ucas", ".sig" })
                    {
                        var f = Path.Combine(dir, name + ext);
                        if (File.Exists(f)) found.Add(f);
                    }
                var m = Path.Combine(dir, Marker);
                if (File.Exists(m)) found.Add(m);
            }
            return found;
        }

        /// <summary>Is the mod installed, and is it this version?</summary>
        public static State Detect(string game, out string version)
        {
            version = null;
            var files = ModFiles(game);
            if (files.Count == 0) return State.None;
            var marker = Path.Combine(ModDir(game), Marker);
            if (File.Exists(marker))
                foreach (var l in File.ReadAllLines(marker))
                    if (l.StartsWith("version=")) version = l.Substring(8).Trim();
            bool current = File.Exists(Path.Combine(ModDir(game), ModBuilder.ModName + ".utoc")) && version == App.Version
                && !files.Any(f => ModBuilder.LegacyNames.Any(n => Path.GetFileName(f).StartsWith(n)));
            return current ? State.Current : State.Old;
        }
        public static bool Installed(string game) { string v; return Detect(game, out v) != State.None; }

        /// <summary>Removes any earlier install (old names, old versions, stray copies), then writes the new files.
        /// Returns how many old files were removed.</summary>
        public static int Install(string game, Dictionary<string, byte[]> files, IEnumerable<string> heroes)
        {
            int removed = Uninstall(game);
            var d = ModDir(game); Directory.CreateDirectory(d);
            foreach (var kv in files) File.WriteAllBytes(Path.Combine(d, kv.Key), kv.Value);
            File.WriteAllLines(Path.Combine(d, Marker), new[] {
                "Dungeons 2 Skin Loader by Poyraz Captain - cosmetic skin mod",
                "version=" + App.Version,
                "installed=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                "heroes=" + string.Join(", ", heroes) });
            return removed;
        }
        public static int Uninstall(string game)
        {
            var files = ModFiles(game);
            foreach (var f in files) File.Delete(f);
            return files.Count;
        }
    }
}
