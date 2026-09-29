"""
Freezes everything Dungeons 2 Skin Loader needs from the game into md2data.bin.

No original artwork ships: every hero texture package is included with its
pixel payload zeroed (the app always writes a full replacement), and the icon
and material maps likewise. The player model ships pre-patched (outer layers +
blink-eye grid), as the app's only mesh.

Layout (little endian; str = u16 length + utf-8; blob = u32 length + bytes):
  "MD2S" u32 version
  u64 game utoc size, u64 game container id            (version fingerprint)
  32 bytes AES key
  u32 hero count, per hero: str name, u8 deluxe,
      3x package (skin, mres, icon): 12 chunk id, str path, blob template,
                                      u32 pixel offset, u32 imports, 8*n ids
  2x mesh (with layers, without): 12 chunk id, str path, blob package, u32 imports, 8*n ids
  u32 remap count, per entry 4 bytes dx dy sx sy
  u32 blink count, per entry 4 bytes col row tu tv
  2x icon geometry (with layers, without):
      u32 nv, nv*3 f32 pos (px), nv*2 f32 uv (px), u32 nt, nt*3 u16, nt*3 f32 normals
  16 bytes MRES body block, 16 bytes MRES head block
"""
import os, struct, sys
import numpy as np
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import build_skin_mod as B, mesh_layers, icon_render

def s(x):
    e = x.encode("utf-8"); return struct.pack("<H", len(e)) + e
def blob(x): return struct.pack("<I", len(x)) + x

hdr = open(os.path.join(B.CACHE, "containerheader.bin"), "rb").read()
def imports_of(pid):
    p = 16; n = struct.unpack_from("<i", hdr, p)[0]; p += 4
    ids = [hdr[p + 8 * i:p + 8 * i + 8] for i in range(n)]; p += 8 * n + 4
    e = p + 16 * ids.index(pid)
    ni, oi, ns, _ = struct.unpack_from("<IIII", hdr, e)
    assert ns == 0, "shader maps not supported"
    return [hdr[e + oi + 8 * k:e + oi + 8 * k + 8] for k in range(ni)]

utoc = os.path.join(B.PAKS, "Dungeons-Windows.utoc")
out = bytearray(b"MD2S" + struct.pack("<I", 3))
out += struct.pack("<QQ", os.path.getsize(utoc), struct.unpack_from("<Q", open(utoc, "rb").read(0x40), 0x38)[0])
from iostore import KEY  # the games AES key (MD2_AES_KEY or tools/aes.key)
out += KEY

rows = [l.rstrip("\n").split("\t") for l in open(os.path.join(B.CACHE, "index.txt"), encoding="utf-8") if l.strip()]
heroes = sorted({(r[1].split(B.SKIN_DIR)[1].split("/")[0], "deluxe" in r[1].rsplit("/", 1)[1].lower()) for r in rows})
hero_blobs = []; originals = []
for name, dlx in heroes:
    o = B.load_originals(name, dlx)
    if not all(k in o for k in ("skin", "mres", "icon")):
        print("skip", name, dlx); continue
    part = s(name) + bytes([dlx])
    for kind, (w, h, n) in (("skin", (64, 64, 16384)), ("mres", (64, 64, 4096)), ("icon", (256, 256, 65536))):
        cid, path, pkg = o[kind]
        off = B.find_mip(pkg, w, h, n)
        tmpl = pkg[:off] + bytes(n) + pkg[off + n:]
        imp = imports_of(cid[:8])
        part += cid + s(path.replace("../../../", "")) + blob(tmpl) + struct.pack("<II", off, len(imp)) + b"".join(imp)
    hero_blobs.append(part)
    # small preview of the stock hero (the game's own skin, game face on)
    cid_s, _, spkg = o["skin"]
    so = B.find_mip(spkg, 64, 64, 16384)
    from PIL import Image
    orig = Image.frombytes("RGBA", (64, 64), spkg[so:so + 16384], "raw", "BGRA")
    originals.append(orig)
    print("hero", name, "(deluxe)" if dlx else "")
out += struct.pack("<I", len(hero_blobs)) + b"".join(hero_blobs)

cid, path, base_mesh = B.load_mesh()
meshes = {}
for layers in (True, False):
    m, _ = mesh_layers.add_layers(base_mesh, layers=layers, blink=True)
    meshes[layers] = m
    imp = imports_of(cid[:8])
    out += cid + s(path.replace("../../../", "")) + blob(m) + struct.pack("<I", len(imp)) + b"".join(imp)

table = B.full_remap_table()
out += struct.pack("<I", len(table)) + b"".join(bytes([dx, dy, sx, sy]) for (dx, dy), (sx, sy) in sorted(table.items()))
out += struct.pack("<I", len(mesh_layers.BLINK_EYES)) + b"".join(bytes([c, r, tu, tv]) for (c, r), (tu, tv) in sorted(mesh_layers.BLINK_EYES.items()))

import icon_pose
for layers in (True, False):
    pos, uv, tris, nrm = icon_pose.posed(meshes[layers])     # the game's locker pose (fitted)
    out += struct.pack("<I", len(pos)) + pos.astype("<f4").tobytes() + uv.astype("<f4").tobytes()
    out += struct.pack("<I", len(tris)) + tris.astype("<u2").tobytes() + nrm.astype("<f4").tobytes()

out += B.bc7_solid(0, 206, 0, 0) + B.bc7_solid(0, 206, 0, 160)
# v2: preview renders of the original heroes (PNG, same order as the heroes), locker pose + camera
import io
for orig in originals:
    im = icon_pose.render(orig, meshes[False], 192)
    buf = io.BytesIO(); im.save(buf, "PNG")
    out += blob(buf.getvalue())
# v3: outer-layer blink grid, then the fitted icon camera
out += struct.pack("<I", len(mesh_layers.BLINK_OUTER)) + b"".join(bytes([c, r, tu, tv]) for (c, r), (tu, tv) in sorted(mesh_layers.BLINK_OUTER.items()))
cam = icon_pose.camera()
out += struct.pack("<I", len(cam)) + struct.pack("<%df" % len(cam), *cam)
dst = os.path.join(HERE, "..", "data", "md2data.bin")
os.makedirs(os.path.dirname(dst), exist_ok=True)
open(dst, "wb").write(out)
print("wrote", dst, len(out), "bytes,", len(hero_blobs), "hero skins")
