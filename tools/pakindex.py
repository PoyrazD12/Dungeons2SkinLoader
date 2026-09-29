"""List files in the game's legacy .pak (v11, encrypted index) with their compression."""
import struct, sys
sys.path.insert(0, __import__("os").path.dirname(__import__("os").path.abspath(__file__)))
from iostore import dec, Rd, PAKS

PAK = PAKS + r"\Dungeons-Windows.pak"

def load():
    f = open(PAK, "rb"); f.seek(-221, 2); foot = f.read(221)
    idx_off, idx_size = struct.unpack_from("<qq", foot, 16 + 1 + 4 + 4)
    f.seek(idx_off); prim = dec(f.read(idx_size))
    r = Rd(prim); mount = r.fstr(); n = r.i32(); r.p += 8
    has_phi = r.u32(); r.p += 36 if has_phi else 0
    has_fdi = r.u32(); fdi_off, fdi_size = struct.unpack_from("<qq", prim, r.p); r.p += 36
    enc_size = r.i32(); enc = prim[r.p:r.p + enc_size]
    f.seek(fdi_off); fdi = dec(f.read((fdi_size + 15) & ~15))
    r = Rd(fdi); files = {}
    for _ in range(r.i32()):
        d = r.fstr()
        for _ in range(r.i32()):
            name = r.fstr(); loc = r.i32()
            files[mount + d + name] = decode_entry(enc, loc)
    return files

def decode_entry(enc, p):
    v = struct.unpack_from("<I", enc, p)[0]; p += 4
    method = (v >> 23) & 0x3F
    nblocks = (v >> 6) & 0xFFFF
    encrypted = bool(v & (1 << 22))
    bsize_bits = v & 0x3F
    if bsize_bits == 0x3F: p += 4
    def num(safe):
        nonlocal p
        if safe: x = struct.unpack_from("<I", enc, p)[0]; p += 4
        else: x = struct.unpack_from("<Q", enc, p)[0]; p += 8
        return x
    offset = num(v & (1 << 31)); usize = num(v & (1 << 29))
    size = num(v & (1 << 30)) if method else usize
    return dict(offset=offset, usize=usize, size=size, method=method, blocks=nblocks, encrypted=encrypted)

if __name__ == "__main__":
    files = load()
    print(len(files), "files")
    for k, e in sorted(files.items()):
        if k.lower().endswith((".ufont", ".ttf", ".otf")):
            print(k, e)
