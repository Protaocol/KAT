"""
Preview of what the paged shader will draw, WITHOUT Unity.
numpy port of the fragment (and box-extent vertex) math in KF_AvatarTextShaderPaged.shader, fed by the real client encode
path (text -> layout -> encode_group -> VRChat quantisation -> animator value 127*p -> shader decode -> atlas sample).

  python preview_layout.py charset.json atlas/ "Hello 你好\n第二行" [out.png] [options]

The page format (2 = base-15 pairs, 3 = one page per slot) is read from charset.json, exactly like the client does; there
is no flag for it. In format 3 the atlas has up to 254 pages and pages are looked up numerically (page_000.png ...).

options (mirror the material properties of the same name):
  --aspect 3.0        quad width / height in the world (KAT installs a 0.5 x 0.1667 quad = 3.0)
  --no-box            _BoxEnabled = 0        --no-shrink   _ShrinkToFit = 0
  --anchor-x 0.5      --anchor-y 0           --scale 1      --padding 0.5     --border 0.14     --radius 0.5
  --weight 0          _Weight: glyph thickness (-0.15 thinner .. +0.15 bolder)
  --outline-width 0   _OutlineWidth: extra visible SDF outline (0 .. 0.3; default off)
  --tail 1            _Tail: speech-bubble mode (0 = off, 1 = full size). Pins the box to the tail side + bottom
  --tail-side 1       _TailSide on the physical quad (0 = uv.x 0 end, 1 = uv.x 1 end)
  --flip              draw as seen from behind / in a mirror (anchors and tail side mirror with the drawing)
"""
import argparse
import glob
import sys
import types

import numpy as np
from PIL import Image
from scipy.ndimage import map_coordinates

_p = types.ModuleType("pythonosc")


class _U:
    def __init__(s, *a): s.sent = []
    def send_message(s, *a): s.sent.append(a)


_p.udp_client = types.SimpleNamespace(SimpleUDPClient=_U)
_p.osc_server = types.SimpleNamespace(ThreadingOSCUDPServer=object)
_p.dispatcher = types.SimpleNamespace(Dispatcher=object)
sys.modules.setdefault("pythonosc", _p)
from kat_codec import PAGE_BASE, Charset, encode_group   # noqa: E402
from katosc import KatOsc, KatOscConfig        # noqa: E402

ap = argparse.ArgumentParser()
ap.add_argument("charset", nargs="?", default="charset.json")
ap.add_argument("atlas", nargs="?", default="atlas")
ap.add_argument("text", nargs="?", default="Hello 你好\\n第二行 abc")
ap.add_argument("out", nargs="?", default="preview.png")
ap.add_argument("--aspect", type=float, default=3.0)
ap.add_argument("--no-box", action="store_true")
ap.add_argument("--no-shrink", action="store_true")
ap.add_argument("--anchor-x", type=float, default=0.5)
ap.add_argument("--anchor-y", type=float, default=0.0)
ap.add_argument("--weight", type=float, default=0.0)
ap.add_argument("--outline-width", type=float, default=0.0)
ap.add_argument("--scale", type=float, default=1.0)
ap.add_argument("--padding", type=float, default=0.5)
ap.add_argument("--border", type=float, default=0.14)
ap.add_argument("--radius", type=float, default=0.5)
ap.add_argument("--tail", type=float, default=0.0)
ap.add_argument("--tail-side", type=float, default=1.0)
ap.add_argument("--flip", action="store_true")
ap.add_argument("--height", type=int, default=384, help="output height in pixels")
args = ap.parse_args()
if not -0.15 <= args.weight <= 0.15:
    ap.error("--weight must be between -0.15 and 0.15")
if not 0.0 <= args.outline_width <= 0.3:
    ap.error("--outline-width must be between 0 and 0.3")

