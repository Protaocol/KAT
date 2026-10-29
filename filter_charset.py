from __future__ import annotations

import argparse
import sys
import unicodedata
from pathlib import Path

from opencc import OpenCC
from wordfreq import zipf_frequency


# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

# wordfreq Zipf scale: ~7 extremely common, ~3 "uncommon but encountered", ~2 rare.
# 3.0 is deliberately conservative: it strips archaic/obscure material without
# reducing Chinese to only HSK-level characters.
DEFAULT_CJK_MIN_ZIPF = 3.0


# Common Chinese punctuation/typography. Deliberately NOT the whole U+3000-U+303F block,
# which also holds symbols you don't want in a practical Chinese font.
CHINESE_PUNCTUATION = set(
    """
    ，。；：？！、·…
    —–‐-﹣－〜～
    「」『』【】〔〕〖〗〘〙〚〛
    《》〈〉
    （）［］｛｝
    “”‘’‘’
    ﹁﹂﹃﹄
    〃々〆〇
    ※
    nbsp
    """.replace("nbsp", "")  # keep the literal list readable
)

# Symbols that show up in ordinary Chinese/technical text. NOT an exhaustive symbol set --
# emoji and decorative Unicode symbols are intentionally left out.
COMMON_SYMBOLS = set(
    """
    ° ℃ ℉
    © ® ™
    § ¶
    † ‡
    №
    ± × ÷
    = ≠ ≈ ≃
    ≤ ≥
    < > ≪ ≫
    ± ∓
    ∞
    √
    ∑ ∏
    ∂
    ∆
    °
    $ € £ ¥ ₩ ₽
    """
)

# Symbols that are technically Unicode symbols but common enough to keep.
COMMON_MISC_SYMBOLS = set(
    """
    @ # % & + - * / \\ = _ ^ | ~ ` !
    """.replace(" ", "").replace("\n", "")
)

# Fullwidth ASCII block (！ ～ Ａ-Ｚ ａ-ｚ ０-９ ...): happens when a Chinese IME
# is in fullwidth mode, so keep it rather than filtering it away.
FULLWIDTH_START = 0xFF01
FULLWIDTH_END = 0xFF5E

# U+3000 IDEOGRAPHIC SPACE
IDEOGRAPHIC_SPACE = "\u3000"


# ---------------------------------------------------------------------------
# Character classification
# ---------------------------------------------------------------------------

def is_ascii_printable(ch: str) -> bool:
    """Keep normal ASCII keyboard characters."""
    cp = ord(ch)
    return 0x20 <= cp <= 0x7E


def is_fullwidth_ascii(ch: str) -> bool:
    """Keep fullwidth forms of ASCII keyboard characters."""
    cp = ord(ch)
    return FULLWIDTH_START <= cp <= FULLWIDTH_END


def is_latin(ch: str) -> bool:
    """
    Keep Latin-script characters, including accented/precomposed forms.

    Looking at the Unicode name rather than a fixed codepoint range avoids
    accidentally deleting Latin Extended-A/B/etc.
    """
    name = unicodedata.name(ch, "")
    return "LATIN" in name


def is_cjk_unified_ideograph(ch: str) -> bool:
    """
    Accept the basic CJK Unified Ideographs range.

    We intentionally do NOT automatically preserve CJK Compatibility
    Ideographs or the huge Extension B-F ranges because those are where
    enormous quantities of rare/archaic characters live.

    Extension A is optionally accepted below.
    """
    # Basic CJK Unified Ideographs plus Extension A (which holds some genuinely useful
    # regional/slang characters; they still have to pass the frequency filter later).
    # CJK Compatibility Ideographs and Extensions B-F are excluded on purpose: that's
    # where the rare/archaic bulk lives.
    cp = ord(ch)

    # CJK Unified Ideographs
    if 0x4E00 <= cp <= 0x9FFF:
        return True

    # CJK Unified Ideographs Extension A
    if 0x3400 <= cp <= 0x4DBF:
        return True

    return False


