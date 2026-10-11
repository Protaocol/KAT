"""
Render charset.json into SDF atlas pages for the paged KAT shader.

  python build_atlas.py charset.json --font NotoSansCJK-Regular.ttc --font-index 2 -o atlas/
  python build_atlas.py charset.json --font wide.otf --narrow-font mono.ttf --cell 64 -o atlas/

Output: atlas/page_00.png .. page_NN.png (8-bit, single channel) + atlas/atlas_meta.json
        File names are zero-padded to at least 2 digits, and to 3 once there are 100+ pages (format 3), so a plain
        alphabetical sort is also numeric order. The Unity builder additionally sorts numerically.

The page format (2 or 3) is read from charset.json; the atlas itself is identical in both formats ("pages are pages"),
only the number of pages differs. atlas_meta.json records the format so the Unity side can cross-check it.

Every page has the same size S x S, S = 16 * cell   (cell 64 -> 1024 x 1024)
  page 0      narrow glyphs, cells cell x 2*cell (1:2), 16 columns x 8 rows  (slot -> col = slot % 16, row = slot // 16)
  pages 1..N  wide glyphs,   cells cell x cell  (1:1), 16 columns x 16 rows (slot -> col = slot % 16, row = slot // 16)
  row 0 is the TOP row of the PNG.

Encoding (matches the shader): value = 0.5 + signed_distance / (2 * spread), inside positive, clamped 0..1.
So 128 = the glyph edge, 255 = `spread` texels or more inside, 0 = `spread` texels or more outside.
"""

import argparse
import glob
import hashlib
import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy.ndimage import distance_transform_edt

from build_charset import print_vram
from kat_codec import NARROW_CELLS, PAGE_FORMAT_DEFAULT, SLOTS_PER_PAGE, Charset, read_charset_format


def page_name_width(total_pages: int) -> int:
    """Digits in page file names: at least 2, expanding to 3 digits for 100 or more pages."""
    return max(2, len(str(total_pages - 1)))


def page_name(index: int, width: int) -> str:
    return f"page_{index:0{width}d}.png"


def sdf_cell(ch, font, w, h, spread, k, baseline_y, stretch_y=1.0):
    """Render one glyph to a w x h SDF cell (uint8). Supersampled by k.

    ``stretch_y`` is applied to the binary raster *before* SDF generation.
    Page 0 uses cells twice as tall as wide (1:2), while page 1+ uses square
    cells. Without the compensating 2x vertical stretch, narrow glyphs occupy
    only half the world-space height of wide glyphs because the shader maps the
    complete cell into one screen slot.
    """
    W, H = w * k, h * k
    if stretch_y <= 0:
        raise ValueError("stretch_y must be > 0")

    render_h = max(1, int(round(H / stretch_y)))
    render_baseline = baseline_y * k
    img = Image.new("L", (W, render_h), 0)
    ImageDraw.Draw(img).text((W / 2, render_baseline), ch, font=font, fill=255, anchor="ms")
    if render_h != H:
        img = img.resize((W, H), Image.Resampling.NEAREST)
    mask = np.asarray(img) > 127
    if not mask.any():
        return np.zeros((h, w), np.uint8)
    d_in = distance_transform_edt(mask)
    d_out = distance_transform_edt(~mask)
    signed = (d_in - d_out) / k                     # in output texels
    v = np.clip(0.5 + signed / (2.0 * spread), 0.0, 1.0)
    v = v.reshape(h, k, w, k).mean(axis=(1, 3))     # box downsample
    return np.round(v * 255).astype(np.uint8)


def _sig(font, ch):
    m = font.getmask(ch)
    return m.size, bytes(m)


def missing_glyphs(font, chars):
    """Characters the font cannot draw (Pillow silently draws .notdef 'tofu' for them)."""
    notdef = _sig(font, "\uffff")
    return [c for c in chars if c != " " and _sig(font, c) == notdef]


def fit_font_size(font_path, index, chars, max_advance_px, hi=400):
    """Largest font size where the widest of `chars` still fits max_advance_px."""
    if not chars:
        # A charset may intentionally contain only the space cell on the narrow page.
        return hi
    lo, best = 8, 8
    while lo <= hi:
        mid = (lo + hi) // 2
        f = ImageFont.truetype(font_path, mid, index=index)
        widest = max(f.getlength(c) for c in chars)
        if widest <= max_advance_px:
            best, lo = mid, mid + 1
        else:
            hi = mid - 1
    return best


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("charset")
    ap.add_argument("--font", required=True, help="font for hanzi / wide glyphs (.ttf/.otf/.ttc)")
    ap.add_argument("--font-index", type=int, default=0, help="face index inside a .ttc")
    ap.add_argument("--narrow-font", help="font for ASCII (default: same as --font). A monospace font looks best")
    ap.add_argument("--narrow-font-index", type=int, default=0)
    ap.add_argument("--cell", type=int, default=64, help="wide cell size in px (default 64 -> 1024^2 pages)")
    ap.add_argument("--spread", type=int, default=6, help="SDF spread in texels (default 6)")
    ap.add_argument("--supersample", type=int, default=4)
    ap.add_argument("--strict", action="store_true", help="abort if the font lacks any charset glyph")
    ap.add_argument("-o", "--out", default="atlas")
    args = ap.parse_args()

    return render_atlas(args.charset, args.font, font_index=args.font_index, narrow_font=args.narrow_font,
                        narrow_font_index=args.narrow_font_index, cell_size=args.cell, spread=args.spread,
                        supersample=args.supersample, strict=args.strict, out=args.out)


