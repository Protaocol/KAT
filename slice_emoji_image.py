#!/usr/bin/env python3
"""
Cuts a big picture into KAT emoji blocks, so that typed on the board it shows up as the original picture.

    python slice_emoji_image.py abc.png
    -> abc/abc01.png, abc/abc02.png, ... (left to right, top to bottom)

Type them into the board as   :abc01::abc02::abc03:  <newline>  :abc04::abc05::abc06:  ...   (one line per row of blocks).

How the board shows an emoji (this is what the script compensates for):

  * An emoji takes two slots in a row. A slot is quad_width / row_length wide and quad_height / rows tall, so with the default
    32 x 4 grid on a 3:1 quad one emoji is 0.75 : 1 (width : height) on the board, not square.
  * build_atlas.py puts the picture into a SQUARE cell, keeping its proportions, scaled to the cell minus a transparent margin of
    `pad` pixels on every side (default cell // 16), and the shader then stretches the whole cell over those two slots.
  * So the margin is a no-show zone: whatever part of a block falls into it is never visible, and a picture that is not square
    gets squashed by the stretch.

So for each block of the picture (4 rows, N columns, each block the shape of one emoji on the board) this script
  1. cuts away the strip that lands in the margin (pad / cell of the block on every side), and
  2. stretches what is left to a square, which build_atlas.py then fills into the cell without changing its proportions.
Put back by the board, every pixel lands where it was in the original and the proportions are right.

N depends on the picture: N = round(rows * picture_aspect / emoji_aspect). If the picture does not fit that exactly, --fit
decides what happens (default: transparent margins, nothing is lost).

Needs Pillow only (numpy is used for --preview).
"""

import argparse
import math
import os
import re
import sys

from PIL import Image

ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
ap.add_argument("input", help="any image Pillow can read, e.g. abc.png")
ap.add_argument("-o", "--out", help="folder to create the output folder in (default: next to the input)")
ap.add_argument("--name", help="folder / file name stem (default: the input's file name, made safe for a shortcode)")
g = ap.add_argument_group("the board (defaults match KAT as installed: 3:1 quad, 32 x 4 slots)")
g.add_argument("--aspect", type=float, default=3.0, help="quad width / height in the world (default 3.0, a 0.5 x 0.1667 quad)")
g.add_argument("--row-length", type=int, default=32, help="slots per row (_RowLength, default 32)")
g.add_argument("--rows", type=int, default=4, help="rows (_RowColumns, default 4)")
g = ap.add_argument_group("the emoji atlas (build_atlas.py)")
g.add_argument("--cell", type=int, default=64, help="--color-cell used for the atlas (default 64)")
g.add_argument("--pad", type=int, default=None, help="--emoji-padding in px (default: cell // 16, at least 1)")
g = ap.add_argument_group("output")
g.add_argument("--fit", choices=["pad", "crop", "stretch"], default="pad",
               help="when the picture's shape is not exactly N blocks wide: pad = add transparent margins (default), "
                    "crop = cut the edges off, stretch = distort it slightly")
g.add_argument("--max-size", type=int, default=512, help="largest side of an output block in px (default 512); build_atlas scales them down anyway")
g.add_argument("--raw", action="store_true", help="do not stretch the blocks to squares (only trims the no-show zone)")
g.add_argument("--preview", action="store_true", help="also write <name>_preview.png: the board's view of the blocks, for checking")
args = ap.parse_args()

if args.row_length < 2 or args.rows < 1 or args.aspect <= 0:
    sys.exit("error: bad board settings")
