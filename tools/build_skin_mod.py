"""
Minecraft Dungeons II skin conversion + IoStore mod-container writer (Python
reference implementation; the app's Core.cs is a 1:1 port). Used as a library
by export_data.py.
"""
import argparse, hashlib, os, struct, sys
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.environ.get("MD2_GAME_DIR", r"C:\Program Files (x86)\Steam\steamapps\common\Minecraft Dungeons II")
PAKS = os.path.join(GAME, r"Dungeons\Content\Paks")
CACHE = os.path.join(HERE, "cache")
SKIN_DIR = "/Dungeons/Content/Spicewood/Art/Characters/Player/Skins/"

# ---------------------------------------------------------------- originals
# Original packages are pulled out of the game once (needs the key + Oodle)
# and cached, so later rebuilds only need this folder.

def load_originals(target, deluxe):
    os.makedirs(CACHE, exist_ok=True)
    idx_path = os.path.join(CACHE, "index.txt")
    if not os.path.exists(idx_path):
        extract_all_skins(idx_path)
    rows = [l.rstrip("\n").split("\t") for l in open(idx_path, encoding="utf-8") if l.strip()]
    mine = [r for r in rows if r[1].split(SKIN_DIR)[1].split("/")[0].lower() == target.lower()]
    if not mine:
        sys.exit("unknown skin '%s' - run with --list" % target)
    def pick(kind):
        c = []
        for cid, path, fn in mine:
            name = path.rsplit("/", 1)[1]
            isdel = "deluxe" in name.lower()
            if isdel != deluxe or not name.startswith("T_"):
                continue
            if kind == "mres" and "MRES" in name: c.append((cid, path, fn))
            elif kind == "icon" and name.endswith("Icon.uasset"): c.append((cid, path, fn))
            elif kind == "skin" and "MRES" not in name and not name.endswith("Icon.uasset"): c.append((cid, path, fn))
        return c[0] if c else None
    out = {}
    for k in ("skin", "mres", "icon"):
        p = pick(k)
        if p is None and k == "mres" and deluxe:     # deluxe variants share the base material map
            deluxe = False; p = pick(k); deluxe = True
        if p:
            out[k] = (bytes.fromhex(p[0]), p[1], open(os.path.join(CACHE, p[2]), "rb").read())
    if "skin" not in out:
        sys.exit("no %sskin texture for '%s'" % ("deluxe " if deluxe else "", target))
    out["header"] = open(os.path.join(CACHE, "containerheader.bin"), "rb").read()
    return out

def extract_all_skins(idx_path):
    sys.path.insert(0, HERE)
    from iostore import Toc
    print("first run: extracting original skin textures from the game...")
    t = Toc("Dungeons-Windows")
    lines = []
    for path, i in sorted(t.files.items()):
        if SKIN_DIR in path and path.rsplit("/", 1)[1].startswith("T_"):
            fn = path.rsplit("/", 1)[1]
            fn = path.split(SKIN_DIR)[1].replace("/", "__")
            open(os.path.join(CACHE, fn), "wb").write(t.read(i))
            lines.append("%s\t%s\t%s" % (t.ids[i].hex(), path, fn))
    hdr = [i for i, c in enumerate(t.ids) if c[11] == 6][0]
    open(os.path.join(CACHE, "containerheader.bin"), "wb").write(t.read(hdr))
    open(idx_path, "w", encoding="utf-8").write("\n".join(lines) + "\n")

MESH_PATH = "/Dungeons/Content/Spicewood/Art/Characters/Player/Master/SK_Player_Master.uasset"

def load_mesh():
    fn = os.path.join(CACHE, "SK_Player_Master.uasset")
    meta = os.path.join(CACHE, "SK_Player_Master.txt")
    if not os.path.exists(meta):
        sys.path.insert(0, HERE)
        from iostore import Toc
        t = Toc("Dungeons-Windows")
        path, i = [(p, i) for p, i in t.files.items() if p.endswith(MESH_PATH)][0]
        open(fn, "wb").write(t.read(i))
        open(meta, "w").write("%s\t%s" % (t.ids[i].hex(), path))
    cid, path = open(meta).read().split("\t")
    return bytes.fromhex(cid), path, open(fn, "rb").read()

