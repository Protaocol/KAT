#!/usr/bin/env python3
"""
Synchronize generated protocol/config/shader artifacts from protocol.json.

The protocol file is the single editable source for wire dimensions and page-format defaults. This tool updates the
Unity installer constants, regenerates the paged shader from its template, refreshes the KAT config example, and runs
static protocol validation. Use --check in CI to verify that generated files already match protocol.json.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from tempfile import NamedTemporaryFile

ROOT = Path(__file__).resolve().parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

import validate_protocol
from validate_protocol import load_gen_shader

PROTOCOL_PATH = ROOT / "protocol.json"
SETTINGS_PATH = ROOT / "KAT_unity" / "KAT" / "Scripts" / "KAT_Settings.cs"
CONFIG_EXAMPLE = ROOT / "KAT_unity" / "KAT" / "KAT_Config.example.txt"

MATERIAL_PROPERTIES = (
    "_MainColor", "_ShadowColor", "_OutlineWidth", "_Weight", "_GrowEnabled", "_GrowStart", "_GrowEnd",
    "_GrowAmount", "_ExtentScanSlots", "_BoxEnabled", "_ShrinkToFit", "_BackColor", "_BorderColor",
    "_BorderWidth", "_Padding", "_CornerRadius", "_Scale", "_AnchorX", "_AnchorY", "_TailWidth",
    "_TailHeight", "_TailLean", "_TailMargin", "_TailSide",
)


def atomic_write(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with NamedTemporaryFile("w", encoding="utf-8", newline="\n", dir=path.parent, delete=False) as f:
        f.write(text)
        tmp = Path(f.name)
    tmp.replace(path)


def load_protocol() -> dict:
    with PROTOCOL_PATH.open(encoding="utf-8") as f:
        d = json.load(f)
    return d


def validate_protocol_values(p: dict) -> None:
    required = (
        "text_length", "line_length", "line_count", "sync_params_default", "sync_params_min", "sync_params_max",
        "sync_params_allowed_even", "symbols", "slots_per_page", "page_base", "page_format", "page_formats",
        "narrow_cells", "follow_symbol", "pointer_clear",
    )
    missing = [k for k in required if k not in p]
    if missing:
        raise ValueError("protocol.json is missing: " + ", ".join(missing))
    if p["text_length"] != p["line_length"] * p["line_count"]:
        raise ValueError("text_length must equal line_length * line_count")
    if str(p["page_format"]) not in p["page_formats"]:
        raise ValueError(f"page_format {p['page_format']} has no page_formats entry")
    if p["symbols"] != 255 or p["slots_per_page"] != 255 or p["page_base"] != 15:
        raise ValueError("current symbol/page packing requires symbols=255, slots_per_page=255, page_base=15")
    allowed = p["sync_params_allowed_even"]
    if allowed != sorted(set(allowed)) or any(n % 2 for n in allowed):
        raise ValueError("sync_params_allowed_even must be sorted unique even integers")
    if p["sync_params_default"] not in allowed:
        raise ValueError("sync_params_default must be one of sync_params_allowed_even")
    if not (p["sync_params_min"] <= p["sync_params_default"] <= p["sync_params_max"]):
        raise ValueError("sync_params_default must be inside sync_params_min..sync_params_max")
    for name in ("2", "3"):
        entry = p["page_formats"].get(name)
        if not isinstance(entry, dict) or "pages_per_float" not in entry or "max_page" not in entry:
            raise ValueError(f"page_formats[{name!r}] must contain pages_per_float and max_page")


def render_settings(p: dict) -> str:
    text = SETTINGS_PATH.read_text(encoding="utf-8")
    replacements = {
        r"(public readonly int TextLength = )\d+;": rf"\g<1>{p['text_length']};",
        r"(public const int SyncParamsMin = )\d+;": rf"\g<1>{p['sync_params_min']};",
        r"(public const int SyncParamsMax = )\d+;": rf"\g<1>{p['sync_params_max']};",
        r"(public const int SyncParamsDefault = )\d+;": rf"\g<1>{p['sync_params_default']};",
        r"(public const int MaxPage2 = )\d+;": rf"\g<1>{p['page_formats']['2']['max_page']};",
        r"(public const int MaxPage3 = )\d+;": rf"\g<1>{p['page_formats']['3']['max_page']};",
        r"(public readonly int PageFormat = PageFormat)\d+;": rf"\g<1>{p['page_format']};",
        r"(int pageFormat = PageFormat)\d+\)": rf"\g<1>{p['page_format']})",
    }
    for pat, repl in replacements.items():
        text, n = re.subn(pat, repl, text)
        if n != 1:
            raise ValueError(f"expected one settings match for {pat!r}, found {n}")
    # These comments describe the current implementation and intentionally stay synchronized with the values.
    text = re.sub(
        r"// Page wire formats .*?\n\s*// Format 3 .*?\n\s*// the client,\n\s*// the animator and the shader must all agree on it\.\n",
        "// Page wire formats are defined in protocol.json. Format 2 packs page pairs; format 3 stores one page per slot.\n"
        "\t\t// The selected format is shared by the charset, atlas, animator, material and shader.\n",
        text,
        count=1,
        flags=re.S,
    )
    text = re.sub(r"// 2 \(default\) or 3, see PageFormat2 / PageFormat3\.", "// Active page format selected by the installer.", text, count=1)
    text = re.sub(
        r"// Parameters, menu controls and style/head-scale layers are omitted together when this is false\.",
        "// Parameters, menu controls and style/head-scale layers are omitted together when this is false.",
        text,
        count=1,
    )
    return text

def update_settings(p: dict) -> bool:
    current = SETTINGS_PATH.read_text(encoding="utf-8")
    expected = render_settings(p)
    if expected != current:
        atomic_write(SETTINGS_PATH, expected)
        return True
    return False


def parse_shader_defaults() -> dict[str, str]:
    shader = (ROOT / "KAT_unity" / "KAT" / "Shaders" / "KF_AvatarTextShaderPaged.shader").read_text(encoding="utf-8")
    values: dict[str, str] = {}
    for m in re.finditer(r"^\s*(?:\[[^\n]+\]\s*)?([_A-Za-z][A-Za-z0-9_]*)\s*\(.*\)\s*=\s*([^\n]+)$", shader, re.M):
        values[m.group(1)] = m.group(2).strip()
    return values


def render_config_example() -> str:
    vals = parse_shader_defaults()
    missing = [name for name in MATERIAL_PROPERTIES if name not in vals]
    if missing:
        raise ValueError("KAT_Config.example.txt references shader properties that are absent: " + ", ".join(missing))
    mat_lines = [
        "# Shader material defaults. Uncomment only the properties you want the installer to write.",
        "# Runtime-driven properties (_Tail, _Scale, _Padding, _CornerRadius, _BorderWidth, _OutlineWidth) can still be",
        "# given an install-time starting value; the animator owns them after its layer runs.",
    ]
    for name in MATERIAL_PROPERTIES:
        mat_lines.append(f"# {name:<16} = {vals[name]}")
    mat_lines += [
        "# _DebugFillEnabled / _DebugFillPage / _DebugFillSlot / _DebugFillColumns / _DebugFillRows are diagnostic fields",
        "# and are reset to zero by the installer.",
    ]
    text = CONFIG_EXAMPLE.read_text(encoding="utf-8")
    start = text.index("[material]\n")
    end = text.index("\n[transforms]\n", start)
    new_section = "[material]\n" + "\n".join(mat_lines) + "\n"
    out = text[:start] + new_section + text[end + 1:]
    return out

def update_config_example() -> bool:
    current = CONFIG_EXAMPLE.read_text(encoding="utf-8")
    expected = render_config_example()
    if expected != current:
        atomic_write(CONFIG_EXAMPLE, expected)
        return True
    return False


def apply_protocol_patch(p: dict, args: argparse.Namespace) -> bool:
    changed = False
    for key, value in {
        "text_length": args.text_length,
        "line_length": args.line_length,
        "line_count": args.line_count,
        "sync_params_default": args.sync_params_default,
        "sync_params_min": args.sync_params_min,
        "sync_params_max": args.sync_params_max,
    }.items():
        if value is not None and p.get(key) != value:
            p[key] = value
            changed = True
    return changed


def write_protocol(p: dict, dry: bool = False) -> bool:
    out = json.dumps(p, ensure_ascii=False, indent=2) + "\n"
    current = PROTOCOL_PATH.read_text(encoding="utf-8")
    if out != current and not dry:
        atomic_write(PROTOCOL_PATH, out)
    return out != current


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--check", action="store_true", help="do not write; fail when any generated artifact is out of date")
    ap.add_argument("--text-length", type=int)
    ap.add_argument("--line-length", type=int)
    ap.add_argument("--line-count", type=int)
    ap.add_argument("--sync-params-default", type=int)
    ap.add_argument("--sync-params-min", type=int)
    ap.add_argument("--sync-params-max", type=int)
    args = ap.parse_args()

    p = load_protocol()
    changed_protocol = apply_protocol_patch(p, args)
    validate_protocol_values(p)
    if args.check and changed_protocol:
        print("protocol.json would change from the requested values")
        return 1
    if changed_protocol:
        print("updated protocol.json")
        write_protocol(p)

    if args.check:
        expected_settings = render_settings(p)
        expected_config = render_config_example()
        problems = []
        if expected_settings != SETTINGS_PATH.read_text(encoding="utf-8"):
            problems.append("KAT_Settings.cs")
        if expected_config != CONFIG_EXAMPLE.read_text(encoding="utf-8"):
            problems.append("KAT_Config.example.txt")
        if problems:
            print("out of date:", ", ".join(problems))
            return 1
        if not load_gen_shader().check():
            return 1
        validate_protocol.main()
        print("all generated artifacts are synchronized")
        return 0

    update_settings(p)
    update_config_example()
    load_gen_shader().regenerate()
    validate_protocol.main()
    print("generated artifacts are synchronized")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
