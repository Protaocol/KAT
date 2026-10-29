"""Load the machine-readable KAT protocol definition used by the Python side.

CONSTANT CLASSIFICATION (so nobody has to re-derive it)

  protocol invariants, the same in every page format
      SYMBOLS (255), SLOTS_PER_PAGE (255), FOLLOW (254), POINTER_CLEAR (255), NARROW_CELLS (128),
      TEXT_LENGTH / LINE_LENGTH / LINE_COUNT, the SYNC_PARAMS_* range
  page-format dependent (use the helpers below, never a bare constant)
      max page number, number of wide pages, max wide glyphs, page floats per sync group, wire bits per slot
      -> max_page(fmt), wide_pages(fmt), max_wide(fmt), page_param_count(sync, fmt), bits_per_slot(fmt)
  format-2-only artifacts
      PAGE_BASE (15): the base-15 pair packing ``pa * 15 + pb``. Format 3 has no packing base at all.
  format-2 capacity constants
      WIDE_PAGES (14) and MAX_WIDE (3570) describe the format-2 capacity.

PAGE FORMATS
  2  (default)  two page numbers share one synced float: ``pa * 15 + pb``.  pages 0..14, 12 bits per slot.
  3  (opt-in)   one page number per synced float.                            pages 0..253, 16 bits per slot.
  A charset/avatar uses one page format for its whole runtime configuration.
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any


_PROTOCOL_PATH = Path(__file__).with_name("protocol.json")


def _load() -> dict[str, Any]:
    with _PROTOCOL_PATH.open(encoding="utf-8") as f:
        return json.load(f)


_PROTOCOL = _load()

TEXT_LENGTH = int(_PROTOCOL["text_length"])
LINE_LENGTH = int(_PROTOCOL["line_length"])
LINE_COUNT = int(_PROTOCOL["line_count"])
SYNC_PARAMS_DEFAULT = int(_PROTOCOL["sync_params_default"])
SYNC_PARAMS_MIN = int(_PROTOCOL["sync_params_min"])
SYNC_PARAMS_MAX = int(_PROTOCOL["sync_params_max"])
SYNC_PARAMS_ALLOWED_EVEN = tuple(int(x) for x in _PROTOCOL["sync_params_allowed_even"])
SYMBOLS = int(_PROTOCOL["symbols"])
SLOTS_PER_PAGE = int(_PROTOCOL["slots_per_page"])
PAGE_BASE = int(_PROTOCOL["page_base"])
NARROW_CELLS = int(_PROTOCOL["narrow_cells"])
WIDE_PAGES = int(_PROTOCOL["wide_pages"])
FOLLOW = int(_PROTOCOL["follow_symbol"])
POINTER_CLEAR = int(_PROTOCOL["pointer_clear"])
MAX_WIDE = WIDE_PAGES * SLOTS_PER_PAGE          # FORMAT 2 capacity; see max_wide(fmt) for the general form

PAGE_FORMAT = int(_PROTOCOL["page_format"])     # the active project default format
PAGE_FORMATS = {
    int(k): {
        "pages_per_float": int(v["pages_per_float"]),
        "max_page": int(v["max_page"]),
        "bits_per_slot": int(v["bits_per_slot"]),
    }
    for k, v in _PROTOCOL["page_formats"].items()
}
PAGE_FORMAT_DEFAULT = 2      # format used when a charset/atlas/install does not pass an explicit format.


class PageFormatError(ValueError):
    """A page format is unknown, or two components disagree about which one is in use."""


def check_format(fmt: int) -> int:
    if isinstance(fmt, bool) or fmt not in PAGE_FORMATS:
        raise PageFormatError(f"unknown page format {fmt!r}; known formats: {sorted(PAGE_FORMATS)}")
    return fmt


def max_page(fmt: int) -> int:
    """Highest page number the format can address (page 0 = narrow)."""
    return PAGE_FORMATS[check_format(fmt)]["max_page"]


def wide_pages(fmt: int) -> int:
    """Number of wide pages (pages 1..max_page)."""
    return max_page(fmt)


def max_wide(fmt: int) -> int:
    """Wide glyph capacity: wide pages x SLOTS_PER_PAGE."""
    return wide_pages(fmt) * SLOTS_PER_PAGE


def pages_per_float(fmt: int) -> int:
    return PAGE_FORMATS[check_format(fmt)]["pages_per_float"]


def bits_per_slot(fmt: int) -> int:
    return PAGE_FORMATS[check_format(fmt)]["bits_per_slot"]


def page_param_count(sync_params: int, fmt: int = PAGE_FORMAT_DEFAULT) -> int:
    """KAT_PageSync floats per sync group of ``sync_params`` slots (format 2: ceil(M/2), format 3: M)."""
    per = pages_per_float(fmt)
    return -(-int(sync_params) // per)


def infer_format_from_param_counts(sync_params: int, page_params: int) -> int | None:
    """Which page format produces exactly ``page_params`` PageSync floats for ``sync_params`` CharSync floats?
    None if neither does. Distinguishable for every legal sync size because ceil(M/2) < M for M >= 2."""
    for fmt in sorted(PAGE_FORMATS):
        if page_param_count(sync_params, fmt) == page_params:
            return fmt
    return None


def validate() -> None:
    if SYNC_PARAMS_DEFAULT not in SYNC_PARAMS_ALLOWED_EVEN:
        raise ValueError("sync_params_default must be one of sync_params_allowed_even")
    if SYNC_PARAMS_MIN % 2 or SYNC_PARAMS_MAX % 2:
        raise ValueError("sync_params_min/max must be even")
    if any(v < SYNC_PARAMS_MIN or v > SYNC_PARAMS_MAX or v % 2 for v in SYNC_PARAMS_ALLOWED_EVEN):
        raise ValueError("sync_params_allowed_even contains an invalid value")
    if TEXT_LENGTH != LINE_LENGTH * LINE_COUNT:
        raise ValueError("text_length must equal line_length * line_count")
    if PAGE_BASE <= 0 or SYMBOLS <= 0 or SLOTS_PER_PAGE <= 0:
        raise ValueError("protocol dimensions must be positive")
    if not 0 <= FOLLOW < SLOTS_PER_PAGE:
        raise ValueError("follow_symbol must be a valid slot symbol")
    if POINTER_CLEAR != SYMBOLS:
        raise ValueError("pointer_clear is expected to be the reserved 255 clear value")
    if PAGE_FORMAT not in PAGE_FORMATS:
        raise ValueError("page_format must be one of page_formats")
    if PAGE_FORMAT != PAGE_FORMAT_DEFAULT:
        raise ValueError("the active project default page_format must be 2")
    for fmt, f in PAGE_FORMATS.items():
        if not 1 <= f["max_page"] < SYMBOLS:
            raise ValueError(f"format {fmt}: max_page must be a page symbol 1..{SYMBOLS - 1}")
    f2 = PAGE_FORMATS[2]
    if f2["pages_per_float"] != 2 or f2["max_page"] != PAGE_BASE - 1 or WIDE_PAGES != f2["max_page"]:
        raise ValueError("format 2 is frozen: pages_per_float 2, max_page == page_base - 1 == wide_pages")
    if PAGE_BASE * PAGE_BASE > SYMBOLS:
        raise ValueError("format 2 needs page_base ** 2 <= symbols")
    if PAGE_FORMATS[3]["pages_per_float"] != 1:
        raise ValueError("format 3 is one page per synced float")
    for fmt, f in PAGE_FORMATS.items():
        if f["bits_per_slot"] != 8 + 8 // f["pages_per_float"]:      # 8 char bits + this slot's share of a page float
            raise ValueError(f"format {fmt}: bits_per_slot inconsistent with pages_per_float")


validate()
