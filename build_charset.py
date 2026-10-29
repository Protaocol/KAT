"""
Build charset.json for the paged KAT client, atlas tool and shader.

  python build_charset.py --freq my_freq_list.txt --count 3000 -o charset.json
  python build_charset.py --corpus novels.txt chat_logs.txt --count 3000 -o charset.json

Where the ranked characters come from (pick one):
  --freq    file whose matching characters are already in frequency order (first occurrence wins).
            One char per line, "char<TAB>count", "rank char count", or one long string all work.
  --corpus  plain-text files; characters are counted and ranked for you and a coverage curve is printed,
            so you can choose --count from data instead of guessing.

Which characters count as rankable (default: CJK ideographs):
  --pattern REGEX     e.g. Hangul  "[\\uac00-\\ud7a3]"   kana+kanji  "[\\u3040-\\u30ff\\u4e00-\\u9fff]"
                      Cyrillic "[\\u0400-\\u04ff]"

Page format (--format 2|3, default 2)
  2  pages 1..14,  max 3570 wide glyphs  (default; what every existing avatar uses)
  3  pages 1..253, max 64515 wide glyphs (opt-in: one page number per synced float; the avatar must be built with the
     installer's "Unpacked page format (format 3)" checkbox). --count is bounded by the chosen format.
  The format is written into charset.json; every other tool reads it from there.

Emoji (optional, shown in full colour; see kat_emoji.py)
  --emoji "😭😂❤️"     emoji to include, typed right here
  --emoji-dir DIR      folder of images; the file name is the shortcode, so DIR/sob.png lets people type :sob:
  Emoji live on their own pages after the wide text pages, and those pages count towards the page limit: in format 2 the wide
  text pages plus the emoji pages have to fit in 14 (a page holds 255 glyphs). So one emoji page costs you 255 wide characters.
  The tool reserves the page for you and says how much to lower --count if the text no longer fits.
  In the client, :sob: is 5 characters but becomes one glyph that takes two slots, like any wide glyph.

Symbols (format 3)
  When the format has loads of room left over after the ranked characters, the charset also gets a block of symbols: roman
  numerals, circled numbers, arrows, math signs, shapes, box drawing and so on (see SYMBOLS). --symbols auto (the default) does
  this in format 3 when at least SYMBOLS_MIN_FREE wide slots are still free; "on" always adds them, "off" never does.
  Pass --symbols-font with your wide font and only the symbols that font can actually draw are added.

Layout rules
  narrow set (page 0, 1:2 cells, max 128)   ASCII 32..126 + euro + degree by default (--narrow to replace)
  wide set (pages 1..N, square cells, max 3570 in format 2 / 64515 in format 3, each drawn over TWO slots)
                      Chinese punctuation by default (--punct to replace, --punct "" for none)
  Every ranked/--extra character is classified by Unicode East Asian Width: Wide/Fullwidth -> wide set,
  everything else (Latin, Cyrillic, Greek, ...) -> narrow set. --punct chars are always wide, --narrow always narrow.
  Most frequent characters land on the earliest wide pages.
"""

import argparse
import json
import re
import sys
import unicodedata
from collections import Counter

from kat_codec import (NARROW_CELLS, PAGE_FORMAT_DEFAULT, SLOTS_PER_PAGE, Charset, check_format, max_wide,
                       required_page_format, wide_pages)
from kat_emoji import collect_emoji
from kat_protocol import PAGE_FORMATS, bits_per_slot

DEFAULT_PATTERN = r"[\u4e00-\u9fff]"
NARROW_BASE = "".join(chr(c) for c in range(32, 127)) + "€°"
WIDE_PUNCT = "，。、；：？！“”‘’（）《》【】—…·「」『』～"


def _span(first: int, last: int) -> str:
    return "".join(chr(c) for c in range(first, last + 1))


