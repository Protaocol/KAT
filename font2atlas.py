#!/usr/bin/env python3
"""Quickstart: build charset.json and an SDF atlas from a font.

Typical use:
    python font2atlas.py NotoSansCJKsc-Regular.otf
    python font2atlas.py yourfont.ttf --protocol 3

This file is designed to be a quick installation, so most values are hardcoded for common usecase.
If you need more control over the build, follow the advanced instructions in the README to use the individual tools directly.

"""
from __future__ import annotations

import hashlib
import json
import os
import sys
import tempfile
from pathlib import Path

import argparse
import build_charset
from kat_codec import max_wide


def default_wide_count(page_format: int) -> int:
    """Number of ranked glyphs to add after reserving the built-in punctuation.

    The default ranked list contains CJK ideographs, so each selected ranked glyph
    consumes one wide slot. Subtracting the initial wide punctuation seed from the
    format capacity therefore fills the format without overflowing it.
    """
    seed_wide = len(dict.fromkeys(ch for ch in build_charset.WIDE_PUNCT if ch not in build_charset.NARROW_BASE))
    return max(0, max_wide(page_format) - seed_wide)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def confirm_replace(charset_out: Path, atlas_out: Path) -> bool:
    """Avoid silently overwriting an earlier build while keeping the CLI flag-light."""
    existing = []
    if charset_out.exists():
        existing.append(str(charset_out))
    generated_atlas = atlas_out.is_dir() and (
        any(atlas_out.glob("page_*.png")) or (atlas_out / "atlas_meta.json").exists()
    )
    if generated_atlas:
        existing.append(str(atlas_out))
    if not existing:
        return True
    print("This will replace the generated output(s): " + ", ".join(existing))
    try:
        answer = input("Continue? [y/N] ").strip().lower()
    except (EOFError, KeyboardInterrupt):
        print("Cancelled; existing output was left unchanged.", file=sys.stderr)
        return False
    if answer not in {"y", "yes"}:
        print("Cancelled; existing output was left unchanged.")
        return False
    return True


def replace_generated_atlas(staged_atlas: Path, atlas_out: Path) -> None:
    """Install a completed atlas while preserving unrelated files in atlas_out."""
    atlas_out.mkdir(parents=True, exist_ok=True)
    for stale in atlas_out.glob("page_*.png"):
        if stale.is_file():
            stale.unlink()
    meta = atlas_out / "atlas_meta.json"
    if meta.exists() and meta.is_file():
        meta.unlink()
    for generated in staged_atlas.iterdir():
        if generated.is_file() and (generated.name.startswith("page_") and generated.suffix.lower() == ".png"
                                    or generated.name == "atlas_meta.json"):
            os.replace(generated, atlas_out / generated.name)


def create_parser() -> argparse.ArgumentParser:
    """Small public CLI surface for the common font-to-atlas workflow."""
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("font", help="input .ttf/.otf font (for a specific .ttc face, use the advanced tools)")
    ap.add_argument("--protocol", type=int, choices=(2, 3), default=2,
                    help="paged format: 2 (default, 3,570 wide glyphs) or 3 (large, up to 64,515)")
    return ap


def main(argv: list[str] | None = None) -> int:
    ap = create_parser()
    args = ap.parse_args(argv)

    font = Path(args.font).expanduser().resolve()
    if not font.is_file():
        ap.error(f"font not found: {font}")

    charset_out = Path.cwd() / "charset.json"
    atlas_out = Path.cwd() / "atlas"

    # Keep --help and basic argument validation usable even before optional build dependencies are installed.
    try:
        import font2charset
        import filter_charset
        import build_atlas
        import sanity_check
    except ImportError as exc:
        missing = getattr(exc, "name", None) or str(exc)
        print(f"Missing build dependency ({missing}). Install the project requirements with:\n  python -m pip install -r requirements.txt", file=sys.stderr)
        return 2

    if not confirm_replace(charset_out, atlas_out):
        return 1

    count = default_wide_count(args.protocol)
    print(f"Font: {font}")
    print(f"Paged format: {args.protocol}")
    print(f"Wide glyph target: up to {count:,} ranked glyphs plus built-in punctuation")
    print("Output: charset.json and atlas/ (in the current directory)")

    with tempfile.TemporaryDirectory(prefix=".font2atlas-", dir=str(Path.cwd())) as work_name:
        work = Path(work_name)
        raw = work / "font_glyphs.txt"
        filtered = work / "filtered_glyphs.txt"
        staged_charset = work / "charset.json"
        staged_atlas = work / "atlas"

        print("\n[1/5] Reading glyphs from the font...")
        font2charset.extract_font_chars(str(font), str(raw), 0)

        print("\n[2/5] Filtering/ranking useful characters...")
        filter_charset.filter_glyphs(raw, filtered, filter_charset.DEFAULT_CJK_MIN_ZIPF, None, verbose=False)

        print("\n[3/5] Building charset...")
        rc = build_charset.generate(
            freq=str(filtered),
            fmt=args.protocol,
            count=count,
            punct=build_charset.WIDE_PUNCT,
            narrow=build_charset.NARROW_BASE,
            out=str(staged_charset),
        )
        if rc != 0:
            print("Charset generation failed; existing outputs were left unchanged.", file=sys.stderr)
            return rc

        print("\n[4/5] Baking the SDF atlas...")
        rc = build_atlas.render_atlas(
            str(staged_charset), str(font),
            font_index=0,
            narrow_font=None,
            narrow_font_index=0,
            cell_size=64,
            spread=6,
            supersample=4,
            strict=False,
            out=str(staged_atlas),
        )
        if rc != 0:
            print("Atlas generation failed; existing outputs were left unchanged.", file=sys.stderr)
            return rc

        meta_path = staged_atlas / "atlas_meta.json"
        meta = json.loads(meta_path.read_text(encoding="utf-8"))
        meta["charset_sha256"] = sha256(staged_charset)
        meta_path.write_text(json.dumps(meta, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")

        print("\n[5/5] Checking the generated bundle...")
        rc = sanity_check.check(staged_charset, staged_atlas)
        if rc != 0:
            print("Sanity check failed; existing outputs were left unchanged.", file=sys.stderr)
            return rc

        # Only replace final outputs after the complete staged build validates.
        charset_out.write_bytes(staged_charset.read_bytes())
        replace_generated_atlas(staged_atlas, atlas_out)

    print("\nBuild complete.")
    print("  Charset: charset.json")
    print("  Atlas:   atlas/")
    print("  Client:  python KatOscApp.pyw --charset charset.json")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
