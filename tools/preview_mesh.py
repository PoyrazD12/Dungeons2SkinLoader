"""Software-render the (patched) player mesh with a skin texture, for checking."""
import sys, numpy as np
from PIL import Image

def render(pos, uv, tris, tex, yaw=30, pitch=-10, size=600, flip_v=False):
    tex = np.array(tex.convert("RGBA"))
    th, tw = tex.shape[:2]
    a, p = np.radians(yaw), np.radians(pitch)
    # mesh: Z up, X side, Y depth -> view: rotate about Z then tilt
    Rz = np.array([[np.cos(a), -np.sin(a), 0], [np.sin(a), np.cos(a), 0], [0, 0, 1]])
    Rx = np.array([[1, 0, 0], [0, np.cos(p), -np.sin(p)], [0, np.sin(p), np.cos(p)]])
    v = pos @ Rz.T @ Rx.T
    sx, sy, depth = v[:, 0], -v[:, 2], v[:, 1]
    s = size * 0.9 / max(np.ptp(sx), np.ptp(sy))
    sx = (sx - (sx.max() + sx.min()) / 2) * s + size / 2
    sy = (sy - (sy.max() + sy.min()) / 2) * s + size / 2
    img = np.zeros((size, size, 4), np.uint8); img[..., :3] = 40; img[..., 3] = 255
    zb = np.full((size, size), np.inf)
    for t in tris:
        x, y, z = sx[t], sy[t], depth[t]
        x0, x1 = int(max(0, x.min())), int(min(size - 1, x.max() + 1))
        y0, y1 = int(max(0, y.min())), int(min(size - 1, y.max() + 1))
        if x1 < x0 or y1 < y0: continue
        X, Y = np.meshgrid(np.arange(x0, x1 + 1) + .5, np.arange(y0, y1 + 1) + .5)
        d = (y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2])
        if abs(d) < 1e-9: continue
        l0 = ((y[1] - y[2]) * (X - x[2]) + (x[2] - x[1]) * (Y - y[2])) / d
        l1 = ((y[2] - y[0]) * (X - x[2]) + (x[0] - x[2]) * (Y - y[2])) / d
        l2 = 1 - l0 - l1
        m = (l0 >= 0) & (l1 >= 0) & (l2 >= 0)
        if not m.any(): continue
        Z = l0 * z[0] + l1 * z[1] + l2 * z[2]
        U = l0 * uv[t[0], 0] + l1 * uv[t[1], 0] + l2 * uv[t[2], 0]
        V = l0 * uv[t[0], 1] + l1 * uv[t[1], 1] + l2 * uv[t[2], 1]
        ui = np.clip((U / 64 * tw).astype(int), 0, tw - 1); vi = np.clip((V / 64 * th).astype(int), 0, th - 1)
        c = tex[vi, ui]
        m &= c[..., 3] > 127
        sub = zb[y0:y1 + 1, x0:x1 + 1]
        m &= Z < sub
        sub[m] = Z[m]
        img[y0:y1 + 1, x0:x1 + 1][m] = c[m]
    return Image.fromarray(img)

if __name__ == "__main__":
    sys.path.insert(0, __import__("os").path.dirname(__import__("os").path.abspath(__file__)))
    import mesh_layers
    pkg = open(sys.argv[1], "rb").read()
    pos, uv, tris = mesh_layers.read_mesh(pkg)
    tex = Image.open(sys.argv[2])
    ims = [render(pos, uv, tris, tex, yaw=y) for y in (20, 200)]
    out = Image.new("RGBA", (1200, 600)); out.paste(ims[0], (0, 0)); out.paste(ims[1], (600, 0))
    out.save(sys.argv[3])