# Extra symbols for charsets with space to spare (format 3). They're always put in the wide set, even though some of them
# are "ambiguous width" in Unicode, because in a CJK font they're drawn full width anyway.
SYMBOLS = (
    _span(0x2160, 0x217F)                 # roman numerals  Ⅰ Ⅱ Ⅲ ... ⅰ ⅱ ⅲ ...
    + _span(0x2460, 0x249B)               # circled, bracketed and dotted numbers  ① ⑴ ⒈
    + _span(0x2776, 0x277F)               # ❶ ... ❿
    + _span(0x3220, 0x3229)               # ㈠ ... ㈩
    + _span(0x3280, 0x3289)               # ㊀ ... ㊉
    + _span(0x2190, 0x2199) + _span(0x21D0, 0x21D5)       # arrows
    + "±×÷≠≈≤≥∞∑∏√∫∂∆∈∉∩∪⊂⊃∠⊥∥∴∵∝≡≒⊕⊗∀∃∅∧∨¬"      # maths
    + "■□▲△▼▽◆◇○●◎◐◑★☆"                  # shapes
    + "♀♂♠♣♥♦♪♫♭♯✓✗"                        # cards, music, ticks
    + "℃℉㎡㎝㎞㎏％‰′″※№℡㈱＄￥￠￡§¶†‡•◦"      # units, currency, document marks
    + _span(0x2500, 0x257F)               # box drawing
)
SYMBOL_SET = frozenset(SYMBOLS)
SYMBOLS_MIN_FREE = 4000                   # "auto" adds the symbols only if at least this many wide slots are still free


def _is_wide_char(ch: str) -> bool:
    return unicodedata.east_asian_width(ch) in ("W", "F")


def build_sets(ranked: list[str], count: int | None = None, extra: str = "",
               punct: str = WIDE_PUNCT, narrow_base: str = NARROW_BASE,
               page_format: int = PAGE_FORMAT_DEFAULT, reserved_pages: int = 0,
               symbols: str = "auto", symbol_ok=None, exclude: str = "") -> tuple[str, str]:
    """Returns (narrow, wide) as strings of glyphs. `count` caps how many of the ranked characters get added.
    Room is 3570 wide glyphs in format 2 and 64515 in format 3. `reserved_pages` wide pages are kept free for emoji pages,
    since those use page numbers too. `symbols` is "auto" / "on" / "off" (see SYMBOLS); `symbol_ok(ch)` can veto a symbol, and
    anything in `exclude` (say, the emoji) is never added as text."""
    check_format(page_format)
    exclude = set(exclude)                       # emoji beat plain text glyphs: a text copy of one is simply dropped
    ranked = [c for c in ranked if c not in exclude]
    extra = "".join(c for c in extra if c not in exclude)
    punct = "".join(c for c in punct if c not in exclude)
    capacity = max_wide(page_format) - reserved_pages * SLOTS_PER_PAGE
    narrow = list(dict.fromkeys(narrow_base))
    wide = [c for c in dict.fromkeys(punct) if c not in narrow]
    seen = set(narrow) | set(wide)

    def add(ch: str) -> None:
        (wide if _is_wide_char(ch) else narrow).append(ch)
        seen.add(ch)

    for ch in extra:
        if ch not in seen and not ch.isspace():
            add(ch)
    picked = 0
    for ch in ranked:
        if count is not None and picked >= count:
            break
        if ch not in seen:
            add(ch)
            picked += 1
    if symbols not in ("auto", "on", "off"):
        raise ValueError("symbols must be auto, on or off")
    free = capacity - len(wide)
    if symbols == "on" or (symbols == "auto" and page_format == 3 and free >= SYMBOLS_MIN_FREE):
        for ch in dict.fromkeys(SYMBOLS):
            if len(wide) >= capacity:
                break
            if ch in seen or ch in exclude or (symbol_ok is not None and not symbol_ok(ch)):
                continue
            wide.append(ch)                  # straight into wide, whatever its Unicode width says
            seen.add(ch)
    if len(narrow) > NARROW_CELLS:
        raise ValueError(f"{len(narrow)} narrow glyphs > {NARROW_CELLS} cells; lower --count / trim --extra "
                         f"(scripts like Cyrillic or Greek are narrow and share page 0 with ASCII)")
    if len(wide) > capacity:
        need = required_page_format(len(wide) + reserved_pages * SLOTS_PER_PAGE)
        hint = f"; lower --count, or use --format {need}" if need and need != page_format else "; lower --count"
        if reserved_pages:
            hint = (f"; {reserved_pages} page(s) are kept free for emoji, so lower --count by "
                    f"{len(wide) - capacity}" + (f", or use --format {need}" if need and need != page_format else ""))
        raise ValueError(f"format {page_format}: {len(wide)} wide glyphs > {capacity} "
                         f"({wide_pages(page_format) - reserved_pages} pages x {SLOTS_PER_PAGE}){hint}")
    return "".join(narrow), "".join(wide)


