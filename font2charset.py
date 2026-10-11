"""Extract Unicode characters advertised by a font cmap.

Usage:
    python font2charset.py FONT_PATH OUTPUT_PATH
"""

from __future__ import annotations

import argparse

from fontTools.ttLib import TTFont


def extract_font_chars(font_path: str, output_path: str, font_index: int = 0) -> int:
    with TTFont(font_path, fontNumber=font_index) as font:
        all_chars = set()
        for table in font["cmap"].tables:
            for codepoint in table.cmap:
                try:
                    all_chars.add(chr(codepoint))
                except (ValueError, OverflowError):
                    continue

    sorted_chars = sorted(all_chars, key=ord)
    with open(output_path, "w", encoding="utf-8", newline="\n") as f:
        f.write("".join(sorted_chars))

    print(f"Extracted {len(sorted_chars):,} characters to {output_path}")
    return len(sorted_chars)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("font", help="input .ttf/.otf/.ttc font")
    ap.add_argument("output", help="output text file")
    ap.add_argument("--font-index", type=int, default=0, help="face index inside a .ttc collection (default: 0)")
    args = ap.parse_args()
    extract_font_chars(args.font, args.output, args.font_index)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