def is_bad_control_or_format(ch: str) -> bool:
    """
    Remove invisible control/format characters which are not actual glyphs.

    We intentionally allow normal whitespace separately.
    """
    # Cc = control, Cf = format (zero-width joiner, bidi marks, variation selectors, ...).
    # Plain whitespace is handled separately by the output routine.
    return unicodedata.category(ch) in {"Cc", "Cf"}


def is_allowed_non_cjk(ch: str) -> bool:
    """Characters which should survive regardless of Chinese frequency."""
    if is_ascii_printable(ch):
        return True

    if is_fullwidth_ascii(ch):
        return True

    if is_latin(ch):
        return True

    if ch in CHINESE_PUNCTUATION:
        return True

    if ch in COMMON_SYMBOLS:
        return True

    if ch in COMMON_MISC_SYMBOLS:
        return True

    if ch == IDEOGRAPHIC_SPACE:
        return True

    # Whitespace itself is dealt with separately by the output routine.
    return False


# ---------------------------------------------------------------------------
# OpenCC handling
# ---------------------------------------------------------------------------

class GlyphFilter:
    def __init__(
        self,
        cjk_min_zipf: float = DEFAULT_CJK_MIN_ZIPF,
        keep_file: Path | None = None,
        verbose: bool = False,
    ) -> None:
        self.cc = OpenCC("t2s")
        self.cjk_min_zipf = cjk_min_zipf
        self.verbose = verbose

        self.manual_keep: set[str] = set()

        if keep_file is not None:
            self.load_keep_file(keep_file)

        # Cache frequency checks because a large inventory may contain
        # thousands of CJK characters.
        self.freq_cache: dict[str, float] = {}

    def load_keep_file(self, path: Path) -> None:
        """
        Load explicit characters to preserve.

        Can be:
            - one character per line
            - or arbitrary Unicode text

        Whitespace is ignored except that a literal IDEOGRAPHIC SPACE
        can still be preserved through the normal rules.
        """
        try:
            data = path.read_text(encoding="utf-8")
        except OSError as exc:
            raise SystemExit(f"Could not read keep file {path}: {exc}")

        for ch in data:
            if not ch.isspace():
                self.manual_keep.add(ch)

    def cjk_frequency(self, ch: str) -> float:
        if ch not in self.freq_cache:
            self.freq_cache[ch] = zipf_frequency(ch, "zh")
        return self.freq_cache[ch]

    def is_traditional_variant(self, ch: str) -> bool:
        """True when OpenCC maps this character to a different (Simplified) form."""
        return self.cc.convert(ch) != ch

    def should_keep(self, ch: str) -> bool:
        if ch in self.manual_keep:          # explicit user override wins
            return True

        # Reject obvious controls/format characters.
        if is_bad_control_or_format(ch):
            return False

        # Normal keyboard / Latin / punctuation / symbols.
        if is_allowed_non_cjk(ch):
            return True

        # CJK characters need additional filtering.
        if not is_cjk_unified_ideograph(ch):
            return False

        # Traditional forms OpenCC can convert are redundant (input is already t2s).
        if self.is_traditional_variant(ch):
            return False

        # Frequency filter: drops archaic / obscure / extension / junk glyphs,
        # keeps common modern Chinese.
        return self.cjk_frequency(ch) >= self.cjk_min_zipf


# ---------------------------------------------------------------------------
# Input/output
# ---------------------------------------------------------------------------

