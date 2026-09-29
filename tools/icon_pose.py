"""
The locker-icon pose, fitted to the game's own hero icons by fit_pose.py and
stored in icon_pose.json: limb rotations, a whole-body lean, and the camera
(yaw/pitch/roll, perspective, framing). Used to pose any (patched) player mesh
and to render icons exactly the way the app does.
"""
import json, os
import numpy as np
from PIL import Image
import mesh_layers, icon_render

HERE = os.path.dirname(os.path.abspath(__file__))
FIT = json.load(open(os.path.join(HERE, "icon_pose.json")))
CAM_KEYS = ("cam_yaw", "cam_pitch", "cam_roll", "persp", "scale", "offx", "offy")
PARTS = {"head": ([(0, 0, 64, 16)], (0, 0, 24.0)),
         "body": ([(16, 16, 40, 32), (16, 32, 40, 48)], None),
         "rarm": ([(40, 16, 56, 32), (40, 32, 56, 48)], (-5.0, 0, 22.0)),
         "larm": ([(32, 48, 48, 64), (48, 48, 64, 64)], (5.0, 0, 22.0)),
         "legA": ([(0, 16, 16, 32), (0, 32, 16, 48)], (2.0, 0, 12.0)),     # +X leg (the game swaps legs)
         "legB": ([(16, 48, 32, 64), (0, 48, 16, 64)], (-2.0, 0, 12.0))}

def R(rx, ry, rz):
    rot = icon_render.rot
    return rot((0, 0, 1), rz) @ rot((0, 1, 0), ry) @ rot((1, 0, 0), rx)

def posed(mesh_pkg):
    """Posed positions (px) and one outward normal per triangle (rotated with its part)."""
    pos, uv, tris = mesh_layers.read_mesh(mesh_pkg)
    pos = pos.astype(np.float64) / 6.25
    cen = uv[tris].mean(1)
    out = pos.copy(); normals = np.zeros((len(tris), 3))
    for name, (rects, piv) in PARTS.items():
        sel = np.zeros(len(tris), bool)
        for u0, v0, u1, v1 in rects:
            sel |= (cen[:, 0] > u0) & (cen[:, 0] < u1) & (cen[:, 1] > v0) & (cen[:, 1] < v1)
        if not sel.any(): continue
        vs = np.unique(tris[sel]); c = pos[vs].mean(0)
        P = pos[tris[sel]]; n = np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0])
        n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-9)
        n[np.einsum("ij,ij->i", n, P.mean(1) - c) < 0] *= -1
        Rm = np.eye(3)
        if piv is not None:
            Rm = R(FIT[name + "_rx"], FIT[name + "_ry"], FIT[name + "_rz"])
            out[vs] = (pos[vs] - np.array(piv)) @ Rm.T + np.array(piv)
        normals[sel] = n @ Rm.T
    Rb = R(FIT["body_rx"], 0, FIT["body_rz"]); hip = np.array([0, 0, 12.0])
    return (out - hip) @ Rb.T + hip, uv, tris, normals @ Rb.T

def camera():
    return [float(FIT[k]) for k in CAM_KEYS]

def render(tex_img, mesh_pkg, size, ss=3):
    """Shaded, supersampled icon render with the fitted camera (same maths as the app)."""
    pos, uv, tris, nrm = posed(mesh_pkg)
    tex = np.array(tex_img.convert("RGBA")); P = FIT; rot = icon_render.rot
    M = rot((1, 0, 0), P["cam_pitch"]) @ rot((0, 1, 0), P["cam_roll"]) @ rot((0, 0, 1), P["cam_yaw"])
    v = (pos - np.array([0, 0, 16.0])) @ M.T; n = nrm @ M.T
    dist = 40.0 + 400.0 * (1 - min(max(P["persp"], 0), 1))
    f = dist / (dist + (-v[:, 1]))
    S = size * ss; s = P["scale"] * S / 36.0
    sx = v[:, 0] * f * s + S / 2 + P["offx"] * S; sy = -v[:, 2] * f * s + S / 2 + P["offy"] * S; dp = -v[:, 1]
    light = np.array([-0.5, 0.7, 0.55]); light /= np.linalg.norm(light)
    img = np.zeros((S, S, 4)); zb = np.full((S, S), np.inf); th, tw = tex.shape[:2]
    for k, t in enumerate(tris):
        if n[k][1] <= 0.02: continue
        x, y, z = sx[t], sy[t], dp[t]
        x0, x1 = max(0, int(x.min())), min(S - 1, int(x.max()) + 1); y0, y1 = max(0, int(y.min())), min(S - 1, int(y.max()) + 1)
        if x1 < x0 or y1 < y0: continue
        d = (y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2])
        if abs(d) < 1e-9: continue
        X, Y = np.meshgrid(np.arange(x0, x1 + 1) + .5, np.arange(y0, y1 + 1) + .5)
        l0 = ((y[1] - y[2]) * (X - x[2]) + (x[2] - x[1]) * (Y - y[2])) / d
        l1 = ((y[2] - y[0]) * (X - x[2]) + (x[0] - x[2]) * (Y - y[2])) / d
        l2 = 1 - l0 - l1; m = (l0 >= -1e-4) & (l1 >= -1e-4) & (l2 >= -1e-4)
        if not m.any(): continue
        Z = l0 * z[0] + l1 * z[1] + l2 * z[2]
        U = l0 * uv[t[0], 0] + l1 * uv[t[1], 0] + l2 * uv[t[2], 0]; V = l0 * uv[t[0], 1] + l1 * uv[t[1], 1] + l2 * uv[t[2], 1]
        c = tex[np.clip((V / 64 * th).astype(int), 0, th - 1), np.clip((U / 64 * tw).astype(int), 0, tw - 1)].astype(np.float64)
        m &= c[..., 3] > 127; sub = zb[y0:y1 + 1, x0:x1 + 1]; m &= Z < sub
        if not m.any(): continue
        sub[m] = Z[m]; c[..., :3] *= 0.55 + 0.45 * max(float(n[k] @ light), 0.0)
        img[y0:y1 + 1, x0:x1 + 1][m] = c[m]
    return Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)).resize((size, size), Image.LANCZOS)