def _usable_glyph(ch: str) -> bool:
    """Characters worth putting in a text box: printable, not a combining mark / control / private-use / odd whitespace."""
    if ch == " ":
        return True
    if ch in ("\ufffd", "\ufffe", "\uffff", "\u303f"):
        return False
    return unicodedata.category(ch) not in ("Cc", "Cf", "Cs", "Co", "Cn", "Zs", "Zl", "Zp", "Mn", "Me")


def _fill_priority(cp: int) -> int:
    """Which font glyphs are added first when the charset has spare room (lower = earlier)."""
    if 0x3000 <= cp <= 0x303F or 0xFF00 <= cp <= 0xFFEF or 0x2000 <= cp <= 0x2BFF:
        return 0                     # punctuation, symbols, arrows, box drawing, fullwidth forms
    if 0x3040 <= cp <= 0x30FF or 0x31F0 <= cp <= 0x31FF:
        return 1                     # kana
    if cp < 0x2000:
        return 2                     # Latin / Greek / Cyrillic ...
    if 0x4E00 <= cp <= 0x9FFF:
        return 3                     # CJK unified ideographs
    if 0xAC00 <= cp <= 0xD7A3:
        return 4                     # Hangul syllables
    return 5


def extend_with_font(narrow: str, wide: str, wide_font_cps, narrow_font_cps,
                     page_format: int = PAGE_FORMAT_DEFAULT) -> tuple[str, str, dict]:
    """
    Fill the spare room of an existing charset with every glyph the fonts can draw.

    wide_font_cps / narrow_font_cps: sets of code points the wide font / narrow font contain (a wide glyph needs the
    wide font, a narrow one the narrow font; they may be the same font). Existing glyphs keep their current positions
    because position is glyph identity; new glyphs are appended, most useful scripts
    first (see _fill_priority). Stops when the narrow page (128) or the wide pages (per page format) are full.
    Returns (narrow, wide, report).
    """
    have = set(narrow) | set(wide)
    cand_wide, cand_narrow = [], []
    for cp in sorted(set(wide_font_cps) | set(narrow_font_cps), key=lambda c: (_fill_priority(c), c)):
        if cp > 0x10FFFF or 0xD800 <= cp <= 0xDFFF:
            continue
        ch = chr(cp)
        if ch in have or not _usable_glyph(ch):
            continue
        if _is_wide_char(ch):
            if cp in wide_font_cps:
                cand_wide.append(ch)
        elif cp in narrow_font_cps:
            cand_narrow.append(ch)
    room_n = NARROW_CELLS - len(narrow)
    room_w = max_wide(page_format) - len(wide)
    add_n, add_w = cand_narrow[:max(room_n, 0)], cand_wide[:max(room_w, 0)]
    report = {
        "added_narrow": len(add_n), "added_wide": len(add_w),
        "left_out_narrow": len(cand_narrow) - len(add_n), "left_out_wide": len(cand_wide) - len(add_w),
    }
    return narrow + "".join(add_n), wide + "".join(add_w), report


def ranked_from_freq_file(path: str, pattern: str = DEFAULT_PATTERN) -> list[str]:
    with open(path, encoding="utf-8") as f:
        return list(dict.fromkeys(re.findall(pattern, f.read())))


def counts_from_corpus(paths: list[str], pattern: str = DEFAULT_PATTERN) -> Counter:
    c: Counter = Counter()
    rx = re.compile(pattern)
    for p in paths:
        with open(p, encoding="utf-8", errors="ignore") as f:
            c.update(rx.findall(f.read()))
    return c


