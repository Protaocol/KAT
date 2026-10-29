"""Validate that Python protocol.json and the shipped Unity/shader artifacts agree.

Checks (every one fails the run, nothing is advisory):
  * KAT_Settings.cs constants (TextLength, sync range/default, PageFormat2/3, MaxPage2/3) vs protocol.json
  * the paged shader: _Chars[128], BOTH _Pages[64] (format 2) and _Pages[128] (format 3) blocks, the KAT_PAGE_FORMAT_3 keyword,
    the _Page0.._Page{N-1} properties, and the packoffset of every _Char/_Page register (format 3 must only APPEND registers,
    so format 2 never shifts)
  * tools/gen_shader.py --check (the committed shader is what the template generates)
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parent
PROTOCOL = ROOT / "protocol.json"
UNITY = ROOT / "KAT_unity" / "KAT"
SETTINGS = UNITY / "Scripts" / "KAT_Settings.cs"
SHADER = UNITY / "Shaders" / "KF_AvatarTextShaderPaged.shader"
GEN_SHADER = UNITY / "tools" / "gen_shader.py"

KEYWORD = "KAT_PAGE_FORMAT_3"


def load_gen_shader():
    """Import the shader generator next to the shader template (it is not a package module)."""
    import importlib.util

    spec = importlib.util.spec_from_file_location("gen_shader", GEN_SHADER)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _fail(message: str) -> "SystemExit":
    return SystemExit(f"Protocol validation FAILED: {message}")


def _cs_int(settings: str, name: str) -> int | None:
    match = re.search(rf"\bpublic\s+(?:const|readonly)\s+int\s+{name}\s*=\s*(-?\d+)\s*;", settings)
    return int(match.group(1)) if match else None


def _shader_blocks(shader: str) -> tuple[str, str]:
    match = re.search(
        rf"#ifdef {KEYWORD}\s*\n\s*float4 _PagePack\[(\d+)\]\s*:\s*packoffset\(c(\d+)\);\s*\n\s*#else\s*\n"
        rf"\s*float4 _PagePack\[(\d+)\]\s*:\s*packoffset\(c(\d+)\);\s*\n\s*#endif",
        shader,
    )
    if not match:
        raise _fail(f"shader has no `#ifdef {KEYWORD} / #else / #endif` around the _PagePack[] declaration")
    return (match.group(1), match.group(2)), (match.group(3), match.group(4))  # type: ignore[return-value]


def main() -> int:
    protocol = json.loads(PROTOCOL.read_text(encoding="utf-8"))
    settings = SETTINGS.read_text(encoding="utf-8")
    shader = SHADER.read_text(encoding="utf-8")

    text_length = int(protocol["text_length"])
    formats = protocol["page_formats"]
    fmt2, fmt3 = formats["2"], formats["3"]

    if int(protocol["page_format"]) != 2:
        raise _fail("protocol.json page_format must stay 2 (format 3 is opt-in, format 2 is the default)")
    if int(fmt2["pages_per_float"]) != 2 or int(fmt3["pages_per_float"]) != 1:
        raise _fail("protocol.json page_formats pages_per_float must be 2 (format 2) and 1 (format 3)")

    # --- KAT_Settings.cs ---------------------------------------------------------------------------------------------
    expected = {
        "TextLength": text_length,
        "SyncParamsMin": protocol["sync_params_min"],
        "SyncParamsMax": protocol["sync_params_max"],
        "SyncParamsDefault": protocol["sync_params_default"],
        "PageFormat2": 2,
        "PageFormat3": 3,
        "MaxPage2": fmt2["max_page"],
        "MaxPage3": fmt3["max_page"],
    }
    for name, value in expected.items():
        actual = _cs_int(settings, name)
        if actual is None:
            raise _fail(f"KAT_Settings.cs does not define `{name}` (expected {value})")
        if actual != int(value):
            raise _fail(f"KAT_Settings.cs {name} = {actual}, protocol.json says {value}")

    # --- shader ------------------------------------------------------------------------------------------------------
    if f"#pragma shader_feature_local _ {KEYWORD}" not in shader:
        raise _fail(f"shader is missing `#pragma shader_feature_local _ {KEYWORD}`")

    char_match = re.search(r"float4 _CharPack\[(\d+)\]", shader)
    if not char_match or int(char_match.group(1)) != text_length // 4:
        raise _fail("shader _CharPack size does not match protocol.json text_length")

    (p3_size, p3_off), (p2_size, p2_off) = _shader_blocks(shader)
    if int(p2_size) != text_length // 8:                     # 64 page floats, 4 per float4 register
        raise _fail(f"shader format-2 _PagePack[{p2_size}] != text_length/8 = {text_length // 8}")
    if int(p3_size) != text_length // 4:
        raise _fail(f"shader format-3 _PagePack[{p3_size}] != text_length/4 = {text_length // 4}")
    if int(p2_off) != text_length // 4 or int(p3_off) != text_length // 4:
        raise _fail(f"_PagePack[] must start at register c{text_length // 4} in both formats (got c{p2_off} / c{p3_off})")

    # Per-register aliases. _Char<i> at c<i//4>.<lane>; _Page<j> at c<text_length//4 + j//4>.<lane>;
    # j >= text_length/2 only inside the keyword block.
    LANES = "xyzw"
    for i in range(text_length):
        if not re.search(rf"float _Char{i} : packoffset\(c{i // 4}\.{LANES[i % 4]}\);", shader):
            raise _fail(f"shader _Char{i} is not declared at packoffset(c{i // 4}.{LANES[i % 4]})")
    for j in range(text_length):
        pattern = rf"float _Page{j} : packoffset\(c{text_length // 4 + j // 4}\.{LANES[j % 4]}\);"
        if len(re.findall(pattern, shader)) != 1:
            raise _fail(f"shader must declare _Page{j} exactly once at packoffset(c{text_length // 4 + j // 4}.{LANES[j % 4]})")
        if not re.search(rf"\[HideInInspector\] _Page{j}\(", shader):
            raise _fail(f"shader Properties block has no _Page{j}")
    if re.search(rf"_Page{text_length}\b", shader):
        raise _fail(f"shader mentions _Page{text_length}, which is out of range")

    # Keyword scoping: the declarations for _Page{text_length//2}.._Page{text_length-1} must sit inside one #ifdef block.
    tail = re.search(
        rf"#ifdef {KEYWORD}\s*\n((?:\s*float _Page\d+ : packoffset\(c\d+\.[xyzw]\);\s*\n)+)\s*#endif", shader
    )
    if not tail:
        raise _fail(f"shader format-3-only _Page declarations are not wrapped in `#ifdef {KEYWORD}`")
    tail_indexes = [int(n) for n in re.findall(r"float _Page(\d+) :", tail.group(1))]
    if tail_indexes != list(range(text_length // 2, text_length)):
        raise _fail("shader format-3-only block must declare exactly _Page64.._Page127")

    if "ReadSlot" not in shader or f"page = DecodeSymbol(ReadPageValue(slot));" not in shader:
        raise _fail("shader ReadSlot has no format-3 branch (page = DecodeSymbol(ReadPageValue(slot)))")
    if "pair % 15u" not in shader or "pair / 15u" not in shader:
        raise _fail("shader ReadSlot lost its format-2 (base-15 pair) branch")

    # --- generator ---------------------------------------------------------------------------------------------------
    if not load_gen_shader().check():
        raise _fail("gen_shader.py --check: committed shader does not match the template")

    print("Protocol validation passed")
    print(f"  text slots: {text_length}")
    print(f"  row width: {protocol['line_length']}")
    print(f"  sync range: {protocol['sync_params_min']}..{protocol['sync_params_max']} (even)")
    print(f"  page formats: 2 (pages 0..{fmt2['max_page']}, {text_length // 2} page params), "
          f"3 (pages 0..{fmt3['max_page']}, {text_length} page params)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