pad_px = args.pad if args.pad is not None else max(1, args.cell // 16)
if pad_px < 0 or 2 * pad_px >= args.cell:
    sys.exit("error: --pad must be >= 0 and smaller than half of --cell")
p = pad_px / args.cell                      # no-show margin, as a fraction of the block, on every side

# one emoji on the board: two slots wide, one row tall
emoji_aspect = 2.0 * args.aspect * args.rows / args.row_length       # width / height
max_cols = args.row_length // 2

img = Image.open(args.input)
img = img.convert("RGBA")
W, H = img.size
src_aspect = W / H

stem = args.name or os.path.splitext(os.path.basename(args.input))[0]
safe = re.sub(r"[^a-z0-9_-]+", "_", stem.lower()).strip("_") or "emoji"
if safe != stem:
    print(f"note: shortcodes may only use lower case letters, digits, _ and -, so the name '{stem}' becomes '{safe}'")
stem = safe

rows = args.rows
cols = max(1, round(rows * src_aspect / emoji_aspect))
if cols > max_cols:
    print(f"warning: this picture needs {cols} emoji per row but a board row holds {max_cols}; the rows will wrap and the picture will break. "
          f"Use a narrower picture or a wider quad (--aspect).")

# The canvas: exactly cols x rows blocks of the right shape.
canvas_aspect = cols * emoji_aspect / rows
if abs(canvas_aspect - src_aspect) / src_aspect < 1e-3:
    canvas = img
elif args.fit == "stretch":
    nw, nh = (W, max(1, round(W / canvas_aspect))) if canvas_aspect < src_aspect else (max(1, round(H * canvas_aspect)), H)
    canvas = img.convert("RGBa").resize((nw, nh), Image.Resampling.LANCZOS).convert("RGBA")
elif args.fit == "crop":
    if canvas_aspect < src_aspect:       # canvas is narrower: cut the sides
        nw = round(H * canvas_aspect); x0 = (W - nw) // 2
        canvas = img.crop((x0, 0, x0 + nw, H))
    else:                                # canvas is wider: cut top and bottom
        nh = round(W / canvas_aspect); y0 = (H - nh) // 2
        canvas = img.crop((0, y0, W, y0 + nh))
else:                                    # pad
    if canvas_aspect < src_aspect:       # canvas is taller: margins above and below
        cw, ch = W, max(H, round(W / canvas_aspect))
    else:                                # canvas is wider: margins left and right
        cw, ch = max(W, round(H * canvas_aspect)), H
    canvas = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
    canvas.paste(img, ((cw - W) // 2, (ch - H) // 2))
CW, CH = canvas.size

bw, bh = CW / cols, CH / rows            # one block in canvas pixels (bw / bh ~ emoji_aspect)
cw_, ch_ = bw * (1 - 2 * p), bh * (1 - 2 * p)         # what is left of it after the no-show margin
side = max(1, min(args.max_size, math.ceil(max(cw_, ch_))))
out_size = (max(1, min(args.max_size, math.ceil(cw_))), max(1, min(args.max_size, math.ceil(ch_)))) if args.raw else (side, side)

parent = args.out or (os.path.dirname(os.path.abspath(args.input)))
out_dir = os.path.join(parent, stem)
os.makedirs(out_dir, exist_ok=True)

total = cols * rows
digits = len(str(total))
premult = canvas.convert("RGBa")          # resize on premultiplied pixels so transparent parts can't leave dark fringes
blocks = []
for r in range(rows):
    for c in range(cols):
        n = r * cols + c + 1
        x0 = c * bw + p * bw
        y0 = r * bh + p * bh
        box = (x0, y0, x0 + cw_, y0 + ch_)
        tile = premult.resize(out_size, Image.Resampling.LANCZOS, box=box).convert("RGBA")
        name = f"{stem}{n:0{digits}d}"
        tile.save(os.path.join(out_dir, name + ".png"))
        blocks.append(tile)

print(f"{args.input}: {W}x{H} -> {cols} x {rows} = {total} blocks in {out_dir}")
print(f"  one emoji on the board is {emoji_aspect:.3f} : 1; no-show margin {pad_px}px of a {args.cell}px cell = {p * 100:.2f}% on each side")
print(f"  block {bw:.1f}x{bh:.1f}px in the picture, {cw_:.1f}x{ch_:.1f}px kept, written as {out_size[0]}x{out_size[1]}px")
if canvas.size != (W, H):
    print(f"  picture shape adjusted with --fit {args.fit}: {W}x{H} -> {CW}x{CH}")
print("  type it as:")
for r in range(rows):
    print("   " + "".join(f":{stem}{r * cols + c + 1:0{digits}d}:" for c in range(cols)))

if args.preview:
    # What the board shows: each block is fitted into a square cell exactly like build_atlas.fit_cell_rgba (proportions kept, scaled
    # to cell - 2*pad, centred), then the cell is stretched over the emoji's two slots.
    box_px = args.cell - 2 * pad_px
    scale_h = 8                              # preview pixels per cell pixel (vertical); horizontal follows the emoji's shape
    pw, ph = round(args.cell * scale_h * emoji_aspect), args.cell * scale_h
    sheet = Image.new("RGBA", (pw * cols, ph * rows), (0, 0, 0, 0))
    for i, tile in enumerate(blocks):
        s = box_px / max(tile.size)
        nw, nh = max(1, round(tile.width * s)), max(1, round(tile.height * s))
        cell = Image.new("RGBA", (args.cell, args.cell), (0, 0, 0, 0))
        cell.paste(tile.convert("RGBa").resize((nw, nh), Image.Resampling.LANCZOS).convert("RGBA"), ((args.cell - nw) // 2, (args.cell - nh) // 2))
        shown = cell.convert("RGBa").resize((pw, ph), Image.Resampling.LANCZOS).convert("RGBA")
        sheet.paste(shown, ((i % cols) * pw, (i // cols) * ph))
    prev_path = os.path.join(parent, stem + "_preview.png")
    sheet.save(prev_path)
    print(f"  preview (the board's view; the gaps are the no-show margins): {prev_path}")