def print_curve(counts: Counter) -> None:
    total = sum(counts.values())
    if not total:
        return
    ordered = [n for _, n in counts.most_common()]
    print(f"corpus: {total:,} matching chars, {len(ordered):,} distinct")
    for k in (500, 1000, 1500, 2000, 2500, 3000, 3500, 5000, 8000, 12000, 20000):
        if k <= len(ordered):
            print(f"  top {k:>4}: {sum(ordered[:k]) / total:7.3%} coverage")


# VRChat's PC "Texture Memory" performance-rank thresholds (MB, whole avatar; VRChat 2023.1.2 release notes and
# creators.vrchat.com). An atlas is only part of an avatar's textures: these are what its share should be judged against.
VRAM_RANKS_MB = (("Excellent", 40), ("Good", 75), ("Medium", 110), ("Poor", 150))


def vram_rank(mb: float) -> str:
    """Performance rank an avatar would land in on texture memory ALONE if this were all it had."""
    for name, limit in VRAM_RANKS_MB:
        if mb <= limit:
            return name
    return "Very Poor"


def vram_mb(pages: int, cell: int, bytes_per_texel: float, mips: bool = True) -> float:
    """VRAM of ``pages`` atlas slices of (16*cell)^2 texels; the mip chain adds a third."""
    side = 16 * cell
    return pages * side * side * bytes_per_texel * (4 / 3 if mips else 1) / 1024 / 1024


def print_vram(pages: int, cells=(32, 48, 64, 96), header: str = "") -> None:
    """Print the ACTUAL texture cost of ``pages`` atlas pages (page 0 included), per cell size and encoding, and the
    VRChat texture-memory rank that cost would put an avatar in by itself. R8 = 1 byte/texel, BC4 = 0.5, +1/3 mips."""
    if header:
        print(header)
    print(f"  atlas VRAM for {pages} page(s) (Texture2DArray, with mips); PC rank limits: "
          + ", ".join(f"{n} {mb} MB" for n, mb in VRAM_RANKS_MB) + ", above = Very Poor")
    for cell in cells:
        side = 16 * cell
        r8, bc4 = vram_mb(pages, cell, 1.0), vram_mb(pages, cell, 0.5)
        print(f"    cell {cell:>3}px -> {side}x{side} per page: {r8:8.1f} MB as R8 ({vram_rank(r8)}), "
              f"{bc4:8.1f} MB as BC4 ({vram_rank(bc4)})")
    if pages > 30:
        print("    note: that is before the rest of the avatar's textures. Consider BC4 (halves it), a smaller cell, or a "
              "smaller charset. Nothing here stops you; just don't find out after upload.")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    src = ap.add_mutually_exclusive_group(required=True)
    src.add_argument("--freq")
    src.add_argument("--corpus", nargs="+")
    ap.add_argument("--pattern", default=DEFAULT_PATTERN, help="regex of rankable characters")
    ap.add_argument("--format", type=int, default=PAGE_FORMAT_DEFAULT, choices=sorted(PAGE_FORMATS),
                    help="page wire format: 2 = default (max 3570 wide glyphs), 3 = unpacked (max 64515). Bounds --count.")
    ap.add_argument("--count", type=int, default=None, help="how many ranked characters to keep (default: as many as the selected format holds)")
    ap.add_argument("--extra", default="", help="characters to force-include")
    ap.add_argument("--punct", default=WIDE_PUNCT, help="always-wide punctuation set (default: Chinese)")
    ap.add_argument("--narrow", default=NARROW_BASE, help="always-narrow base set (default: ASCII + euro + degree)")
    ap.add_argument("--emoji", default="", help='emoji to include, typed here, e.g. "😭😂❤️"')
    ap.add_argument("--emoji-dir", help="folder of emoji images; the file name is the shortcode (sob.png -> :sob:)")
    ap.add_argument("--symbols", choices=("auto", "on", "off"), default="auto",
                    help="roman numerals, arrows, maths and similar symbols: auto = only in format 3 when there's plenty of room")
    ap.add_argument("--symbols-font", help="your wide font: only symbols it can draw get added")
    ap.add_argument("--symbols-font-index", type=int, default=0)
    ap.add_argument("-o", "--out", default="charset.json")
    args = ap.parse_args()

    return generate(freq=args.freq, corpus=args.corpus, pattern=args.pattern, fmt=args.format, count=args.count,
                    extra=args.extra, punct=args.punct, narrow=args.narrow, out=args.out,
                    emoji=args.emoji, emoji_dir=args.emoji_dir, symbols=args.symbols,
                    symbols_font=args.symbols_font, symbols_font_index=args.symbols_font_index)


