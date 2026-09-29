"""
Re-texture a (slim-arm) Minecraft skin for the Dungeons II player model.

The game model mostly follows the Minecraft UV layout, but not everywhere (its
legs are swapped left/right, mirrored, and have top/bottom faces exchanged).
Instead of hard-coding quirks, every texel the game model uses is traced to
its 3D spot on the model, and that spot is looked up on a standard Minecraft
model to fetch the right colour.
"""
import numpy as np
import mesh_layers

UNIT = 6.25
# Minecraft boxes in game model space (pixels): X = character's left, Y = front, Z = up
BOXES = {  # name: (u, v, w, h, d, x0, y0, z0)
    "head": (0, 0, 8, 8, 8, -4, -4, 24),
    "body": (16, 16, 8, 12, 4, -4, -2, 12),
    "rarm": (40, 16, 3, 12, 4, -7, -2, 12),
    "larm": (32, 48, 3, 12, 4, 4, -2, 12),
    "rleg": (0, 16, 4, 12, 4, -4, -2, 0),
    "lleg": (16, 48, 4, 12, 4, 0, -2, 0),
}
# game UV regions of the base (non-hat) parts
GAME_PARTS = [(0, 0, 32, 16), (16, 16, 40, 32), (40, 16, 56, 32), (32, 48, 48, 64), (0, 16, 16, 32), (16, 48, 32, 64)]

def mc_uv(p, n):
    """Minecraft skin UV of surface point p (pixels) with outward axis normal n, or None."""
    ax = int(np.argmax(np.abs(n))); sg = np.sign(n[ax])
    for u, v, w, h, d, x0, y0, z0 in BOXES.values():
        x1, y1, z1 = x0 + w, y0 + d, z0 + h
        x, y, z = p
        plane = {(0, 1): x1, (0, -1): x0, (1, 1): y1, (1, -1): y0, (2, 1): z1, (2, -1): z0}[(ax, sg)]
        if abs(p[ax] - plane) > 0.2:
            continue
        e = 0.05
        if not (x0 - e <= x <= x1 + e and y0 - e <= y <= y1 + e and z0 - e <= z <= z1 + e):
            continue
        if (ax, sg) == (1, 1):   return u + d + (x - x0), v + d + (z1 - z)          # front
        if (ax, sg) == (1, -1):  return u + 2 * d + w + (x1 - x), v + d + (z1 - z)  # back
        if (ax, sg) == (0, -1):  return u + (y - y0), v + d + (z1 - z)              # right side
        if (ax, sg) == (0, 1):   return u + d + w + (y1 - y), v + d + (z1 - z)      # left side
        if (ax, sg) == (2, 1):   return u + d + (x - x0), v + (y - y0)              # top
        return u + d + w + (x - x0), v + (y1 - y)                                    # bottom
    return None

def build_table(mesh_pkg):
    """dest texel (x, y) -> source texel (x, y) for every texel the game model uses."""
    pos, uv, tris = mesh_layers.read_mesh(mesh_pkg)
    pos = pos / UNIT
    cen = uv[tris].mean(1)
    table = {}
    for u0, v0, u1, v1 in GAME_PARTS:
        sel = tris[(cen[:, 0] > u0) & (cen[:, 0] < u1) & (cen[:, 1] > v0) & (cen[:, 1] < v1)]
        c = pos[np.unique(sel)].mean(0)
        for t in sel:
            P, U = pos[t].astype(np.float64), uv[t].astype(np.float64)
            n = np.cross(P[1] - P[0], P[2] - P[0])
            if np.linalg.norm(n) < 1e-9:
                continue
            n /= np.linalg.norm(n)
            if np.dot(n, P.mean(0) - c) < 0:
                n = -n
            if np.max(np.abs(n)) < 0.95:
                continue
            det = (U[1, 1] - U[2, 1]) * (U[0, 0] - U[2, 0]) + (U[2, 0] - U[1, 0]) * (U[0, 1] - U[2, 1])
            if abs(det) < 1e-6:
                continue
            for ty in range(int(U[:, 1].min()), int(np.ceil(U[:, 1].max()))):
                for tx in range(int(U[:, 0].min()), int(np.ceil(U[:, 0].max()))):
                    qx, qy = tx + .5, ty + .5
                    l0 = ((U[1, 1] - U[2, 1]) * (qx - U[2, 0]) + (U[2, 0] - U[1, 0]) * (qy - U[2, 1])) / det
                    l1 = ((U[2, 1] - U[0, 1]) * (qx - U[2, 0]) + (U[0, 0] - U[2, 0]) * (qy - U[2, 1])) / det
                    l2 = 1 - l0 - l1
                    if min(l0, l1, l2) < -1e-6:
                        continue
                    p = l0 * P[0] + l1 * P[1] + l2 * P[2]
                    m = mc_uv(p, n)
                    if m is not None:
                        table[(tx, ty)] = (min(63, int(m[0])), min(63, int(m[1])))
    return table

def apply(img, table):
    src = img.copy(); s = src.load(); d = img.load()
    for (x, y), (sx, sy) in table.items():
        d[x, y] = s[sx, sy]
    return img
