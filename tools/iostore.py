import struct,ctypes,os
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
# configure with environment variables (see tools/README.md)
GAME=os.environ.get("MD2_GAME_DIR", r"C:\Program Files (x86)\Steam\steamapps\common\Minecraft Dungeons II")
PAKS=os.path.join(GAME, "Dungeons", "Content", "Paks")
KEY=bytes.fromhex(os.environ.get("MD2_AES_KEY") or open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "aes.key")).read().strip())
def dec(b): return Cipher(algorithms.AES(KEY),modes.ECB()).decryptor().update(b)
_oo=None
def oodle():
    global _oo
    if _oo is None:
        _oo=ctypes.WinDLL(os.environ["MD2_OODLE_DLL"])   # an oo2core_7 (or newer) DLL: needed for Oodle Leviathan
        _oo.OodleLZ_Decompress.restype=ctypes.c_int64
    return _oo
def oodle_dec(src,rawlen):
    out=ctypes.create_string_buffer(rawlen)
    r=oodle().OodleLZ_Decompress(src,ctypes.c_int64(len(src)),out,ctypes.c_int64(rawlen),1,0,0,None,None,None,None,None,None,3)
    if r!=rawlen: raise Exception("oodle fail %d"%r)
    return out.raw
class Rd:
    def __init__(s,b,p=0): s.b=b;s.p=p
    def u32(s): v=struct.unpack_from("<I",s.b,s.p)[0]; s.p+=4; return v
    def i32(s): v=struct.unpack_from("<i",s.b,s.p)[0]; s.p+=4; return v
    def fstr(s):
        n=s.i32()
        if n==0: return ""
        if n<0: v=s.b[s.p:s.p-2*n].decode("utf-16-le")[:-1]; s.p+=-2*n; return v
        v=s.b[s.p:s.p+n-1].decode("latin1"); s.p+=n; return v
class Toc:
    def __init__(s,name):
        s.name=name
        b=open(os.path.join(PAKS,name+".utoc"),"rb").read()
        h=struct.unpack_from("<16sB3xIIIIIIIII",b,0)
        (_,s.ver,hs,s.n,s.nblk,bes,s.nmeth,mlen,s.bsize,dsize,parts)=h
        s.cid=struct.unpack_from("<Q",b,0x38)[0]
        s.flags=b[0x50]; seeds=struct.unpack_from("<I",b,0x54)[0]; nohash=struct.unpack_from("<I",b,0x60)[0]
        p=hs
        s.ids=[b[p+12*i:p+12*i+12] for i in range(s.n)]; p+=12*s.n
        s.ol=[]
        for i in range(s.n):
            e=b[p+10*i:p+10*i+10]
            s.ol.append((int.from_bytes(e[0:5],"big"),int.from_bytes(e[5:10],"big")))
        p+=10*s.n
        p+=4*seeds+4*nohash
        s.blocks=[]
        for i in range(s.nblk):
            e=b[p+12*i:p+12*i+12]
            s.blocks.append((int.from_bytes(e[0:5],"little"),int.from_bytes(e[5:8],"little"),int.from_bytes(e[8:11],"little"),e[11]))
        p+=12*s.nblk
        s.meths=[b[p+32*i:p+32*i+32].rstrip(b"\0").decode() for i in range(s.nmeth)]; p+=32*s.nmeth
        s.files={}
        if dsize:
            d=b[p:p+dsize]
            if s.flags&2: d=dec(d)
            r=Rd(d); mount=r.fstr()
            nd=r.u32(); dirs=[struct.unpack_from("<IIII",d,r.p+16*i) for i in range(nd)]; r.p+=16*nd
            nf=r.u32(); fil=[struct.unpack_from("<III",d,r.p+12*i) for i in range(nf)]; r.p+=12*nf
            ns=r.u32(); strs=[r.fstr() for _ in range(ns)]
            def walk(di,path):
                nm,child,sib,ff=dirs[di]
                if nm!=0xffffffff: path=path+strs[nm]+"/"
                f=ff
                while f!=0xffffffff:
                    fn,nxt,ud=fil[f]; s.files[mount+path+strs[fn]]=ud; f=nxt
                c=child
                while c!=0xffffffff:
                    walk(c,path); c=dirs[c][2]
            walk(0,"")
        s.ucas=os.path.join(PAKS,name+".ucas")
    def read(s,idx):
        off,ln=s.ol[idx]
        out=bytearray()
        f=open(s.ucas,"rb")
        first=off//s.bsize; last=(off+ln-1)//s.bsize
        for bi in range(first,last+1):
            co,cs,us,m=s.blocks[bi]
            f.seek(co); rs=(cs+15)&~15 if s.flags&2 else cs
            raw=f.read(rs)
            if s.flags&2: raw=dec(raw)
            raw=raw[:cs]
            out+= raw if m==0 else oodle_dec(raw,us)
        st=off-first*s.bsize
        return bytes(out[st:st+ln])