cs = Charset.load(args.charset)
FMT = cs.page_format
cfg = KatOscConfig(); cfg.osc_enable_server = False; cfg.osc_enable_chatbox = False
c = KatOsc(cfg, cs, autostart=False)
text = args.text.replace("\\n", "\n")
slots = c._layout(text)

# animator: per group, _Char{slot} = 127*p ; _Page{j} = 127*p   (quantised like VRChat)
#   format 2: j = pair index (slot // 2), 64 properties      format 3: j = slot index, 128 properties
q = lambda v: round(v * 127) / 127
m = c.sync_params
CAP = max(128, c.pointer_count * m)                 # a sync size that does not divide 128 rounds the last group up
chars = np.zeros(CAP); pages = np.zeros(CAP if FMT == 3 else (CAP + 1) // 2)
for p in range(c.pointer_count):
    cf, pf = encode_group(slots[p * m:(p + 1) * m], FMT)
    for i, v in enumerate(cf): chars[p * m + i] = 127 * q(v)
    base = p * m if FMT == 3 else (p * m) // 2
    for j, v in enumerate(pf): pages[base + j] = 127 * q(v)


def page_of(slot_idx):
    """Page number of a slot (scalar or array), decoded the way the shader's ReadSlot does for this format."""
    if FMT == 3:
        return dec(pages[slot_idx])
    pair = dec(pages[slot_idx >> 1])
    return np.where(slot_idx & 1, pair % PAGE_BASE, pair // PAGE_BASE)


def _page_files(folder):
    """page_<n>.png sorted NUMERICALLY (an alphabetical sort puts page_100 before page_11 when names are not padded)."""
    import re
    found = {}
    for f in glob.glob(f"{folder}/page_*.png"):
        mt = re.fullmatch(r"page_(\d+)\.png", f.replace("\\", "/").rsplit("/", 1)[-1])
        if mt:
            found[int(mt.group(1))] = f
    if sorted(found) != list(range(len(found))):
        raise SystemExit(f"atlas pages in {folder} are not a contiguous 0..N-1 run: {sorted(found)[:8]}...")
    return [found[i] for i in range(len(found))]


_files = _page_files(args.atlas)
NP = len(_files)
need = cs.pages
if NP < need:
    raise SystemExit(f"atlas has {NP} pages but the charset needs {need} (format {FMT}); wrong atlas folder?")
_atlas_cache = {}


def get_page(i):
    """Load an atlas page on first use: a 254-page format-3 atlas is ~1 GB as float32, but a preview touches a handful."""
    if i not in _atlas_cache:
        _atlas_cache[i] = np.asarray(Image.open(_files[i]), dtype=np.float32) / 255
    return _atlas_cache[i]


def sample(page, u, v):  # bilinear, u,v in 0..1, v=0 bottom (Unity)
    a = get_page(page); H, W = a.shape
    return map_coordinates(a, [(1 - v) * H - 0.5, u * W - 0.5], order=1, mode="nearest")


def dec(k): k = np.round(k); return np.where(k < 0, k + 255, k).astype(int)


# UsedExtent()
ROW_LEN, ROWS = 32, 4
last_col = last_row = -1
for s in range(ROW_LEN * ROWS):
    sy = int(dec(chars[s])); pg = int(page_of(s))
    if pg != 0 or sy != 0:
        r = s // ROW_LEN
        last_row = max(last_row, r); last_col = max(last_col, s - r * ROW_LEN)
has_text = last_col >= 0
print("extent (last col,row):", last_col, last_row, "| hasText:", has_text)

H = args.height
W = int(round(H * args.aspect))
ys, xs = np.mgrid[0:H, 0:W].astype(np.float64)
aspect = args.aspect
grid = np.array([ROW_LEN, ROWS], float)
quad = np.array([aspect, 1.0])
cell_size = quad / grid
box_on = not args.no_box
_MainColor = np.array([1, 1, 1, 1.0]); _ShadowColor = np.array([0, 0, 0, 1.0])
_BackColor = np.array([0, 0, 0, 0.55]); _BorderColor = np.array([1, 1, 1, 0.9])
_Weight, _OutlineWidth = args.weight, args.outline_width
TAIL_H = 2.0 # tail height in slot widths at _Tail = 1 (keep in sync with the shader)
TAIL_W = 1.6 # tail base width in slot widths at _Tail = 1


def layout(px, py):
    """-> g (slot-grid coords), Q (box space), box size, pad, scale, origin."""
    uvx = (px + 0.5) / W; uvy = 1 - (py + 0.5) / H          # uv.y up
    if args.flip: uvx = 1 - uvx
    P = np.stack([uvx * aspect, 1 - uvy], -1)
    used = (np.array([last_col, last_row]) + 1.0) if (not args.no_shrink and has_text) else grid
    pad = args.padding * cell_size[0] if box_on else 0.0
    box_size = used * cell_size + 2 * pad
    # the tail hangs below the box: reserve its height so it stays inside the quad
    bubble = box_on and has_text and args.tail > 0.01
    tail_h = args.tail * TAIL_H * cell_size[0] if bubble else 0.0
    full = box_size + np.array([0.0, tail_h])
    ax = args.tail_side if bubble else args.anchor_x
    ay = 1.0 if bubble else args.anchor_y
    if args.flip: ax = 1 - ax
    s = min(1.0, min(quad[0] / full[0], quad[1] / full[1])) * args.scale
    origin = (quad - full * s) * np.array([ax, ay])
    Q = (P - origin) / s
    g = (Q - pad) / cell_size
    return g, Q, box_size, pad


def slot_sd(g):
    cell = np.floor(g); local = g - cell
    slot = np.clip(cell[..., 0] + cell[..., 1] * ROW_LEN, 0, 127).astype(int)
    sym = dec(chars[slot])
    page = page_of(slot)
    in_grid = (g[..., 0] >= 0) & (g[..., 0] < ROW_LEN) & (g[..., 1] >= 0) & (g[..., 1] < ROWS)
    blank = (~in_grid) | (not has_text)
    fol = (page == 0) & (sym == 254)
    prev = np.maximum(slot - 1, 0)
    psym = dec(chars[prev]); ppage = page_of(prev)
    blank = blank | (fol & ((cell[..., 0] < 0.5) | (slot == 0) | (ppage == 0)))
    sym = np.where(fol, psym, sym); page = np.where(fol, ppage, page); right = fol
    narrow = page == 0
    blank = blank | (narrow & (sym >= 128))
    nx = ((sym & 15) + local[..., 0]) / 16; ny = ((sym >> 4) + local[..., 1]) / 8
    wx = ((sym & 15) + np.where(right, 0.5, 0) + local[..., 0] * 0.5) / 16; wy = ((sym >> 4) + local[..., 1]) / 16
    ux = np.where(narrow, nx, wx); uy = np.where(narrow, ny, wy)
    out = np.zeros(slot.shape)
    for pg in np.unique(page[~blank]):
        pg = int(pg)
        if pg >= NP:
            raise SystemExit(f"text uses page {pg} but the atlas only has {NP} pages")
        sel = (page == pg) & ~blank
        out[sel] = sample(pg, ux[sel], 1 - uy[sel])
    return out


def box_dist(Q, box_size):
    """Signed distance (negative inside) of the rounded box united with the tail triangle."""
    half = box_size * 0.5
    radius = min(args.radius * cell_size[0], half.min())
    dq = np.abs(Q - half) - (half - radius)
    d = np.linalg.norm(np.maximum(dq, 0), axis=-1) + np.minimum(np.maximum(dq[..., 0], dq[..., 1]), 0) - radius
    if args.tail > 0.01 and box_on and has_text:
        th = args.tail * TAIL_H * cell_size[0]; tw = args.tail * TAIL_W * cell_size[0]
        side = 1 - args.tail_side if args.flip else args.tail_side
        # triangle hanging under the box, base on the bottom edge, near the chosen corner, tip leaning outwards
        margin = radius + 0.4 * cell_size[0]
        lo = min(margin + tw * 0.5, box_size[0] * 0.5); hi = max(box_size[0] - margin - tw * 0.5, box_size[0] * 0.5)
        bx = lo if side < 0.5 else hi
        tip_x = bx + (-0.35 if side < 0.5 else 0.35) * tw
        A = np.array([bx - tw * 0.5, box_size[1] - 1e-4]); B = np.array([bx + tw * 0.5, box_size[1] - 1e-4])
        C = np.array([tip_x, box_size[1] + th])
        dt = tri_dist(Q, A, B, C)
        d = np.minimum(d, dt)
    return d


def tri_dist(Pp, A, B, C):
    """Signed distance to triangle ABC (negative inside)."""
    def seg(P, a, b):
        ab = b - a; t = np.clip(((P - a) @ ab) / (ab @ ab), 0, 1)
        return np.linalg.norm(P - (a + t[..., None] * ab), axis=-1)
    d = np.minimum(np.minimum(seg(Pp, A, B), seg(Pp, B, C)), seg(Pp, C, A))
    def cross(a, b, P): return (b[0] - a[0]) * (P[..., 1] - a[1]) - (b[1] - a[1]) * (P[..., 0] - a[0])
    s1, s2, s3 = cross(A, B, Pp), cross(B, C, Pp), cross(C, A, Pp)
    inside = ((s1 >= 0) & (s2 >= 0) & (s3 >= 0)) | ((s1 <= 0) & (s2 <= 0) & (s3 <= 0))
    return np.where(inside, -d, d)


g0, Q0, bs, pad = layout(xs, ys)
sd = slot_sd(g0)
sdx = slot_sd(layout(xs + 1, ys)[0]); sdy = slot_sd(layout(xs, ys + 1)[0])
aa = np.maximum(np.abs(sdx - sd) + np.abs(sdy - sd), 1e-3)
fill = np.clip((sd - (0.5 - _Weight)) / aa + 0.5, 0, 1)
outer = np.clip((sd - (0.5 - _Weight - _OutlineWidth)) / aa + 0.5, 0, 1)
ratio = (fill / np.maximum(outer, 1e-4))[..., None]
text_rgb = _ShadowColor[:3] * (1 - ratio) + _MainColor[:3] * ratio
text_a = outer * _MainColor[3]

d = box_dist(Q0, bs)
dx = box_dist(layout(xs + 1, ys)[1], bs); dy = box_dist(layout(xs, ys + 1)[1], bs)
aab = np.maximum(np.abs(dx - d) + np.abs(dy - d), 1e-6)
box_cover = np.clip(0.5 - d / aab, 0, 1)
border_cover = np.clip((d + args.border * cell_size[0]) / aab + 0.5, 0, 1)[..., None]
box_rgb = _BackColor[:3] * (1 - border_cover) + _BorderColor[:3] * border_cover
box_a = (_BackColor[3] * (1 - border_cover[..., 0]) + _BorderColor[3] * border_cover[..., 0]) * box_cover * (1.0 if box_on else 0.0)

a = text_a + box_a * (1 - text_a)
rgb = (text_rgb * text_a[..., None] + box_rgb * (box_a * (1 - text_a))[..., None]) / np.maximum(a, 1e-5)[..., None]
if not has_text:
    a = np.zeros_like(a)

# composite over a fake world so the translucent backdrop is visible
yy = np.linspace(0, 1, H)[:, None] * np.ones((1, W))
world = np.stack([0.35 + 0.25 * yy, 0.55 + 0.1 * yy, 0.45 + 0.3 * yy], -1)
world = world + (((xs // 24) + (ys // 24)) % 2)[..., None] * 0.08
img = world * (1 - a[..., None]) + rgb * a[..., None]
Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).save(args.out)
print("ok\n", slots, "\n->", args.out, f"({W}x{H})")