def generate(*, freq=None, corpus=None, pattern=DEFAULT_PATTERN, fmt=PAGE_FORMAT_DEFAULT, count=None,
             extra="", punct=WIDE_PUNCT, narrow=NARROW_BASE, out="charset.json",
             emoji="", emoji_dir=None, symbols="auto", symbols_font=None, symbols_font_index=0) -> int:
    if freq:
        ranked = ranked_from_freq_file(freq, pattern)
    else:
        counts = counts_from_corpus(corpus, pattern)
        print_curve(counts)
        ranked = [ch for ch, _ in counts.most_common()]

    try:
        emojis = collect_emoji(inline=emoji, emoji_dir=emoji_dir)
        symbol_ok = None
        if symbols_font:
            from PIL import ImageFont
            from build_atlas import missing_glyphs      # imported here because build_atlas imports this module
            lacking = set(missing_glyphs(ImageFont.truetype(symbols_font, 48, index=symbols_font_index), SYMBOLS))
            symbol_ok = lambda ch: ch not in lacking
        narrow_set, wide_set = build_sets(ranked, count, extra, punct, narrow, page_format=fmt, reserved_pages=emojis.pages(),
                                          symbols=symbols, symbol_ok=symbol_ok, exclude=set(emojis.color))
        cs = Charset(narrow_set, wide_set, page_format=fmt, color=emojis.color, emoji_aliases=emojis.aliases,
                     color_sources=emojis.sources, emoji_synonyms=emojis.synonyms)
    except (ValueError, OSError) as e:
        print(f"error: {e}", file=sys.stderr)
        return 1

    pages = cs.sdf_pages
    with open(out, "w", encoding="utf-8") as f:
        json.dump(cs.to_json_dict(), f, ensure_ascii=False)
    print(f"format {fmt}: narrow {len(narrow_set)}/{NARROW_CELLS}, wide {len(wide_set)}/{max_wide(fmt)} -> {pages} atlas page(s)  ({out})")
    symbol_count = sum(1 for ch in wide_set if ch in SYMBOL_SET)
    if symbol_count >= 100:
        print(f"symbols: {symbol_count} (roman numerals, arrows, maths, shapes, box drawing...)")
    if cs.has_color:
        if emojis.overridden:
            print(f"pictures from the emoji folder replace the font for: {' '.join(emojis.overridden)}")
        for dup, owner in emojis.merged:
            print(f"identical picture, shares a glyph: {dup} -> {owner}")
        print(f"emoji: {len(cs.color)} ({len(cs.emoji_aliases)} with a shortcode) on {cs.color_page_count} emoji page(s) "
              f"starting at page {cs.color_first_page}; wide + emoji pages = {cs.pages - 1}/{wide_pages(fmt)}")
    if count is not None and len(ranked) < count:
        print(f"note: only {len(ranked)} ranked characters were available (asked for {count})")
    if fmt == 3:
        print(f"format 3 wire cost: {bits_per_slot(3)} bits/slot instead of {bits_per_slot(2)} (see README 'Page formats')")
        if len(wide_set) <= max_wide(2):
            print(f"WARNING: only {len(wide_set)} wide glyphs -- that fits format 2 ({max_wide(2)}). Format 3 costs more "
                  "wire bits, animator layers and clips for headroom you are not using. Drop --format 3 unless you plan to grow.")
    print_vram(pages)
    if cs.has_color:
        for cell in (32, 48, 64):
            print(f"  emoji pages at cell {cell}px: {vram_mb(cs.color_page_count, cell, 1.0):.1f} MB as BC7 "
                  f"({vram_mb(cs.color_page_count, cell, 4.0):.1f} MB if it falls back to RGBA32)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