# ------------------------------------------------------------- skin convert
def to_64x64(im):
    im = im.convert("RGBA")
    if im.size == (64, 32):  # legacy: mirror right limbs into the left-limb slots
        n = Image.new("RGBA", (64, 64)); n.paste(im, (0, 0))
        for (su, sv), (du, dv) in (((0, 16), (16, 48)), ((40, 16), (32, 48))):
            limb = im.crop((su, sv, su + 16, sv + 16))
            n.paste(mirror_limb(limb), (du, dv))
        im = n
    if im.size != (64, 64):
        sys.exit("skin must be 64x64 or 64x32, got %s" % (im.size,))
    return im

def mirror_limb(l):
    # 4x12x4 limb: flip each face horizontally and swap the side faces
    o = Image.new("RGBA", (16, 16))
    for (x, y, w, h), (dx, dy) in (((4, 0, 4, 4), (4, 0)), ((8, 0, 4, 4), (8, 0)), ((4, 4, 4, 12), (4, 4)),
                                   ((12, 4, 4, 12), (12, 4)), ((0, 4, 4, 12), (8, 4)), ((8, 4, 4, 12), (0, 4))):
        o.paste(l.crop((x, y, x + w, y + h)).transpose(Image.FLIP_LEFT_RIGHT), (dx, dy))
    return o

def is_slim(im):
    return im.getpixel((50, 16))[3] == 0 and im.getpixel((54, 20))[3] == 0

def classic_arm_to_slim(src, u, v):
    """Rebuild one 4px arm region (16x16 at u,v) as a 3px arm."""
    a = np.array(src.crop((u, v, u + 16, v + 16)))
    o = np.zeros_like(a)
    keep_f = [0, 1, 3]   # front/top/bottom: drop 3rd column (edges keep their detail)
    keep_b = [0, 2, 3]   # back is mirrored, drop its 2nd column
    o[0:4, 4:7] = a[0:4, 4:8][:, keep_f]      # top
    o[0:4, 7:10] = a[0:4, 8:12][:, keep_f]    # bottom
    o[4:16, 0:4] = a[4:16, 0:4]               # right side
    o[4:16, 4:7] = a[4:16, 4:8][:, keep_f]    # front
    o[4:16, 7:11] = a[4:16, 8:12]             # left side
    o[4:16, 11:14] = a[4:16, 12:16][:, keep_b]  # back
    return Image.fromarray(o)

ARMS = [(40, 16), (40, 32), (32, 48), (48, 48)]
OVERLAYS = [((0, 32), (0, 16)), ((16, 32), (16, 16)), ((40, 32), (40, 16)),   # pants, jacket, sleeve
            ((0, 48), (16, 48)), ((48, 48), (32, 48))]                         # left pants, left sleeve

