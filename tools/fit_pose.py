"""
Recover the locker-icon pose: render stock heroes with their own textures on the
stock mesh, and optimise camera + limb rotations until the renders match the
game's official icons (silhouette + colour).
"""
import sys, os, struct, json
import numpy as np
from PIL import Image
from scipy.optimize import minimize
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_skin_mod as B, mesh_layers, icon_render

RES = 96
HEROES = ["Greta", "Darian", "Javier", "_Mint"]

def dxt5(data, w, h):
    out = np.zeros((h, w, 4), np.uint8); bi = 0
    for by in range(0, h, 4):
        for bx in range(0, w, 4):
            b = data[bi:bi + 16]; bi += 16
            a0, a1 = b[0], b[1]; ab = int.from_bytes(b[2:8], "little")
            al = [a0, a1] + ([((7 - i) * a0 + i * a1) // 7 for i in range(1, 7)] if a0 > a1 else [((5 - i) * a0 + i * a1) // 5 for i in range(1, 5)] + [0, 255])
            c0, c1 = struct.unpack_from("<HH", b, 8); ci = struct.unpack_from("<I", b, 12)[0]
            rgb = lambda c: [((c >> 11) & 31) * 255 // 31, ((c >> 5) & 63) * 255 // 63, (c & 31) * 255 // 31]
            p0, p1 = rgb(c0), rgb(c1); pal = [p0, p1, [(2 * x + y) // 3 for x, y in zip(p0, p1)], [(x + 2 * y) // 3 for x, y in zip(p0, p1)]]
            for i in range(16):
                out[by + i // 4, bx + i % 4, :3] = pal[(ci >> (2 * i)) & 3]; out[by + i // 4, bx + i % 4, 3] = al[(ab >> (3 * i)) & 7]
    return out

def load_hero(name):
    o = B.load_originals(name, False)
    d = o["skin"][2]; so = B.find_mip(d, 64, 64, 16384)
    tex = np.array(Image.frombytes("RGBA", (64, 64), d[so:so + 16384], "raw", "BGRA"))
    d = o["icon"][2]; io = B.find_mip(d, 256, 256, 65536)
    icon = Image.fromarray(dxt5(d[io:io + 65536], 256, 256)).resize((RES, RES), Image.BOX)
    return tex, np.array(icon).astype(np.float32)

base = mesh_layers.read_mesh(B.load_mesh()[2])
POS = base[0].astype(np.float64) / 6.25; UV = base[1]; TRIS = base[2]
CEN = UV[TRIS].mean(1)
def part(rects):
    sel = np.zeros(len(TRIS), bool)
    for u0, v0, u1, v1 in rects:
        sel |= (CEN[:, 0] > u0) & (CEN[:, 0] < u1) & (CEN[:, 1] > v0) & (CEN[:, 1] < v1)
    return np.unique(TRIS[sel])
PARTS = {  # name: (vertex ids, pivot)
    "head": (part([(0, 0, 64, 16)]), (0, 0, 24.0)),
    "rarm": (part([(40, 16, 56, 32)]), (-5.0, 0, 22.0)),
    "larm": (part([(32, 48, 48, 64)]), (5.0, 0, 22.0)),
    "legA": (part([(0, 16, 16, 32)]), (2.0, 0, 12.0)),     # +X leg (game swaps legs)
    "legB": (part([(16, 48, 32, 64)]), (-2.0, 0, 12.0)),
}
BODY_ALL = np.arange(len(POS))

def R(rx, ry, rz):
    return icon_render.rot((0, 0, 1), rz) @ icon_render.rot((0, 1, 0), ry) @ icon_render.rot((1, 0, 0), rx)

NAMES = ["cam_yaw", "cam_pitch", "cam_roll", "persp", "scale", "offx", "offy", "body_rx", "body_rz",
         "head_rx", "head_ry", "head_rz", "rarm_rx", "rarm_ry", "rarm_rz", "larm_rx", "larm_ry", "larm_rz",
         "legA_rx", "legA_ry", "legA_rz", "legB_rx", "legB_ry", "legB_rz"]

def posed(p):
    P = dict(zip(NAMES, p)); pos = POS.copy()
    for k in ("head", "rarm", "larm", "legA", "legB"):
        vs, piv = PARTS[k]; piv = np.array(piv)
        pos[vs] = (pos[vs] - piv) @ R(P[k + "_rx"], P[k + "_ry"], P[k + "_rz"]).T + piv
    # whole-body lean around the hips
    pos = (pos - np.array([0, 0, 12.0])) @ R(P["body_rx"], 0, P["body_rz"]).T + np.array([0, 0, 12.0])
    return pos, P

def render(p, tex, res=RES):
    pos, P = posed(p)
    M = icon_render.rot((1, 0, 0), P["cam_pitch"]) @ icon_render.rot((0, 1, 0), P["cam_roll"]) @ icon_render.rot((0, 0, 1), P["cam_yaw"])
    v = (pos - np.array([0, 0, 16.0])) @ M.T
    dist = 40.0 + 400.0 * (1 - np.clip(P["persp"], 0, 1))
    f = dist / (dist + (-v[:, 1]))           # simple perspective: depth along -Y is away
    sx = v[:, 0] * f; sy = -v[:, 2] * f; dp = -v[:, 1]
    s = P["scale"] * res / 36.0
    sx = sx * s + res / 2 + P["offx"] * res; sy = sy * s + res / 2 + P["offy"] * res
    img = np.zeros((res, res, 4), np.float32); zb = np.full((res, res), np.inf)
    th, tw = tex.shape[:2]
    for t in TRIS:
        x, y, z = sx[t], sy[t], dp[t]
        x0, x1 = max(0, int(x.min())), min(res - 1, int(x.max()) + 1)
        y0, y1 = max(0, int(y.min())), min(res - 1, int(y.max()) + 1)
        if x1 < x0 or y1 < y0: continue
        d = (y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2])
        if abs(d) < 1e-9: continue
        X, Y = np.meshgrid(np.arange(x0, x1 + 1) + .5, np.arange(y0, y1 + 1) + .5)
        l0 = ((y[1] - y[2]) * (X - x[2]) + (x[2] - x[1]) * (Y - y[2])) / d
        l1 = ((y[2] - y[0]) * (X - x[2]) + (x[0] - x[2]) * (Y - y[2])) / d
        l2 = 1 - l0 - l1
        m = (l0 >= -1e-4) & (l1 >= -1e-4) & (l2 >= -1e-4)
        if not m.any(): continue
        Z = l0 * z[0] + l1 * z[1] + l2 * z[2]
        U = l0 * UV[t[0], 0] + l1 * UV[t[1], 0] + l2 * UV[t[2], 0]
        V = l0 * UV[t[0], 1] + l1 * UV[t[1], 1] + l2 * UV[t[2], 1]
        c = tex[np.clip((V / 64 * th).astype(int), 0, th - 1), np.clip((U / 64 * tw).astype(int), 0, tw - 1)]
        m &= c[..., 3] > 127
        sub = zb[y0:y1 + 1, x0:x1 + 1]; m &= Z < sub
        if not m.any(): continue
        sub[m] = Z[m]; img[y0:y1 + 1, x0:x1 + 1][m] = c[m]
    return img

def loss(p, data):
    tot = 0
    for tex, icon in data:
        r = render(p, tex)
        ar, at = r[..., 3] / 255, icon[..., 3] / 255
        sil = np.abs(ar - at).mean()
        both = (ar > .5) & (at > .5)
        col = 0
        if both.any():
            a = r[both][:, :3]; b = icon[both][:, :3]
            k = (a * b).sum() / max((a * a).sum(), 1)       # best overall brightness
            col = np.abs(a * k - b).mean() / 255
        tot += sil + 0.35 * col
    return tot / len(data)

if __name__ == "__main__":
    data = [load_hero(h) for h in HEROES[:2]]
    p0 = np.array([-22, -12, 0, 0.3, 1.0, 0, 0.02, 0, 0, 0, 0, 0, 16, 25, 0, -14, -25, 0, 10, -10, 0, -8, 10, 0], float)
    best = [loss(p0, data), p0]
    print("start", best[0])
    it = [0]
    def cb(xk):
        it[0] += 1
    for rnd in range(3):
        res = minimize(loss, best[1], args=(data,), method="Powell", callback=cb,
                       options={"maxiter": 3, "xtol": 0.05, "ftol": 1e-4})
        print("round", rnd, res.fun)
        if res.fun < best[0]: best = [res.fun, res.x]
    p = best[1]
    json.dump(dict(zip(NAMES, [float(v) for v in p])), open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "icon_pose.json"), "w"), indent=1)
    # comparison sheet over all test heroes
    sheet = Image.new("RGBA", (RES * 2 * len(HEROES), RES), (60, 60, 60, 255))
    for i, h in enumerate(HEROES):
        tex, icon = load_hero(h)
        sheet.alpha_composite(Image.fromarray(icon.astype(np.uint8)), (RES * 2 * i, 0))
        sheet.alpha_composite(Image.fromarray(render(p, tex).astype(np.uint8)), (RES * 2 * i + RES, 0))
    sheet.resize((sheet.width * 2, sheet.height * 2), Image.NEAREST).save("pose_fit.png")
    print("loss on all:", loss(p, [load_hero(h) for h in HEROES]))
    print(json.dumps(dict(zip(NAMES, [round(float(v), 2) for v in p]))))
