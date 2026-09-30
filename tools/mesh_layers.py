"""
Adds Minecraft "second layer" geometry (jacket, sleeves, pants) to the
Dungeons II player mesh SK_Player_Master, whose cooked data is fully inline.

The base body/arm/leg boxes are duplicated, pushed outward, re-pointed at the
outer-layer UV regions and given the same bone weights, so they animate with
the limb they wrap. Transparent skin pixels stay invisible (masked material),
exactly like the hat layer the game already has.
"""
import struct
import numpy as np

UNIT = 6.25      # mesh units per skin pixel (head is 8px = 50 units)
INFLATE = 0.25   # outer layer offset in pixels (Minecraft uses 0.25)

# The game already has a separate head/hat shell.  Its source geometry is
# inflated by 0.25 px, whereas Minecraft's hat layer is 0.50 px from the
# head.  Keep body overlays at INFLATE and move only this existing shell by
# the remaining 0.25 px.
HAT_UV = (32, 0, 64, 16)
HAT_EXTRA_INFLATE = 0.25

# base UV rect (u0, v0, u1, v1) -> overlay UV offset (du, dv)
PARTS = [((16, 16, 40, 32), (0, 16)),    # body      -> jacket
         ((40, 16, 54, 32), (0, 16)),    # right arm -> right sleeve (slim arm is 14px wide)
         ((32, 48, 46, 64), (16, 0)),    # left arm  -> left sleeve
         ((0, 16, 16, 32), (0, 16)),     # right leg -> right pants
         ((16, 48, 32, 64), (-16, 0))]   # left leg  -> left pants

# Offsets in the cooked package (validated against expected values below so a
# game update that moves anything makes this refuse instead of corrupting).
O = dict(sec_tris=5131, sec_verts=5186, buf_size=5258, idx_count=5269, idx_data=5273,
         pos_n1=9897, pos_n2=9905, smvb_n=17211, tan_n=17227, uv_n=22099,
         skin_bones=24545, skin_n=24549, skin_bytes=24565, look_n1=29453, look_n2=29461,
         export_size=992 + 72 + 8)
EXPECT = dict(sec_tris=770, sec_verts=608, idx_count=2310, pos_n1=608, pos_n2=608, smvb_n=608,
              tan_n=608, uv_n=608, skin_bones=2432, skin_n=608, skin_bytes=4864, look_n1=608, look_n2=608)

# Blink-eye grid: one 1-px quad in front of every face pixel in rows 1..6. Each
# reads its own palette texel from the (otherwise unused) top-left corner, which
# stock skins leave transparent, so a skin opts pixels in by painting texels.
# The game's eye bones squash toward z = 27 px (bottom edge of face row 4); the
# quad weights (eye bone vs head) are chosen so each pixel closes onto its own
# edge nearest that line instead of sliding toward it.
USED_PALETTE = {(6, 5), (7, 5), (6, 6), (7, 6), (3, 6), (4, 6), (3, 7), (4, 7), (6, 7), (7, 7)}
FREE_TEXELS = [(x, y) for y in range(8) for x in range(8) if (x, y) not in USED_PALETTE]
BLINK_ROWS = range(1, 7)
BLINK_EYES = {}   # (face col, face row) -> palette texel
for _i, (_r, _c) in enumerate([(r, c) for r in BLINK_ROWS for c in range(8)]):
    BLINK_EYES[(_c, _r)] = FREE_TEXELS[_i]
# second grid just in front of the hat (outer) layer, for eyes drawn on the outer
# layer; its texels are a block Minecraft skins never use (all 28 stock skins leave
# it transparent and no geometry samples it): (24 + col, row)
BLINK_OUTER = {(c, r): (24 + c, r) for r in BLINK_ROWS for c in range(8)}
EYE_BONE = {-1: 25, 1: 23}            # skeleton eye bone for the -X / +X side of the face
HEAD_BONE = 22
EYE_PIVOT_Z = 27.0                     # px, where the eye bones' blink squash converges
SEC_MAX_INFLUENCES = 5190              # render section MaxBoneInfluences (1 in the stock mesh)
def u32(b, o): return struct.unpack_from("<I", b, o)[0]

def inflate_existing_shell(pos, uv, idx, uv_rect, extra_inflate):
    """Move one existing UV-isolated shell outwards without touching its base mesh."""
    u0, v0, u1, v1 = uv_rect
    centres = uv[idx].mean(1)
    tris = idx[(centres[:, 0] > u0) & (centres[:, 0] < u1) &
               (centres[:, 1] > v0) & (centres[:, 1] < v1)]
    if not len(tris):
        raise ValueError("could not find the hat shell in SK_Player_Master")
    verts = sorted(set(tris.flatten().tolist()))
    centre = pos[verts].mean(0)
    normals = {}
    for tri in tris:
        a, b, c = pos[tri]
        normal = np.cross(b - a, c - a)
        if np.linalg.norm(normal) < 1e-9:
            continue
        normal /= np.linalg.norm(normal)
        if np.dot(normal, (a + b + c) / 3 - centre) < 0:
            normal = -normal
        for vertex in tri:
            normals.setdefault(vertex, normal)
    groups = {}
    for vertex in verts:
        groups.setdefault(tuple(np.round(pos[vertex], 3)), []).append(vertex)
    for vertices in groups.values():
        unique_normals = []
        for vertex in vertices:
            normal = normals.get(vertex)
            if normal is not None and not any(np.dot(normal, other) > 0.99 for other in unique_normals):
                unique_normals.append(normal)
        if unique_normals:
            offset = sum(unique_normals) * extra_inflate * UNIT
            for vertex in vertices:
                pos[vertex] += offset