def convert_skin(user, animated_face, blink=False):
    s = to_64x64(user)
    if not is_slim(s):
        for u, v in ARMS:
            s.paste(classic_arm_to_slim(s, u, v), (u, v))
    # bake outer layers (jacket/sleeves/pants) onto the base so nothing is lost
    # if the game mesh has no second layer there; keep them in place too.
    for (ou, ov), (bu, bv) in OVERLAYS:
        base = s.crop((bu, bv, bu + 16, bv + 16)); top = s.crop((ou, ov, ou + 16, ov + 16))
        base.alpha_composite(top); s.paste(base, (bu, bv))
    import remap
    table = full_remap_table()
    remap.apply(s, table)
    px = s.load()
    face = s.crop((8, 8, 16, 16)); hat = s.crop((40, 8, 48, 16))
    # the game draws brows/eyes/mouth from 8 "palette" texels in the top-left
    # corner (unused in normal skins). Clear the corner, then fill them.
    for y in range(8):
        for x in range(8):
            px[x, y] = (0, 0, 0, 0)
    f = face.load()
    def c(x, y): return f[x, y][:3]
    alpha = 255 if animated_face else 0
    # RGB is picked from the skin's own face so it blends in either way.
    pal = {(6, 5): c(2, 4), (7, 5): c(5, 4),      # irises (left, right)
           (6, 6): c(1, 4), (7, 6): c(6, 4),      # eye whites
           (3, 7): darkest(c(1, 3), c(2, 3)), (4, 7): darkest(c(5, 3), c(6, 3)),  # brows
           (6, 7): c(3, 6), (7, 7): c(4, 6)}      # mouth
    for (x, y), rgb in pal.items():
        px[x, y] = rgb + (alpha,)
    if blink:
        # the chosen eye pixels are drawn by the blinking eye quads instead of the
        # face texture: paint each quad's palette texel with the eye colour and
        # fill the face pixel with the nearest non-eye face colour
        import mesh_layers
        eyes = set(blink) if isinstance(blink, (list, tuple, set)) else set(detect_eyes(face))
        for (col, row) in eyes:
            tu, tv = mesh_layers.BLINK_EYES[(col, row)]
            px[tu, tv] = c(col, row) + (255,)
            px[8 + col, 8 + row] = fill_colour(f, eyes, col, row)
    # 8x8 face used by the portrait UI (hat layer composited on top)
    pf = face.copy(); pf.alpha_composite(hat)
    s.paste(pf, (56, 20))
    return s

def full_remap_table():
    """dest texel -> source texel: the game model's UV quirks (legs swapped/mirrored/
    top-bottom exchanged, head & hat backs mirrored) traced through 3D, plus the
    same fix for the pants overlay; the palette corner is left alone."""
    import remap
    table = {k: v for k, v in remap.build_table(load_mesh()[2]).items() if not (k[0] < 8 and k[1] < 8)}
    def pants_of(x, y):
        if 0 <= x < 16 and 16 <= y < 32: return x, y + 16          # MC right leg -> right pants
        if 16 <= x < 32 and 48 <= y < 64: return x - 16, y         # MC left leg  -> left pants
        return None
    for (x, y), src in list(table.items()):
        dst_o, src_o = pants_of(x, y), pants_of(*src)
        if dst_o and src_o:
            table[dst_o] = src_o
    for y in range(8, 16):                  # hat back mirrors like the head back
        for i in range(8):
            table[(56 + i, y)] = (63 - i, y)
    return table

def detect_eyes(face):
    """Guess 1-px eyes: clearly dark face pixels in rows 1..6 (the blink grid)."""
    f = face.load()
    lum = lambda p: 0.3 * p[0] + 0.59 * p[1] + 0.11 * p[2]
    vals = sorted(lum(f[x, y]) for y in range(8) for x in range(8) if f[x, y][3])
    med = vals[len(vals) // 2] if vals else 128
    dark = {(x, y) for y in range(2, 5) for x in range(1, 7)
            if f[x, y][3] and lum(f[x, y]) < min(70, med * 0.45)}
    return sorted(p for p in dark if (7 - p[0], p[1]) in dark)   # eyes come in mirrored pairs

def fill_colour(f, eyes, col, row):
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1), (2, 0), (-2, 0)):
        x, y = col + dx, row + dy
        if 0 <= x < 8 and 0 <= y < 8 and (x, y) not in eyes and f[x, y][3]:
            return f[x, y]
    return f[col, row]

def darkest(a, b):
    return a if sum(a) <= sum(b) else b

# ------------------------------------------------------------ texture patch
def find_mip(pkg, w, h, nbytes):
    tail = struct.pack("<iii", w, h, 1)
    o = pkg.rfind(tail)
    if o < nbytes:
        sys.exit("could not locate %dx%d mip data" % (w, h))
    return o - nbytes

def patch_bgra(pkg, img):
    o = find_mip(pkg, 64, 64, 16384)
    raw = img.tobytes("raw", "BGRA")
    return pkg[:o] + raw + pkg[o + 16384:]