def render_atlas(charset_path, font, *, font_index=0, narrow_font=None, narrow_font_index=0,
                 cell_size=64, spread=6, supersample=4, strict=False, out="atlas") -> int:
    with open(charset_path, encoding="utf-8") as f:
        cs = json.load(f)
    fmt = read_charset_format(cs)
    narrow, wide = cs["narrow"], cs["wide"]
    Charset(narrow, wide, page_format=fmt)          # same capacity / duplicate checks the client applies: fail here, not in game
    cell, s, k = cell_size, spread, supersample
    if not isinstance(cell, int) or cell <= 0:
        raise ValueError("cell_size must be a positive integer")
    if not isinstance(s, int) or s < 1:
        raise ValueError("spread must be a positive integer")
    if cell <= 2 * s:
        raise ValueError("cell_size must be greater than twice spread so glyphs have a positive render area")
    if not isinstance(k, int) or k < 1:
        raise ValueError("supersample must be a positive integer")
    size = 16 * cell
    os.makedirs(out, exist_ok=True)
    nfont_path = narrow_font or font
    nfont_index = narrow_font_index if narrow_font else font_index

    charset_file = charset_path

    # ---- wide: em box fills the cell minus the spread margin, centred vertically
    wsize = (cell - 2 * s) * k
    wfont = ImageFont.truetype(font, wsize, index=font_index)
    asc, desc = wfont.getmetrics()
    w_baseline = cell / 2 + ((asc - desc) / 2) / k

    missing = missing_glyphs(wfont, wide)
    missing += missing_glyphs(ImageFont.truetype(nfont_path, 48, index=nfont_index), narrow)
    if missing:
        print(f"WARNING: {len(missing)} glyph(s) missing from the font(s) and will render as tofu: "
              f"{''.join(missing[:60])}{' ...' if len(missing) > 60 else ''}", file=sys.stderr)
        if strict:
            return 1

    # ---- narrow: widest ASCII advance must fit the half-width cell
    nsize = fit_font_size(nfont_path, nfont_index, [c for c in narrow if c != " "], (cell - 2 * s) * k)
    nfont = ImageFont.truetype(nfont_path, nsize, index=nfont_index)
    nasc, ndesc = nfont.getmetrics()
    n_h = 2 * cell
    # Narrow atlas cells are 2x taller than wide cells. Render the narrow
    # glyph in a square logical height, then stretch it into the 1:2 cell
    # before generating the SDF so its screen-space height matches wide glyphs.
    n_baseline = cell / 2 + ((nasc - ndesc) / 2) / k

    def page_image(glyphs, cw, ch_, rows, font, baseline, stretch_y=1.0):
        page = np.zeros((size, size), np.uint8)
        for slot, g in enumerate(glyphs):
            r, c = divmod(slot, 16)
            if r >= rows:
                raise ValueError("too many glyphs for page")
            page[r * ch_:(r + 1) * ch_, c * cw:(c + 1) * cw] = sdf_cell(
                g, font, cw, ch_, s, k, baseline, stretch_y=stretch_y
            )
        return Image.fromarray(page, "L")

    npages = math.ceil(len(wide) / SLOTS_PER_PAGE)
    name_w = page_name_width(npages + 1)
    for stale in glob.glob(f"{out}/page_*.png"):       # stale generated pages must not remain in the atlas directory
        os.remove(stale)
    page_image(narrow, cell, n_h, NARROW_CELLS // 16, nfont, n_baseline, stretch_y=2.0).save(f"{out}/{page_name(0, name_w)}")
    for p in range(npages):
        chunk = wide[p * SLOTS_PER_PAGE:(p + 1) * SLOTS_PER_PAGE]
        page_image(chunk, cell, cell, 16, wfont, w_baseline).save(f"{out}/{page_name(p + 1, name_w)}")
        print(f"page {p + 1}/{npages}", end="\r", flush=True)

    digest = hashlib.sha256()
    with open(charset_file, "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            digest.update(chunk)
    meta = {"page_format": fmt, "cell": cell, "spread": s, "slice": size, "pages": npages + 1,
            "narrow_cell": [cell, n_h], "wide_cell": [cell, cell], "sdf_edge": 0.5,
            "missing_glyphs": "".join(missing), "charset_file": os.path.basename(charset_file),
            "charset_sha256": digest.hexdigest()}
    with open(f"{out}/atlas_meta.json", "w") as f:
        json.dump(meta, f, indent=1)
    print(f"\nwrote {npages + 1} pages of {size}x{size} to {out}/  (page format {fmt})")
    # Report what was ACTUALLY written, not a table of hypotheticals.
    print_vram(npages + 1, cells=tuple(sorted({32, 48, 64, 96, cell})), header="texture cost of this atlas:")
    return 0


if __name__ == "__main__":
    sys.exit(main())
