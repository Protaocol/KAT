"""
KAT wire codec + charset model.  Two page formats, chosen per charset / per avatar (see kat_protocol.py).

GLYPH MODEL (both formats)
  glyph index g = page * 255 + slot          (slot 0..254)

  page 0        NARROW glyphs (ASCII etc). Atlas cells are 1:2 (w:h) = exactly one text slot.
                Slots 0..127 are real cells (16 x 8 grid); slot 254 is the reserved FOLLOW marker.
  pages 1..N    WIDE glyphs (hanzi + Chinese punctuation). Atlas cells are square (16 x 16 grid),
                drawn across TWO adjacent text slots: [lead][FOLLOW].
                format 2: pages 1..14  -> up to 14 * 255  = 3570  wide glyphs   (default)
                format 3: pages 1..253 -> up to 253 * 255 = 64515 wide glyphs   (opt-in)

  EMOJI pages (optional)  the pages right after the last wide text page. Same square cells and the same [lead][FOLLOW] pair of
                slots as a wide glyph, but the pixels are RGBA instead of an SDF (atlas/color_page_XX.png, which Unity turns into
                its own BC7 array). They use up page numbers like any other page, so in format 2 the wide text pages plus the
                emoji pages have to fit in 14. charset.json describes them with "color", "emoji_aliases" and "color_sources".
                The client swaps ":sob:" and multi-code-point emoji for a single glyph before it lays text out (expand_emoji).

  Slot 0 of page 0 must be a space: an animator slot that was never written reads 0 -> blank.

WIRE (one pointer write = M chars, M even)
  KAT_CharSync0..M-1               one symbol each: the SLOT (0..254) of the glyph
  format 2  KAT_PageSync0..M/2-1   one symbol per PAIR of slots: pa * 15 + pb   (pages 0..14)      12 bits/slot
  format 3  KAT_PageSync0..M-1     one symbol per slot: the page number itself   (pages 0..253)    16 bits/slot
  A wide glyph uses two slots (24 / 32 bits) but is a whole square hanzi.

  The format is NOT negotiated on the wire. Both sides must be built for the same one; katosc.py verifies
  this against the avatar's parameter layout and refuses to send if they disagree (see KatFormatMismatch).

SYMBOLS
  A synced float carries 8 bits but only 255 distinct values (-127..127)/127.
  symbol s (0..254) -> k = s if s <= 127 else s - 255 ; float = k / 127
  shader: k = round(p * 127) ; s = k < 0 ? k + 255 : k
  (the existing KAT animation clips already turn the float into k = 127 * p in material._CharN)
"""

import json
import math
import unicodedata

from kat_emoji import ALIAS_RE, SEQUENCE_BASE, VS16

from kat_protocol import (          # noqa: F401  (re-exported: existing code imports these from kat_codec)
    FOLLOW,
    MAX_WIDE,                       # format-2 capacity; use max_wide(fmt) for the selected format
    NARROW_CELLS,
    PAGE_BASE,                      # FORMAT 2 only: the base-15 pair packing
    PAGE_FORMAT,
    PAGE_FORMAT_DEFAULT,
    SLOTS_PER_PAGE,
    SYMBOLS,
    PageFormatError,
    bits_per_slot,
    check_format,
    max_page,
    max_wide,
    page_param_count,               # accepts an optional format; active default is format 2
    wide_pages,
)

WIDE_START = SLOTS_PER_PAGE          # first wide glyph index (page 1, slot 0)
BLANK = 0


class GlyphRangeError(ValueError):
    """A glyph index the selected page format cannot represent (raised instead of clamping / overflowing)."""


class KatFormatMismatch(PageFormatError):
    """The page format the client encodes for is not the one the avatar was built for."""


def max_glyph_index(page_format: int = PAGE_FORMAT_DEFAULT) -> int:
    return (max_page(page_format) + 1) * SLOTS_PER_PAGE - 1


def required_page_format(wide_count: int) -> int | None:
    """Smallest page format whose capacity holds ``wide_count`` wide glyphs (None = too many for any format)."""
    for fmt in (2, 3):
        if wide_count <= max_wide(fmt):
            return fmt
    return None


def read_charset_format(d: dict) -> int:
    """Page format of a charset.json / atlas_meta.json dict.

    ``page_format`` is the canonical key. ``format`` is accepted as an alias and both may be present when they
    agree. If neither key is present, the charset uses the active default, format 2. If both keys disagree, the
    file is rejected because the page layout must be unambiguous.
    """
    vals = {k: d[k] for k in ("page_format", "format") if k in d}
    for k, v in vals.items():
        if isinstance(v, bool) or not isinstance(v, int):
            raise PageFormatError(f"charset field {k!r} must be an integer page format, got {v!r}")
        check_format(v)
    if len(set(vals.values())) > 1:
        raise PageFormatError(f"charset fields disagree about the page format: {vals}. Keep one, or make them equal.")
    return next(iter(vals.values()), PAGE_FORMAT_DEFAULT)