def bc7_solid(r, g, b, a):
    """BC7 mode-6 block of one exact RGBA colour (7-bit endpoints + p-bit)."""
    bits, pos = 1 << 6, 7
    def put(v, n):
        nonlocal bits, pos
        bits |= v << pos; pos += n
    for c in (r, r, g, g, b, b, a, a):
        put(c >> 1, 7)
    # p-bits and indices stay 0, so each channel decodes to (c >> 1) << 1:
    # exact for even values.
    return bits.to_bytes(16, "little")

def patch_mres(pkg):
    # BC7 64x64 material map (R metal, G roughness, B emissive, A subsurface).
    # Rebuild it neutral: not metallic, hero-like roughness, no glow; skin
    # subsurface only on the head, as the game's own heroes have.
    o = find_mip(pkg, 64, 64, 4096)
    body = bc7_solid(0, 206, 0, 0)
    head = bc7_solid(0, 206, 0, 160)
    out = bytearray()
    for by in range(16):
        for bx in range(16):
            out += head if (by < 4 and bx < 8) else body   # head+hat rows 0..15, cols 0..31
    return pkg[:o] + bytes(out) + pkg[o + 4096:]
# DXT5 / BC3 encoder (simple bounding-box endpoints) for the UI icon
def bc3_encode(rgba):
    h, w, _ = rgba.shape
    out = bytearray()
    for by in range(0, h, 4):
        for bx in range(0, w, 4):
            b = rgba[by:by + 4, bx:bx + 4].reshape(16, 4).astype(np.int32)
            out += bc3_alpha(b[:, 3]) + bc1_color(b[:, :3])
    return bytes(out)

