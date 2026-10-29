#!/usr/bin/env python3
"""
Run static consistency checks across protocol, charset and atlas artifacts.

The checker treats the explicitly supplied charset and atlas paths as authoritative. Additional similarly named files
elsewhere are not selected automatically, which prevents a directory containing multiple builds from changing the
active data by accident.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def run_protocol_validation() -> None:
    import validate_protocol

    validate_protocol.main()


def load_charset(path: Path) -> tuple[dict, int, int]:
    d = json.loads(path.read_text(encoding="utf-8"))
    from kat_codec import Charset, read_charset_format
    fmt = read_charset_format(d)
    cs = Charset(d["narrow"], d["wide"], page_format=fmt, color=d.get("color"), emoji_aliases=d.get("emoji_aliases"),
                 color_sources=d.get("color_sources"), emoji_synonyms=d.get("emoji_synonyms"))
    return d, fmt, cs


def check_color_pages(atlas: Path, expected: int) -> list[str]:
    """Checks the emoji pages: color_page_<n>.png, RGBA, numbered from 0 without gaps, all the same square size."""
    problems: list[str] = []
    found = []
    for p in atlas.glob("color_page_*.png"):
        m = re.fullmatch(r"color_page_(\d+)\.png", p.name)
        if not m:
            problems.append(f"unexpected emoji page filename: {p.name}")
            continue
        found.append((int(m.group(1)), p))
    found.sort()
    ids = [i for i, _ in found]
    if ids != list(range(len(ids))):
        problems.append(f"emoji pages must be numbered without gaps from 0; found {ids[:12]}")
    if len(found) != expected:
        problems.append(f"the charset has emoji that need {expected} emoji page(s), but found {len(found)}")
    size = None
    for idx, p in found:
        try:
            with Image.open(p) as im:
                if im.mode != "RGBA":
                    problems.append(f"emoji page {idx} is {im.mode}; expected RGBA")
                if im.width != im.height or im.width % 16:
                    problems.append(f"emoji page {idx} is {im.size}; expected a square size divisible by 16")
                if size is None:
                    size = im.size
                elif im.size != size:
                    problems.append(f"emoji page {idx} size {im.size} differs from {size}")
        except Exception as exc:
            problems.append(f"cannot read {p.name}: {exc}")
    return problems


def check_atlas(atlas: Path, charset_hash: str, fmt: int, expected_pages: int, expected_color_pages: int = 0) -> list[str]:
    """`expected_pages` counts the SDF pages only (page 0 plus the wide text pages); emoji pages are checked on their own."""
    problems: list[str] = []
    problems += check_color_pages(atlas, expected_color_pages) if (expected_color_pages or any(atlas.glob("color_page_*.png"))) else []
    if not atlas.is_dir():
        return [f"atlas directory does not exist: {atlas}"]
    pngs = []
    for p in atlas.glob("page_*.png"):
        m = re.fullmatch(r"page_(\d+)\.png", p.name)
        if not m:
            problems.append(f"unexpected page filename: {p.name}")
            continue
        pngs.append((int(m.group(1)), p))
    pngs.sort()
    if not pngs:
        problems.append("atlas contains no page_*.png files")
    else:
        ids = [i for i, _ in pngs]
        if ids != list(range(len(ids))):
            problems.append(f"atlas pages must be contiguous from 0; found {ids[:12]}{'...' if len(ids) > 12 else ''}")
        if len(pngs) != expected_pages:
            problems.append(f"charset needs {expected_pages} atlas pages, found {len(pngs)}")
        size = None
        mode = None
        for idx, p in pngs:
            try:
                with Image.open(p) as im:
                    if size is None:
                        size, mode = im.size, im.mode
                    elif im.size != size:
                        problems.append(f"page {idx} size {im.size} differs from {size}")
                    if im.mode != mode:
                        problems.append(f"page {idx} mode {im.mode} differs from {mode}")
                    if im.mode not in ("L", "I;16"):
                        problems.append(f"page {idx} is {im.mode}; expected a single-channel image")
            except Exception as exc:
                problems.append(f"cannot read {p.name}: {exc}")

    meta_path = atlas / "atlas_meta.json"
    if not meta_path.exists():
        problems.append("atlas_meta.json is missing")
    else:
        try:
            meta = json.loads(meta_path.read_text(encoding="utf-8"))
            meta_fmt = int(meta.get("page_format", meta.get("format", 2)))
            if meta_fmt != fmt:
                problems.append(f"atlas metadata format {meta_fmt} disagrees with charset format {fmt}")
            if int(meta.get("pages", -1)) != expected_pages + expected_color_pages:
                problems.append(f"atlas metadata pages={meta.get('pages')!r} disagrees with charset pages={expected_pages + expected_color_pages}")
            if expected_color_pages and int(meta.get("color_page_start", -1)) != expected_pages:
                problems.append(f"atlas metadata color_page_start={meta.get('color_page_start')!r}; the charset puts the emoji pages at page {expected_pages}")
            stored_hash = meta.get("charset_sha256")
            if stored_hash and str(stored_hash).lower() != charset_hash.lower():
                problems.append("atlas metadata charset_sha256 does not match the supplied charset")
        except Exception as exc:
            problems.append(f"cannot read atlas_meta.json: {exc}")
    return problems


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--charset", type=Path, default=Path("charset.json"))
    ap.add_argument("--atlas", type=Path, default=Path("atlas"))
    ap.add_argument("--skip-protocol", action="store_true")
    args = ap.parse_args()

    return check(args.charset, args.atlas, skip_protocol=args.skip_protocol)


def check(charset, atlas, skip_protocol: bool = False) -> int:
    charset = Path(charset).resolve()
    atlas = Path(atlas).resolve()
    if not charset.is_file():
        print(f"ERROR: charset file does not exist: {charset}", file=sys.stderr)
        return 2
    if not skip_protocol:
        run_protocol_validation()

    try:
        _, fmt, cs = load_charset(charset)
        pages = cs.sdf_pages
        digest = sha256(charset)
    except Exception as exc:
        print(f"ERROR: charset validation failed: {exc}", file=sys.stderr)
        return 2

    problems = check_atlas(atlas, digest, fmt, pages, cs.color_page_count)
    if problems:
        for problem in problems:
            print("ERROR:", problem, file=sys.stderr)
        return 1

    extra = f" + {cs.color_page_count} emoji page(s) from page {cs.color_first_page}" if cs.has_color else ""
    print(f"OK: charset={charset.name} format={fmt} pages={pages}{extra} sha256={digest[:16]}…")
    print(f"OK: atlas={atlas} pages={pages}{extra} metadata/hash match")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
