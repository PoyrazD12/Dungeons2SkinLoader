"""
Locker icon renderer: poses the game's own player model the way the game's hero
icons are posed (3/4 view, arms slightly out, feet slightly apart) and renders
it with soft shading into a 256x256 RGBA image.
"""
import numpy as np
from PIL import Image
import mesh_layers

UNIT = 6.25
# part -> (uv rects incl. outer layer, pivot in pixels, rotation axis, angle degrees)
# game model axes: X = character's left, Y = front, Z = up. Right arm sits at -X.
POSE = {  # part: (uv rects, pivot, [(axis, degrees), ...] applied in order)
    "rarm": ([(40, 16, 56, 32), (40, 32, 56, 48)], (-4.0, 0, 23.0), [((0, 1, 0), 27), ((1, 0, 0), 16)]),
    "larm": ([(32, 48, 48, 64), (48, 48, 64, 64)], (4.0, 0, 23.0), [((0, 1, 0), -25), ((1, 0, 0), -14)]),
    # the game's legs are swapped: the "right leg" UVs sit on the +X leg
    "legA": ([(0, 16, 16, 32), (0, 32, 16, 48)], (2.0, 0, 12.0), [((0, 1, 0), -11), ((1, 0, 0), 10)]),
    "legB": ([(16, 48, 32, 64), (0, 48, 16, 64)], (-2.0, 0, 12.0), [((0, 1, 0), 11), ((1, 0, 0), -8)]),
}
def rot(axis, deg):
    a = np.radians(deg); x, y, z = axis; c, s = np.cos(a), np.sin(a)
    return np.array([[c + x*x*(1-c), x*y*(1-c) - z*s, x*z*(1-c) + y*s],
                     [y*x*(1-c) + z*s, c + y*y*(1-c), y*z*(1-c) - x*s],
                     [z*x*(1-c) - y*s, z*y*(1-c) + x*s, c + z*z*(1-c)]])

PARTS = dict(head=[(0, 0, 64, 16)], body=[(16, 16, 40, 32), (16, 32, 40, 48)],
             **{k: v[0] for k, v in POSE.items()})

def posed_mesh(mesh_pkg):
    """Posed vertex positions (pixels) + one outward normal per triangle."""
    pos, uv, tris = mesh_layers.read_mesh(mesh_pkg)
    pos = pos.astype(np.float64) / UNIT
    cen = uv[tris].mean(1)
    normals = np.zeros((len(tris), 3))
    out = pos.copy()
    for name, rects in PARTS.items():
        sel = np.zeros(len(tris), bool)
        for u0, v0, u1, v1 in rects:
            sel |= (cen[:, 0] > u0) & (cen[:, 0] < u1) & (cen[:, 1] > v0) & (cen[:, 1] < v1)
        if not sel.any(): continue
        vs = np.unique(tris[sel]); c = pos[vs].mean(0)
        P = pos[tris[sel]]
        n = np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0])
        n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-9)
        flip = np.einsum("ij,ij->i", n, P.mean(1) - c) < 0
        n[flip] *= -1
        R = np.eye(3); piv = np.zeros(3)
        if name in POSE:
            _, pivot, rots = POSE[name]; piv = np.array(pivot)
            for axis, ang in rots: R = rot(axis, ang) @ R
            out[vs] = (pos[vs] - piv) @ R.T + piv
        normals[sel] = n @ R.T
    return out, uv, tris, normals

def render_icon(skin, mesh_pkg, size=256, ss=3, yaw=-22, pitch=-14):
    pos, uv, tris, normals = posed_mesh(mesh_pkg)
    tex = np.array(skin.convert("RGBA")); th, tw = tex.shape[:2]
    Rz = rot((0, 0, 1), yaw); Rx = rot((1, 0, 0), pitch)
    M = Rx @ Rz
    v = pos @ M.T; nrm = normals @ M.T
    # view space: screen x = X, screen y = -Z, viewer looks along -Y (front = +Y)
    S = size * ss
    sx, sy, depth = v[:, 0], -v[:, 2], -v[:, 1]
    scale = 0.845 * S / (sy.max() - sy.min())
    sx = (sx - (sx.max() + sx.min()) / 2) * scale + S / 2
    sy = (sy - sy.min()) * scale + S * 0.085
    light = np.array([-0.5, 0.7, 0.55]); light /= np.linalg.norm(light)
    img = np.zeros((S, S, 4)); zb = np.full((S, S), np.inf)
    for k, t in enumerate(tris):
        n = nrm[k]
        if n[1] <= 0.02: continue          # back-facing
        x, y, z = sx[t], sy[t], depth[t]
        x0, x1 = int(max(0, x.min())), int(min(S - 1, x.max() + 1))
        y0, y1 = int(max(0, y.min())), int(min(S - 1, y.max() + 1))
        if x1 < x0 or y1 < y0: continue
        X, Y = np.meshgrid(np.arange(x0, x1 + 1) + .5, np.arange(y0, y1 + 1) + .5)
        d = (y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2])
        if abs(d) < 1e-9: continue
        l0 = ((y[1] - y[2]) * (X - x[2]) + (x[2] - x[1]) * (Y - y[2])) / d
        l1 = ((y[2] - y[0]) * (X - x[2]) + (x[0] - x[2]) * (Y - y[2])) / d
        l2 = 1 - l0 - l1
        m = (l0 >= -1e-4) & (l1 >= -1e-4) & (l2 >= -1e-4)
        if not m.any(): continue
        Z = l0 * z[0] + l1 * z[1] + l2 * z[2]
        U = l0 * uv[t[0], 0] + l1 * uv[t[1], 0] + l2 * uv[t[2], 0]
        V = l0 * uv[t[0], 1] + l1 * uv[t[1], 1] + l2 * uv[t[2], 1]
        c = tex[np.clip((V / 64 * th).astype(int), 0, th - 1), np.clip((U / 64 * tw).astype(int), 0, tw - 1)].astype(np.float64)
        m &= c[..., 3] > 127
        sub = zb[y0:y1 + 1, x0:x1 + 1]; m &= Z < sub
        if not m.any(): continue
        sub[m] = Z[m]
        c[..., :3] *= 0.55 + 0.45 * max(float(n @ light), 0.0)
        img[y0:y1 + 1, x0:x1 + 1][m] = c[m]
    im = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8))
    return im.resize((size, size), Image.LANCZOS)