def bc3_alpha(a):
    a0, a1 = int(a.max()), int(a.min())
    if a0 == a1:
        return bytes([a0, a1]) + bytes(6)
    pal = [a0, a1] + [((7 - i) * a0 + i * a1) // 7 for i in range(1, 7)]
    idx = [int(np.argmin([abs(int(v) - p) for p in pal])) for v in a]
    bits = 0
    for i, k in enumerate(idx):
        bits |= k << (3 * i)
    return bytes([a0, a1]) + bits.to_bytes(6, "little")

def rgb565(c):
    return ((int(c[0]) * 31 + 127) // 255 << 11) | ((int(c[1]) * 63 + 127) // 255 << 5) | ((int(c[2]) * 31 + 127) // 255)

def from565(v):
    return np.array([((v >> 11) & 31) * 255 // 31, ((v >> 5) & 63) * 255 // 63, (v & 31) * 255 // 31])

def bc1_color(c):
    mean = c.mean(0); d = c - mean
    axis = np.linalg.svd(d, full_matrices=False)[2][0] if np.abs(d).sum() > 0 else np.array([1, 1, 1.0])
    t = d @ axis
    lo, hi = c[int(np.argmin(t))], c[int(np.argmax(t))]
    c0, c1 = rgb565(hi), rgb565(lo)
    if c0 < c1: c0, c1 = c1, c0
    if c0 == c1:
        return struct.pack("<HHI", c0, c1, 0)
    p0, p1 = from565(c0), from565(c1)
    pal = np.array([p0, p1, (2 * p0 + p1) // 3, (p0 + 2 * p1) // 3])
    idx = np.argmin(((c[:, None, :] - pal[None]) ** 2).sum(2), 1)
    bits = 0
    for i, k in enumerate(idx):
        bits |= int(k) << (2 * i)
    return struct.pack("<HHI", c0, c1, bits)

def patch_icon(pkg, icon):
    o = find_mip(pkg, 256, 256, 65536)
    return pkg[:o] + bc3_encode(np.array(icon)) + pkg[o + 65536:]

# ------------------------------------------------------------- icon render
def box_faces(u, v, w, h, d):
    return {"top": (u + d, v, w, d), "bottom": (u + d + w, v, w, d), "right": (u, v + d, d, h),
            "front": (u + d, v + d, w, h), "left": (u + d + w, v + d, d, h), "back": (u + 2 * d + w, v + d, w, h)}

def render_icon(skin, size=256, ss=4):
    S = size * ss
    tex = skin.load()
    # (uv origin, dims w h d, box min corner x y z, inflate)
    A = 3  # slim arms
    parts = [((0, 0), (8, 8, 8), (-4, 24, -4), 0), ((32, 0), (8, 8, 8), (-4, 24, -4), .5),
             ((16, 16), (8, 12, 4), (-4, 12, -2), 0), ((16, 32), (8, 12, 4), (-4, 12, -2), .25),
             ((40, 16), (A, 12, 4), (-4 - A, 12, -2), 0), ((40, 32), (A, 12, 4), (-4 - A, 12, -2), .25),
             ((32, 48), (A, 12, 4), (4, 12, -2), 0), ((48, 48), (A, 12, 4), (4, 12, -2), .25),
             ((0, 16), (4, 12, 4), (-4, 0, -2), 0), ((0, 32), (4, 12, 4), (-4, 0, -2), .25),
             ((16, 48), (4, 12, 4), (0, 0, -2), 0), ((0, 48), (4, 12, 4), (0, 0, -2), .25)]
    yaw, pitch = np.radians(28), np.radians(-12)
    Ry = np.array([[np.cos(yaw), 0, np.sin(yaw)], [0, 1, 0], [-np.sin(yaw), 0, np.cos(yaw)]])
    Rx = np.array([[1, 0, 0], [0, np.cos(pitch), -np.sin(pitch)], [0, np.sin(pitch), np.cos(pitch)]])
    R = Rx @ Ry
    light = np.array([0.35, 0.8, 0.5]); light /= np.linalg.norm(light)
    quads = []
    for (u, v), (w, h, d), (x0, y0, z0), inf in parts:
        x1, y1, z1 = x0 + w + inf, y0 + h + inf, z0 + d + inf
        x0, y0, z0 = x0 - inf, y0 - inf, z0 - inf
        sx, sy, sz = (x1 - x0) / w, (y1 - y0) / h, (z1 - z0) / d
        F = box_faces(u, v, w, h, d)
        # face -> (origin, step along texel i, step along texel j, normal)
        geo = {"front": ((x0, y1, z1), (sx, 0, 0), (0, -sy, 0), (0, 0, 1)),
               "back": ((x1, y1, z0), (-sx, 0, 0), (0, -sy, 0), (0, 0, -1)),
               "right": ((x0, y1, z0), (0, 0, sz), (0, -sy, 0), (-1, 0, 0)),
               "left": ((x1, y1, z1), (0, 0, -sz), (0, -sy, 0), (1, 0, 0)),
               "top": ((x0, y1, z0), (sx, 0, 0), (0, 0, sz), (0, 1, 0))}
        for name, (org, di, dj, n) in geo.items():
            n = R @ np.array(n, float)
            if n[2] <= 0:
                continue
            shade = 0.55 + 0.45 * max(0.0, float(n @ light))
            fu, fv, fw, fh = F[name]
            org, di, dj = np.array(org, float), np.array(di, float), np.array(dj, float)
            for j in range(fh):
                for i in range(fw):
                    r, g, b, a = tex[fu + i, fv + j]
                    if a < 128:
                        continue
                    p = org + di * i + dj * j
                    cs = [R @ q for q in (p, p + di, p + di + dj, p + dj)]
                    depth = sum(q[2] for q in cs) / 4 + (0.01 if inf else 0)
                    col = (int(r * shade), int(g * shade), int(b * shade), 255)
                    quads.append((depth, [(q[0], q[1]) for q in cs], col))
    quads.sort(key=lambda q: q[0])
    xs = [pt[0] for q in quads for pt in q[1]]; ys = [pt[1] for q in quads for pt in q[1]]
    scale = 0.86 * S / max(max(xs) - min(xs), max(ys) - min(ys))
    cx, cy = (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0)); dr = ImageDraw.Draw(img)
    for _, pts, col in quads:
        dr.polygon([(S / 2 + (x - cx) * scale, S / 2 - (y - cy) * scale) for x, y in pts], fill=col, outline=col)
    return img.resize((size, size), Image.LANCZOS)

# --------------------------------------------------------- container writer
def dir_index(mount, paths):
    """paths: list of (relative path, toc index). Returns FIoDirectoryIndexResource bytes."""
    strings, sidx = [], {}
    def s(x):
        if x not in sidx: sidx[x] = len(strings); strings.append(x)
        return sidx[x]
    dirs = [[0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF]]  # name, firstchild, sibling, firstfile
    files = []
    def child(di, name):
        c = dirs[di][1]
        while c != 0xFFFFFFFF:
            if strings[dirs[c][0]] == name: return c
            c = dirs[c][2]
        dirs.append([s(name), 0xFFFFFFFF, dirs[di][1], 0xFFFFFFFF]); dirs[di][1] = len(dirs) - 1
        return len(dirs) - 1
    for path, ti in paths:
        parts = path.split("/"); d = 0
        for p in parts[:-1]: d = child(d, p)
        files.append([s(parts[-1]), dirs[d][3], ti]); dirs[d][3] = len(files) - 1
    b = fstr(mount)
    b += struct.pack("<I", len(dirs)) + b"".join(struct.pack("<IIII", *x) for x in dirs)
    b += struct.pack("<I", len(files)) + b"".join(struct.pack("<III", *x) for x in files)
    b += struct.pack("<I", len(strings)) + b"".join(fstr(x) for x in strings)
    return b

def fstr(x):
    e = x.encode("ascii") + b"\0"
    return struct.pack("<i", len(e)) + e

def container_header(cid, packages, base):
    """packages: list of 8-byte package ids. Copies each package's store entry
    (imported packages / shader maps) from the game's own container header."""
    p = 16; n = struct.unpack_from("<i", base, p)[0]; p += 4
    ids = [base[p + 8 * i:p + 8 * i + 8] for i in range(n)]; p += 8 * n
    p += 4; eb = p
    entries, tails = [], []
    for pid in packages:
        i = ids.index(pid); e = eb + 16 * i
        ni, oi, ns, os_ = struct.unpack_from("<IIII", base, e)
        # array offsets are relative to each (count, offset) pair's own start
        imp = base[e + oi:e + oi + 8 * ni] if ni else b""
        shm = base[e + 8 + os_:e + 8 + os_ + 20 * ns] if ns else b""
        entries.append((ni, ns)); tails.append((imp, shm))
    k = len(packages); blob = bytearray(16 * k); tail = bytearray()
    for i, ((ni, ns), (imp, shm)) in enumerate(zip(entries, tails)):
        e = 16 * i
        io = 16 * k + len(tail); tail += imp
        so = 16 * k + len(tail); tail += shm
        struct.pack_into("<IIII", blob, e, ni, (io - e) if ni else 0, ns, (so - (e + 8)) if ns else 0)
    blob += tail
    h = struct.pack("<IIQ", 0x496F436E, 5, cid)
    h += struct.pack("<i", k) + b"".join(packages)
    h += struct.pack("<i", len(blob)) + bytes(blob)
    h += struct.pack("<ii", 0, 0)          # optional segment ids / entries
    h += struct.pack("<I", 0)              # redirects name batch (empty)
    h += struct.pack("<i", 0)              # localized packages
    h += struct.pack("<i", 0)              # package redirects
    off = len(h) + 16
    h += struct.pack("<qq", off, 4) + struct.pack("<I", 0)  # soft package references: none
    return h

def aes(data):
    from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
    key = bytes.fromhex(open(os.path.join(HERE, "aes.key")).read().strip())
    return Cipher(algorithms.AES(key), modes.ECB()).encryptor().update(data)

def chunk_hash(seed, data):
    """FIoStoreTocResource::HashChunkIdWithSeed (FNV-1a style, 64-bit)."""
    x = seed if seed else 0xCBF29CE484222325
    for b in data:
        x = ((x * 0x100000001B3) & 0xFFFFFFFFFFFFFFFF) ^ b
    return x

def write_container(base_path, chunks, paths, encrypt=False):
    """chunks: list of (12-byte chunk id, data). Uncompressed, unencrypted, indexed."""
    BS = 0x10000
    ucas = bytearray(); offlen = []; blocks = []; uoff = 0
    for cid, data in chunks:
        offlen.append((uoff, len(data)))
        for i in range(0, max(len(data), 1), BS):
            part = data[i:i + BS]
            blocks.append((len(ucas), len(part), len(part), 0))
            if encrypt:
                part = aes(part + bytes(-len(part) % 16))
            ucas += part
        uoff += (len(data) + BS - 1) // BS * BS
    di = dir_index("../../../", paths)
    if encrypt:
        di = aes(di + bytes(-len(di) % 16))
    n = len(chunks)
    # perfect-hash table (the engine won't mount a v8 TOC without one): pick a
    # seed-table size where every chunk lands in its own bucket, then point
    # each bucket straight at its slot with a negative seed.
    nseeds = n
    while len({chunk_hash(0, c) % nseeds for c, _ in chunks}) < n:
        nseeds += 1
    seeds = [0] * nseeds
    for i, (c, _) in enumerate(chunks):
        seeds[chunk_hash(0, c) % nseeds] = -i - 1
    cont_id = struct.unpack("<Q", chunks[-1][0][:8])[0]
    hdr = bytearray(144)
    hdr[0:16] = b"-==--==--==--==-"
    struct.pack_into("<B3xIIIIIIIII", hdr, 16, 8, 144, n, len(blocks), 12, 0, 32, BS, len(di), 1)
    struct.pack_into("<Q", hdr, 0x38, cont_id)
    hdr[0x50] = 8 | (2 if encrypt else 0)           # Indexed (+Encrypted)
    struct.pack_into("<IQI", hdr, 0x54, nseeds, 0xFFFFFFFFFFFFFFFF, 0)
    t = bytes(hdr)
    t += b"".join(c for c, _ in chunks)
    t += b"".join(o.to_bytes(5, "big") + l.to_bytes(5, "big") for o, l in offlen)
    t += struct.pack("<%di" % nseeds, *seeds)
    t += b"".join(o.to_bytes(5, "little") + c.to_bytes(3, "little") + u.to_bytes(3, "little") + bytes([m])
                  for o, c, u, m in blocks)
    t += di
    t += b"".join(hashlib.sha1(d).digest() + b"\0\0\0\0" for _, d in chunks)  # meta (hash is informational)
    open(base_path + ".utoc", "wb").write(t)
    open(base_path + ".ucas", "wb").write(bytes(ucas))

def write_empty_pak(path):
    """A v11 .pak with no files: UE only mounts a .utoc/.ucas pair that has a .pak next to it."""
    body = b""
    fdi = struct.pack("<i", 1) + fstr("/") + struct.pack("<i", 0)
    phi = struct.pack("<ii", 0, 0)
    idx_off = len(body)
    prim_len_guess = len(fstr("../../../")) + 4 + 8 + 4 + 16 + 20 + 4 + 16 + 20 + 4 + 4
    phi_off = idx_off + prim_len_guess; fdi_off = phi_off + len(phi)
    prim = fstr("../../../") + struct.pack("<iQ", 0, 0)
    prim += struct.pack("<Iqq", 1, phi_off, len(phi)) + hashlib.sha1(phi).digest()
    prim += struct.pack("<Iqq", 1, fdi_off, len(fdi)) + hashlib.sha1(fdi).digest()
    prim += struct.pack("<ii", 0, 0)
    assert len(prim) == prim_len_guess
    foot = bytes(16) + b"\0" + struct.pack("<Iiqq", 0x5A6F12E1, 11, idx_off, len(prim)) + hashlib.sha1(prim).digest() + bytes(160)
    open(path, "wb").write(body + prim + phi + fdi + foot)
