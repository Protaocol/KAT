"""
Render charset.json into SDF atlas pages for the paged KAT shader.

  python build_atlas.py charset.json --font NotoSansCJK-Regular.ttc --font-index 2 -o atlas/
  python build_atlas.py charset.json --font wide.otf --narrow-font mono.ttf --cell 64 -o atlas/

Emoji (only if the charset has a "color" list, see kat_emoji.py):
  python build_atlas.py charset.json --font wide.otf --emoji-dir my_emoji/ -o atlas/
  Typed emoji are drawn from --font (or --narrow-font) when that font has them, otherwise from the system's colour emoji font
  (Segoe UI Emoji on Windows, Apple Color Emoji on macOS, Noto Color Emoji on Linux). Emoji that come from images are read
  from --emoji-dir, falling back to the folder next to charset.json and then the current folder.
  --color-cell   size of one emoji cell in px (default: same as --cell)
  --emoji-padding  empty margin around each emoji in px (default: cell / 16)
  This writes atlas/color_page_00.png etc. (RGBA). Unity turns them into a BC7 array.

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

from build_charset import print_vram, vram_mb
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


# ---------------------------------------------------------------------------------------------------- emoji

def color_page_name(index: int, width: int) -> str:
    return f"color_page_{index:0{width}d}.png"


def find_system_emoji_font() -> tuple[str, int] | None:
    """Looks for the colour emoji font the OS ships. Returns (path, face index) or None."""
    home = os.path.expanduser("~")
    candidates = [
        "C:/Windows/Fonts/seguiemj.ttf",                                   # Windows
        "/System/Library/Fonts/Apple Color Emoji.ttc",                     # macOS
        "/usr/share/fonts/truetype/noto/NotoColorEmoji.ttf",               # Linux, a few usual places
        "/usr/share/fonts/noto/NotoColorEmoji.ttf",
        "/usr/share/fonts/google-noto-color-emoji/NotoColorEmoji.ttf",
        "/usr/share/fonts/google-noto-emoji/NotoColorEmoji.ttf",
        os.path.join(home, ".fonts", "NotoColorEmoji.ttf"),
        os.path.join(home, ".local", "share", "fonts", "NotoColorEmoji.ttf"),
    ]
    for path in candidates:
        if os.path.isfile(path):
            return path, 0
    try:                                                                   # fontconfig knows where it is, if it's around
        import subprocess
        out = subprocess.run(["fc-match", "-f", "%{file}", "emoji"], capture_output=True, text=True, timeout=5).stdout.strip()
        if out and os.path.isfile(out) and "emoji" in out.lower():
            return out, 0
    except (OSError, subprocess.SubprocessError):
        pass
    return None


def load_emoji_font(path: str, index: int = 0):
    """Bitmap emoji fonts only open at the size they were made for (109 for Noto, 160 for Apple), so try a few. Scalable
    ones like Segoe UI Emoji take anything."""
    last = None
    for size in (109, 160, 128, 136, 96, 72, 64):
        try:
            return ImageFont.truetype(path, size, index=index)
        except OSError as exc:
            last = exc
    raise OSError(f"can't open {path} as an emoji font: {last}")


def render_emoji_glyph(key: str, font) -> Image.Image:
    """Draws one emoji (sequences too) with the given font into an RGBA image. Comes back fully transparent if the font has
    nothing for it."""
    left, top, right, bottom = font.getbbox(key, anchor="ls")
    asc, desc = font.getmetrics()
    w = max(int(math.ceil(max(right, font.getlength(key)))) + 4, 1)
    h = max(asc + desc + 4, 1)
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(img).text((0, asc), key, font=font, embedded_color=True, anchor="ls")
    return img


def fit_cell_rgba(img: Image.Image, cell: int, pad: int) -> np.ndarray:
    """Scales the image to fit inside the cell (minus the padding), keeps its proportions, and centres it. The resize is done
    on premultiplied pixels, so the transparent parts can't pull dark fringes into the edges. Gives back a (cell, cell, 4)
    uint8 array with normal (straight) alpha."""
    out = np.zeros((cell, cell, 4), np.uint8)
    if img.getchannel("A").getbbox() is None:
        return out
    box = cell - 2 * pad
    scale = box / max(img.size)
    nw, nh = max(1, round(img.width * scale)), max(1, round(img.height * scale))
    small = img.convert("RGBa").resize((nw, nh), Image.Resampling.LANCZOS).convert("RGBA")
    x0, y0 = (cell - nw) // 2, (cell - nh) // 2
    out[y0:y0 + nh, x0:x0 + nw] = np.asarray(small)
    return out


def bleed_rgb(rgba: np.ndarray) -> np.ndarray:
    """Gives fully transparent pixels the colour of the nearest visible pixel. Filtering and mipmaps blend neighbours together,
    and the invisible black that transparent pixels usually carry would otherwise darken the edge of every emoji."""
    a = rgba[..., 3]
    if a.max() == 0 or a.min() > 0:
        return rgba
    idx = distance_transform_edt(a == 0, return_distances=False, return_indices=True)
    out = rgba.copy()
    out[..., :3] = rgba[idx[0], idx[1], :3]
    return out


def resolve_emoji_image(name: str, search: list[str]) -> str | None:
    for base in search:
        cand = os.path.join(base, name)
        if os.path.isfile(cand):
            return cand
    return None


def render_color_pages(cs: Charset, *, provided_fonts, search_dirs, cell, pad):
    """Draws every emoji of the charset. `provided_fonts` is a list of (path, index) for the fonts the user gave us, tried first;
    the system emoji font is the fallback. Returns (pages, keys that came out blank)."""
    provided = [(path, index, ImageFont.truetype(path, 48, index=index)) for path, index in provided_fonts]
    loaded: dict = {}                                  # (path, index) -> font opened at an emoji-friendly size

    def emoji_font(path, index):
        if (path, index) not in loaded:
            loaded[(path, index)] = load_emoji_font(path, index)
        return loaded[(path, index)]

    system = find_system_emoji_font()
    empty: list[str] = []
    cells: list[np.ndarray] = []
    for key in cs.color:
        src = cs.color_sources.get(key)
        if src:
            path = resolve_emoji_image(src, search_dirs)
            if path is None:
                raise FileNotFoundError(f"emoji image {src!r} not found in: {', '.join(search_dirs)} (use --emoji-dir)")
            with Image.open(path) as im:
                img = im.convert("RGBA")
        else:
            img = None
            for path, index, probe_font in provided:   # your font first; its first character tells us if it knows this emoji
                if not missing_glyphs(probe_font, [key[0]]):
                    img = render_emoji_glyph(key, emoji_font(path, index))
                    break
            if img is None:
                if system is None:
                    raise ValueError(f"{key!r} isn't in your font and no system colour emoji font was found "
                                     "(install Noto Color Emoji, or put a picture of it in --emoji-dir)")
                img = render_emoji_glyph(key, emoji_font(*system))
        arr = bleed_rgb(fit_cell_rgba(img, cell, pad))
        if arr[..., 3].max() == 0:
            empty.append(key)
        cells.append(arr)
    size = 16 * cell
    pages = []
    for p0 in range(0, len(cells), SLOTS_PER_PAGE):
        page = np.zeros((size, size, 4), np.uint8)
        for slot, arr in enumerate(cells[p0:p0 + SLOTS_PER_PAGE]):
            r, c = divmod(slot, 16)
            page[r * cell:(r + 1) * cell, c * cell:(c + 1) * cell] = arr
        pages.append(page)
    return pages, empty


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
    ap.add_argument("--emoji-dir", help="folder of emoji images (default: next to charset.json, then the current folder)")
    ap.add_argument("--color-cell", type=int, help="size of one emoji cell in px (default: --cell)")
    ap.add_argument("--emoji-padding", type=int, help="empty margin around each emoji in px (default: cell / 16)")
    ap.add_argument("-o", "--out", default="atlas")
    args = ap.parse_args()

    return render_atlas(args.charset, args.font, font_index=args.font_index, narrow_font=args.narrow_font,
                        narrow_font_index=args.narrow_font_index, cell_size=args.cell, spread=args.spread,
                        supersample=args.supersample, strict=args.strict, out=args.out,
                        emoji_dir=args.emoji_dir,
                        color_cell=args.color_cell, emoji_padding=args.emoji_padding)


def render_atlas(charset_path, font, *, font_index=0, narrow_font=None, narrow_font_index=0,
                 cell_size=64, spread=6, supersample=4, strict=False, out="atlas",
                 emoji_dir=None, color_cell=None, emoji_padding=None) -> int:
    with open(charset_path, encoding="utf-8") as f:
        cs = json.load(f)
    fmt = read_charset_format(cs)
    narrow, wide = cs["narrow"], cs["wide"]
    # same capacity / duplicate checks the client applies: fail here, not in game
    charset_obj = Charset(narrow, wide, page_format=fmt, color=cs.get("color"), emoji_aliases=cs.get("emoji_aliases"),
                          color_sources=cs.get("color_sources"), emoji_synonyms=cs.get("emoji_synonyms"))
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
    for stale in glob.glob(f"{out}/page_*.png") + glob.glob(f"{out}/color_page_*.png"):   # stale generated pages must not remain
        os.remove(stale)
    page_image(narrow, cell, n_h, NARROW_CELLS // 16, nfont, n_baseline, stretch_y=2.0).save(f"{out}/{page_name(0, name_w)}")
    for p in range(npages):
        chunk = wide[p * SLOTS_PER_PAGE:(p + 1) * SLOTS_PER_PAGE]
        page_image(chunk, cell, cell, 16, wfont, w_baseline).save(f"{out}/{page_name(p + 1, name_w)}")
        print(f"page {p + 1}/{npages}", end="\r", flush=True)

    # ---- emoji pages: RGBA, laid out in the same 16 x 16 grid of square cells as the wide pages
    ccell = color_cell or cell
    color_pages = 0
    empty_color: list[str] = []
    if charset_obj.has_color:
        if not isinstance(ccell, int) or ccell < 8:
            raise ValueError("color_cell must be an integer >= 8")
        pad = emoji_padding if emoji_padding is not None else max(1, ccell // 16)
        if pad < 0 or 2 * pad >= ccell:
            raise ValueError("emoji_padding must be at least 0 and less than half the emoji cell")
        search = [d for d in (emoji_dir, os.path.dirname(os.path.abspath(charset_path)), os.getcwd()) if d]
        provided = [(font, font_index)] + ([(narrow_font, narrow_font_index)] if narrow_font else [])
        cpages, empty_color = render_color_pages(charset_obj, provided_fonts=provided, search_dirs=search, cell=ccell, pad=pad)
        color_pages = len(cpages)
        cname_w = page_name_width(color_pages + 1)
        for i, arr in enumerate(cpages):
            Image.fromarray(arr, "RGBA").save(f"{out}/{color_page_name(i, cname_w)}")
        if empty_color:
            print(f"WARNING: {len(empty_color)} emoji came out blank (no font had them, or the image is empty): "
                  f"{' '.join(empty_color[:20])}{' ...' if len(empty_color) > 20 else ''}", file=sys.stderr)
            if strict:
                return 1
        if any(len(k) > 1 and "\u200d" in k for k in charset_obj.color if k not in charset_obj.color_sources):
            from PIL import features
            if not features.check("raqm"):
                print("note: this Pillow has no libraqm, so ZWJ emoji (families, professions...) may come out as their separate "
                      "parts. Single emoji, skin tones and flags are fine.", file=sys.stderr)

    digest = hashlib.sha256()
    with open(charset_file, "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            digest.update(chunk)
    meta = {"page_format": fmt, "cell": cell, "spread": s, "slice": size, "pages": npages + 1 + color_pages,
            "sdf_pages": npages + 1, "color_pages": color_pages,
            "color_page_start": npages + 1 if color_pages else 0, "color_cell": ccell if color_pages else 0,
            "color_slice": 16 * ccell if color_pages else 0, "color_glyphs": len(charset_obj.color),
            "narrow_cell": [cell, n_h], "wide_cell": [cell, cell], "sdf_edge": 0.5,
            "missing_glyphs": "".join(missing), "charset_file": os.path.basename(charset_file),
            "charset_sha256": digest.hexdigest()}
    with open(f"{out}/atlas_meta.json", "w") as f:
        json.dump(meta, f, indent=1)
    print(f"\nwrote {npages + 1} pages of {size}x{size} to {out}/  (page format {fmt})")
    if color_pages:
        print(f"wrote {color_pages} emoji page(s) of {16 * ccell}x{16 * ccell} RGBA (page numbers {npages + 1}..{npages + color_pages}); "
              f"Unity compresses them to BC7: {vram_mb(color_pages, ccell, 1.0):.1f} MB")
    # Report what was ACTUALLY written, not a table of hypotheticals.
    print_vram(npages + 1, cells=tuple(sorted({32, 48, 64, 96, cell})), header="texture cost of this atlas:")
    return 0


if __name__ == "__main__":
    sys.exit(main())