def symbol_to_float(s: int) -> float:
    if not 0 <= s < SYMBOLS:
        raise ValueError(f"symbol {s} out of range 0..{SYMBOLS - 1}")
    k = s if s <= 127 else s - SYMBOLS
    return k / 127.0


def float_to_symbol(v: float) -> int:
    k = int(round(v * 127.0))
    return k if k >= 0 else k + SYMBOLS


def page_slot(glyph_index: int) -> tuple[int, int]:
    return divmod(glyph_index, SLOTS_PER_PAGE)


def encode_group(glyph_indices: list[int], page_format: int = PAGE_FORMAT_DEFAULT) -> tuple[list[float], list[float]]:
    """M glyph indices -> (M char floats, page floats).

    format 2: ceil(M/2) page floats, ``pa * 15 + pb`` per pair of slots (an odd tail pairs with page 0).
    format 3: M page floats, one page number per slot.
    Out-of-range glyph indices raise GlyphRangeError naming the format and the index; nothing is clamped.
    """
    check_format(page_format)
    top = max_page(page_format)
    slots, pages = [], []
    for g in glyph_indices:
        if g < 0:
            raise GlyphRangeError(f"format {page_format}: glyph index {g} is negative")
        page, slot = page_slot(g)
        if page > top:
            hint = " (this charset needs page format 3)" if page_format == 2 and page <= max_page(3) else ""
            raise GlyphRangeError(
                f"format {page_format}: glyph index {g} -> page {page}, but pages must be 0..{top}"
                f" (max glyph index {max_glyph_index(page_format)}){hint}"
            )
        slots.append(slot)
        pages.append(page)
    char_floats = [symbol_to_float(s) for s in slots]
    if page_format == 3:
        return char_floats, [symbol_to_float(p) for p in pages]
    page_floats = []
    for j in range(0, len(pages), 2):
        pb = pages[j + 1] if j + 1 < len(pages) else 0
        page_floats.append(symbol_to_float(pages[j] * PAGE_BASE + pb))
    return char_floats, page_floats