def filter_glyphs(
    input_path: Path,
    output_path: Path,
    cjk_min_zipf: float,
    keep_file: Path | None,
    verbose: bool,
) -> None:
    try:
        text = input_path.read_text(encoding="utf-8")
    except OSError as exc:
        raise SystemExit(f"Could not read input file {input_path}: {exc}")

    glyph_filter = GlyphFilter(
        cjk_min_zipf=cjk_min_zipf,
        keep_file=keep_file,
        verbose=verbose,
    )

    kept: list[str] = []
    seen: set[str] = set()

    # Track why things disappear, which is useful for tuning the threshold.
    stats = {
        "input_codepoints": 0,
        "kept": 0,
        "duplicate": 0,
        "control_or_format": 0,
        "not_allowed_script_or_symbol": 0,
        "traditional": 0,
        "rare_cjk": 0,
    }

    for ch in text:
        # Newlines are just separators in the glyph inventory.
        if ch in "\r\n":
            continue

        stats["input_codepoints"] += 1

        if ch in seen:
            stats["duplicate"] += 1
            continue

        # Manual override.
        if ch in glyph_filter.manual_keep:
            seen.add(ch)
            kept.append(ch)
            stats["kept"] += 1
            continue

        if is_bad_control_or_format(ch):
            stats["control_or_format"] += 1
            continue

        if is_allowed_non_cjk(ch):
            seen.add(ch)
            kept.append(ch)
            stats["kept"] += 1
            continue

        if not is_cjk_unified_ideograph(ch):
            stats["not_allowed_script_or_symbol"] += 1
            continue

        if glyph_filter.is_traditional_variant(ch):
            stats["traditional"] += 1
            continue

        freq = glyph_filter.cjk_frequency(ch)

        if freq < cjk_min_zipf:
            stats["rare_cjk"] += 1
            continue

        seen.add(ch)
        kept.append(ch)
        stats["kept"] += 1

    # Stable, deterministic output. CJK glyphs are deliberately frequency-ranked
    # because build_charset.py consumes this file as a ranked source. Non-CJK
    # characters are kept after the ranked CJK block in deterministic codepoint
    # order. Ties use Unicode codepoint order so repeated runs remain identical.
    kept.sort(
        key=lambda ch: (
            0,
            -glyph_filter.cjk_frequency(ch),
            ord(ch),
        )
        if is_cjk_unified_ideograph(ch)
        else (1, 0.0, ord(ch))
    )

    try:
        output_path.write_text(
            "".join(ch + "\n" for ch in kept),
            encoding="utf-8",
            newline="\n",
        )
    except OSError as exc:
        raise SystemExit(f"Could not write output file {output_path}: {exc}")

    print()
    print("Glyph filtering complete")
    print("------------------------")
    print(f"Input:                 {input_path}")
    print(f"Output:                {output_path}")
    print(f"Input codepoints:      {stats['input_codepoints']:,}")
    print(f"Unique kept glyphs:    {stats['kept']:,}")
    print(f"Duplicates removed:    {stats['duplicate']:,}")
    print(f"Controls/format:       {stats['control_or_format']:,}")
    print(f"Other scripts/symbols: {stats['not_allowed_script_or_symbol']:,}")
    print(f"Traditional variants:   {stats['traditional']:,}")
    print(f"Rare CJK removed:       {stats['rare_cjk']:,}")
    print(f"CJK Zipf threshold:     {cjk_min_zipf}")
    print()


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def main() -> int:
    parser = argparse.ArgumentParser(
        description="Remove rare/obsolete/irrelevant glyphs from a Chinese font glyph inventory."
    )

    parser.add_argument(
        "input",
        type=Path,
        help="Input TXT containing the extracted glyphs.",
    )

    parser.add_argument(
        "output",
        type=Path,
        help="Output TXT containing the filtered glyph inventory.",
    )

    parser.add_argument(
        "--cjk-min-zipf",
        type=float,
        default=DEFAULT_CJK_MIN_ZIPF,
        help=(
            "Minimum Chinese character frequency. "
            "Default: %(default)s. "
            "Lower = keep more rare Chinese characters."
        ),
    )

    parser.add_argument(
        "--keep-file",
        type=Path,
        default=None,
        help=(
            "Optional TXT containing characters that must ALWAYS be kept, "
            "regardless of OpenCC/frequency filtering."
        ),
    )

    parser.add_argument(
        "--verbose",
        action="store_true",
        help="Reserved for future detailed diagnostics.",
    )

    args = parser.parse_args()

    if not args.input.exists():
        print(f"Error: input file does not exist: {args.input}", file=sys.stderr)
        return 1

    if args.cjk_min_zipf < 0:
        print("Error: --cjk-min-zipf cannot be negative.", file=sys.stderr)
        return 1

    filter_glyphs(
        input_path=args.input,
        output_path=args.output,
        cjk_min_zipf=args.cjk_min_zipf,
        keep_file=args.keep_file,
        verbose=args.verbose,
    )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())