def add_layers(pkg, mode="full", layers=True, blink=True):
    if mode == "orig":
        return pkg, (0, 0)
    b = bytearray(pkg)
    for k, v in EXPECT.items():
        if u32(b, O[k]) != v:
            raise ValueError("SK_Player_Master layout changed (%s=%d, expected %d)" % (k, u32(b, O[k]), v))
    NV, NI = 608, 2310
    idx = np.frombuffer(b, "<u2", NI, O["idx_data"]).reshape(-1, 3).copy()
    p_pos = O["pos_n2"] + 4
    pos = np.frombuffer(b, "<f4", NV * 3, p_pos).reshape(-1, 3).astype(np.float64)
    p_tan = O["tan_n"] + 4
    tan = np.frombuffer(b, np.uint8, NV * 8, p_tan).reshape(-1, 8).copy()
    p_uv = O["uv_n"] + 4
    uv = np.frombuffer(b, "<f2", NV * 2, p_uv).reshape(-1, 2).astype(np.float32) * 64
    p_sk = O["skin_bytes"] + 4
    skin = np.frombuffer(b, np.uint8, NV * 8, p_sk).reshape(-1, 8).copy()
    p_lk = O["look_n2"] + 4
    look = np.frombuffer(b, "<u4", NV, p_lk).copy()

    inflate_existing_shell(pos, uv, idx, HAT_UV, HAT_EXTRA_INFLATE)

    new_pos, new_tan, new_uv, new_skin, new_look, new_tris = [], [], [], [], [], []
    nv = NV
    orig_of = {}
    for (u0, v0, u1, v1), (du, dv) in (PARTS if layers else []):
        cen = uv[idx].mean(1)
        tris = idx[(cen[:, 0] > u0) & (cen[:, 0] < u1) & (cen[:, 1] > v0) & (cen[:, 1] < v1)]
        verts = sorted(set(tris.flatten().tolist()))
        c = pos[verts].mean(0)
        # outward face normal per vertex, from its triangles
        vn = {}
        for t in tris:
            a, bb, cc = pos[t]
            n = np.cross(bb - a, cc - a)
            if np.linalg.norm(n) < 1e-9:
                continue
            n /= np.linalg.norm(n)
            if np.dot(n, (a + bb + cc) / 3 - c) < 0:
                n = -n
            for v in t:
                vn.setdefault(v, n)
        # vertices sharing a position (box corners/edges) get the sum of the
        # distinct face normals there, so the shell grows without seams
        groups = {}
        for v in verts:
            groups.setdefault(tuple(np.round(pos[v], 3)), []).append(v)
        remap = {}
        for key, vs in groups.items():
            ns = []
            for v in vs:
                n = vn.get(v)
                if n is not None and not any(np.dot(n, m) > 0.99 for m in ns):
                    ns.append(n)
            off = sum(ns) * INFLATE * UNIT if ns else np.zeros(3)
            for v in vs:
                orig_of[nv] = v
                remap[v] = nv
                new_pos.append(pos[v] + off)
                new_tan.append(tan[v])
                new_uv.append(uv[v] + (du, dv))
                new_skin.append(skin[v])
                new_look.append(look[v])
                nv += 1
        new_tris += [[remap[v] for v in t] for t in tris]

    if blink:
        cen = uv[idx].mean(1)
        ref = idx[(cen[:, 0].astype(int) == 6) & (cen[:, 1].astype(int) == 5)]   # a game iris quad
        P = pos[ref[0]]; nref = np.cross(P[1] - P[0], P[2] - P[0]); src = int(ref[0][0])
        bonemap = list(struct.unpack_from("<13H", b, 5160))
        head = bonemap.index(HEAD_BONE)
        grids = [(BLINK_EYES, 4.013 * UNIT),      # in front of the face, behind the hat layer
                 (BLINK_OUTER, 4.10 * UNIT)]      # just in front of the hat layer (~4.064 px)
        for (col, row), (tu, tv), y in [(k, v, gy) for grid, gy in grids for k, v in grid.items()]:
            x0, x1 = (-4 + col) * UNIT, (-3 + col) * UNIT
            zb, zt = 31 - row, 32 - row
            eye = bonemap.index(EYE_BONE[-1 if col < 4 else 1])
            # weight on the eye bone so the moving edge lands on the static one at s=0
            if zb >= EYE_PIVOT_Z:   wt, wb = 1.0 / (zt - EYE_PIVOT_Z), 0.0
            else:                   wt, wb = 0.0, 1.0 / (EYE_PIVOT_Z - zb)
            def rec(w):
                we = int(round(255 * w))
                if we <= 0: return np.array([head, 0, 0, 0, 255, 0, 0, 0], np.uint8)
                if we >= 255: return np.array([eye, 0, 0, 0, 255, 0, 0, 0], np.uint8)
                return np.array([eye, head, 0, 0, we, 255 - we, 0, 0], np.uint8)
            corners = [(x0, y, zt * UNIT), (x1, y, zt * UNIT), (x1, y, zb * UNIT), (x0, y, zb * UNIT)]
            recs = [rec(wt), rec(wt), rec(wb), rec(wb)]
            base = nv
            for cpos, r in zip(corners, recs):
                new_pos.append(np.array(cpos)); new_tan.append(tan[src]); new_uv.append(np.array([tu + .5, tv + .5]))
                new_skin.append(r); new_look.append(look[src]); nv += 1
            q = [[base, base + 1, base + 2], [base, base + 2, base + 3]]
            A = np.array(corners)
            if np.dot(np.cross(A[1] - A[0], A[2] - A[0]), nref) < 0:
                q = [[t[0], t[2], t[1]] for t in q]
            new_tris += q
    if mode == "verts_only":
        new_tris = []
    elif mode == "tris_only":
        inv = {}
        for (u0, v0, u1, v1), _ in PARTS:
            pass
        back = {}
        for t in new_tris:
            pass
        new_tris = [[orig_of[v] for v in t] for t in new_tris]
        new_pos, new_tan, new_uv, new_skin, new_look = [], [], [], [], []
        nv = NV
    k = nv - NV
    if nv > 65535:
        raise ValueError("too many vertices for 16-bit indices")
    add_idx = np.array(new_tris, "<u2").tobytes()
    add_pos = np.array(new_pos, "<f4").tobytes()
    add_tan = np.array(new_tan, np.uint8).tobytes()
    add_uv = (np.array(new_uv, np.float32) / 64).astype("<f2").tobytes()
    add_skin = np.array(new_skin, np.uint8).tobytes()
    add_look = np.array(new_look, "<u4").tobytes()

    # counts first (offsets are all before any insertion point they describe)
    nt = len(new_tris)
    struct.pack_into("<I", b, O["sec_tris"], 770 + nt)
    struct.pack_into("<I", b, O["sec_verts"], nv)
    if blink and mode == "full":
        struct.pack_into("<i", b, SEC_MAX_INFLUENCES, max(2, struct.unpack_from("<i", b, SEC_MAX_INFLUENCES)[0]))
    struct.pack_into("<I", b, O["idx_count"], NI + 3 * nt)
    for f in ("pos_n1", "pos_n2", "smvb_n", "tan_n", "uv_n", "skin_n", "look_n1", "look_n2"):
        struct.pack_into("<I", b, O[f], nv)
    struct.pack_into("<I", b, O["skin_bones"], 4 * nv)
    struct.pack_into("<I", b, O["skin_bytes"], 8 * nv)
    added = len(add_idx) + len(add_pos) + len(add_tan) + len(add_uv) + len(add_skin) + len(add_look)
    struct.pack_into("<I", b, O["buf_size"], u32(b, O["buf_size"]) + added)
    struct.pack_into("<Q", b, O["export_size"], struct.unpack_from("<Q", b, O["export_size"])[0] + added)

    # insert data at the end of each buffer, back to front so offsets stay valid
    inserts = [(p_lk + 4 * NV, add_look), (p_sk + 8 * NV, add_skin), (p_uv + 4 * NV, add_uv),
               (p_tan + 8 * NV, add_tan), (p_pos + 12 * NV, add_pos), (O["idx_data"] + 2 * NI, add_idx)]
    for at, data in inserts:
        b[at:at] = data
    return bytes(b), (k, nt)

def read_mesh(pkg):
    """(positions, uvs*64, triangles) of the (possibly patched) mesh, for previews."""
    b = pkg
    ni = u32(b, O["idx_count"])
    idx = np.frombuffer(b, "<u2", ni, O["idx_data"]).reshape(-1, 3)
    shift = 2 * ni - 2 * 2310
    nv = u32(b, O["pos_n2"] + shift)
    pos = np.frombuffer(b, "<f4", nv * 3, O["pos_n2"] + 4 + shift).reshape(-1, 3)
    uv_n = O["uv_n"] + shift + (12 * nv - 12 * 608) + (8 * nv - 8 * 608)
    uv = np.frombuffer(b, "<f2", nv * 2, uv_n + 4).reshape(-1, 2).astype(np.float32) * 64
    return pos, uv, idx