def decode_group(char_floats: list[float], page_floats: list[float], page_format: int = PAGE_FORMAT_DEFAULT) -> list[int]:
    """Reference decoder: what animator + shader must end up computing.

    Strict on purpose: too few page floats, or (format 2) a pair symbol >= 15*15 -- which no format-2 encoder can
    produce, and which is what format-3 page data looks like when misread -- raise instead of decoding garbage.
    """
    check_format(page_format)
    need = page_param_count(len(char_floats), page_format)
    if len(page_floats) < need:
        raise ValueError(f"format {page_format}: {len(char_floats)} chars need {need} page floats, got {len(page_floats)}")
    out = []
    for i, c in enumerate(char_floats):
        if page_format == 3:
            page = float_to_symbol(page_floats[i])
            if page > max_page(3):
                raise GlyphRangeError(f"format 3: page symbol {page} > {max_page(3)}")
        else:
            pair = float_to_symbol(page_floats[i // 2])
            if pair >= PAGE_BASE * PAGE_BASE:
                raise GlyphRangeError(
                    f"format 2: page-pair symbol {pair} >= {PAGE_BASE * PAGE_BASE}; not a valid format-2 pair "
                    "(was this data encoded using the wrong page format?)")
            page = pair // PAGE_BASE if i % 2 == 0 else pair % PAGE_BASE
        out.append(page * SLOTS_PER_PAGE + float_to_symbol(c))
    return out


class Charset:
    def __init__(self, narrow: str, wide: str, fallback: str = "?", page_format: int = PAGE_FORMAT_DEFAULT,
                 color: list[str] | None = None, emoji_aliases: dict[str, str] | None = None,
                 color_sources: dict[str, str] | None = None, emoji_synonyms: dict[str, str] | None = None):
        check_format(page_format)
        self.page_format = page_format
        if not narrow or narrow[0] != " ":
            raise ValueError("narrow[0] must be a space (blank slot)")
        if len(narrow) > NARROW_CELLS:
            raise ValueError(f"{len(narrow)} narrow glyphs > {NARROW_CELLS} cells")
        if len(wide) > max_wide(page_format):
            need = required_page_format(len(wide))
            hint = f"; that many needs page format {need}" if need else "; too many for any page format"
            raise ValueError(f"format {page_format}: {len(wide)} wide glyphs > {max_wide(page_format)} "
                             f"({wide_pages(page_format)} pages x {SLOTS_PER_PAGE}){hint}")
        if len(set(narrow + wide)) != len(narrow) + len(wide):
            raise ValueError("duplicate glyphs in charset")
        self.narrow, self.wide = narrow, wide
        self._index = {c: i for i, c in enumerate(narrow)}
        self._index.update({c: WIDE_START + j for j, c in enumerate(wide)})
        self._cache: dict[str, int] = {}
        self.fallback_index = self._index.get(fallback, BLANK)
        self._init_color(list(color or []), dict(emoji_aliases or {}), dict(color_sources or {}), dict(emoji_synonyms or {}))

    # ------------------------------------------------------------------ emoji

    def _init_color(self, color: list[str], aliases: dict[str, str], sources: dict[str, str],
                    synonyms: dict[str, str] | None = None) -> None:
        text_pages = math.ceil(len(self.wide) / SLOTS_PER_PAGE)
        color_pages = math.ceil(len(color) / SLOTS_PER_PAGE)
        if text_pages + color_pages > wide_pages(self.page_format):
            raise ValueError(
                f"format {self.page_format}: {len(self.wide)} wide glyphs need {text_pages} page(s) and {len(color)} emoji "
                f"need {color_pages}, but there are only {wide_pages(self.page_format)} wide pages. Emoji pages use up page "
                "numbers too: use fewer wide glyphs, or switch to page format 3.")
        if any(not isinstance(k, str) or not k for k in color):
            raise ValueError("emoji keys must be non-empty strings")
        if len(set(color)) != len(color):
            raise ValueError("the same emoji is listed twice in the charset")
        clash = [k for k in color if k in self._index]
        if clash:
            raise ValueError(f"{''.join(clash[:10])!r} is both an emoji and a normal text glyph in this charset")
        for k in sources:
            if k not in color:
                raise ValueError(f"color_sources has an entry for {k!r}, which isn't one of the emoji")
        self.color = color
        self.color_sources = sources
        self.color_first_page = 1 + text_pages
        self.color_page_count = color_pages
        base = self.color_first_page * SLOTS_PER_PAGE
        # An emoji made of several code points can't be one Python character, so the client swaps each one for a stand-in
        # character before laying text out. Those stand-ins only exist at runtime; they never go in charset.json.
        self._seq_map: dict[str, str] = {}
        for j, key in enumerate(color):
            if len(key) == 1:
                self._index[key] = base + j
            else:
                ph = chr(SEQUENCE_BASE + j)
                self._seq_map[key] = ph
                self._index[ph] = base + j
                self._index[key] = base + j        # so index_of() also works with the whole sequence
        self._seq_lens = sorted({len(k) for k in self._seq_map}, reverse=True)
        self.emoji_aliases: dict[str, str] = {}
        for name, key in aliases.items():
            if not ALIAS_RE.fullmatch(name):
                raise ValueError(f"{name!r} isn't a valid shortcode (use 1-32 of a-z 0-9 _ -, lower case)")
            if key not in color:
                raise ValueError(f":{name}: points at {key!r}, which isn't one of the emoji")
            self.emoji_aliases[name] = key
        self._max_alias = max((len(n) for n in self.emoji_aliases), default=0)
        # An emoji synonym is another emoji that means an existing glyph (two identical pictures in --emoji-dir). It is swapped for
        # that glyph's character when text is laid out, exactly like a multi-code-point emoji is.
        self.emoji_synonyms: dict[str, str] = {}
        self._syn_single: dict[str, str] = {}
        for syn, key in (synonyms or {}).items():
            if not syn or not isinstance(syn, str) or syn[0].isascii():
                raise ValueError(f"emoji synonym {syn!r} must be an emoji")
            if key not in color:
                raise ValueError(f"emoji synonym {syn!r} points at {key!r}, which isn't one of the emoji")
            if syn in color or syn in self._index:
                raise ValueError(f"emoji synonym {syn!r} is already a glyph of its own")
            self.emoji_synonyms[syn] = key
            ph = self._glyph_char(key)
            if len(syn) == 1:
                self._syn_single[syn] = ph
            else:
                self._seq_map[syn] = ph
            self._index[syn] = self._index[ph]
        self._seq_lens = sorted({len(k) for k in self._seq_map}, reverse=True)
        self.has_color = bool(color)

    def _glyph_char(self, key: str) -> str:
        """The one character the layout code uses for this emoji."""
        return key if len(key) == 1 else self._seq_map[key]

    def expand_emoji(self, text: str) -> str:
        """Swaps ``:shortcode:`` and multi-code-point emoji for one character each, so layout can keep treating every glyph as
        a single character. Shortcodes we don't know are left alone, and a variation selector right after an emoji is dropped.
        This is only for laying out and measuring; the text the person is editing never changes."""
        if not self.has_color:
            return text
        out: list[str] = []
        i, n = 0, len(text)
        color_single = {k for k in self.color if len(k) == 1}
        while i < n:
            c = text[i]
            if c == ":" and self._max_alias:
                j = text.find(":", i + 1, i + 2 + self._max_alias)
                if j > i + 1:
                    key = self.emoji_aliases.get(text[i + 1:j].lower())
                    if key is not None:
                        out.append(self._glyph_char(key))
                        i = j + 1
                        continue
            if c in self._syn_single:
                out.append(self._syn_single[c])
                i += 1
                if i < n and text[i] == VS16:
                    i += 1
                continue
            if self._seq_lens and ord(c) > 0x7F:
                hit = None
                for length in self._seq_lens:           # longest match first
                    ph = self._seq_map.get(text[i:i + length])
                    if ph is not None:
                        hit = (ph, length)
                        break
                if hit:
                    out.append(hit[0])
                    i += hit[1]
                    continue
            out.append(c)
            i += 1
            if c in color_single and i < n and text[i] == VS16:
                i += 1
        return "".join(out)

    def is_color(self, glyph_index: int) -> bool:
        lo = self.color_first_page * SLOTS_PER_PAGE
        return self.has_color and lo <= glyph_index < lo + len(self.color)

    @classmethod
    def load(cls, path: str, fallback: str = "?", page_format: int | None = None) -> "Charset":
        """The page format comes from the file (``page_format`` or ``format``; missing means format 2).
        Passing ``page_format`` asserts the expected value; a mismatch raises PageFormatError."""
        with open(path, encoding="utf-8") as f:
            d = json.load(f)
        fmt = read_charset_format(d)
        if page_format is not None and page_format != fmt:
            raise PageFormatError(f"{path} is a format-{fmt} charset but format {page_format} was requested")
        return cls(d["narrow"], d["wide"], fallback, page_format=fmt, color=d.get("color"),
                   emoji_aliases=d.get("emoji_aliases"), color_sources=d.get("color_sources"),
                   emoji_synonyms=d.get("emoji_synonyms"))

    def to_json_dict(self) -> dict:
        """Return the canonical charset JSON representation with an explicit page format."""
        d = {
            "format": self.page_format,
            "page_format": self.page_format,
            "narrow": self.narrow,
            "wide": self.wide,
            "pages": self.pages,
        }
        if self.has_color:                      # only written when there are emoji, so normal charsets look exactly as before
            d["color"] = self.color
            d["emoji_aliases"] = self.emoji_aliases
            if self.emoji_synonyms:
                d["emoji_synonyms"] = self.emoji_synonyms
            if self.color_sources:
                d["color_sources"] = self.color_sources
        return d

    @property
    def sdf_pages(self) -> int:
        """How many pages are SDF: page 0 plus the wide text pages."""
        return 1 + math.ceil(len(self.wide) / SLOTS_PER_PAGE)

    @property
    def pages(self) -> int:
        """Every page number in use: the SDF pages, then the emoji pages."""
        return self.sdf_pages + self.color_page_count

    @staticmethod
    def is_wide(glyph_index: int) -> bool:
        return glyph_index >= WIDE_START

    def index_of(self, ch: str) -> int:
        """Exact match, else NFKC fold to a single char (fullwidth '１' -> '1'), else fallback."""
        hit = self._cache.get(ch)
        if hit is not None:
            return hit
        i = self._index.get(ch)
        if i is None:
            folded = unicodedata.normalize("NFKC", ch)
            i = self._index.get(folded) if len(folded) == 1 else None
        if i is None:
            i = self.fallback_index
        self._cache[ch] = i
        return i

    def contains(self, ch: str) -> bool:
        """True if ``ch`` (exactly, or after the same NFKC fold ``index_of`` applies) has a glyph in this charset."""
        if ch in self._index:
            return True
        folded = unicodedata.normalize("NFKC", ch)
        return len(folded) == 1 and folded in self._index

    def char_at(self, glyph_index: int) -> str:
        if 0 <= glyph_index < len(self.narrow):
            return self.narrow[glyph_index]
        j = glyph_index - WIDE_START
        if 0 <= j < len(self.wide):
            return self.wide[j]
        k = glyph_index - self.color_first_page * SLOTS_PER_PAGE
        if self.has_color and 0 <= k < len(self.color):
            return self.color[k]
        return ""      # FOLLOW marker or unassigned cell
