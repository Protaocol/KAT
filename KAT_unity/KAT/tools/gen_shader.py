"""
Regenerates ../Shaders/KF_AvatarTextShaderPaged.shader from shader_template.txt.

The 128 _CharN + 128 _PageM property lines (64 used by page format 2, 128 by format 3), their cbuffer register aliases and the "touch every property"
sum are repetitive, so they are emitted here. Edit shader_template.txt for shader changes, then:

    python tools/gen_shader.py           # regenerate
    python tools/gen_shader.py --check   # exit 1 if the committed shader is out of date

It also clears the debug-fill fields (_DebugFill*) stored in the KAT .mat files, so a value left over from tuning can never
ship. Unity does the same on every shader reimport (KAT_DebugFields.cs).
"""
import pathlib
import re
import sys

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[2]
sys.path.insert(0, str(ROOT))
try:
    from kat_protocol import TEXT_LENGTH  # noqa: E402
except ImportError:
    # The packaged KAT archive does not ship the wider project's kat_protocol.py.
    # Keep the standalone generator usable and synchronized with KatSettings.TextLength.
    TEXT_LENGTH = 128

NCH = TEXT_LENGTH
NPG = TEXT_LENGTH // 2          # format 2: one _Page per PAIR of slots
NPG3 = TEXT_LENGTH              # format 3: one _Page per SLOT. NCH must match KatSettings.TextLength and the stock CharN clips.

props = "\n".join(f'\t\t[HideInInspector] _Char{i}("Character {i}", Float) = 0' for i in range(NCH))
# _Page0..127 always exist as properties: format 2 animates 0..63 (pairs), format 3 animates 0..127 (slots).
props += "\n" + "\n".join(f'\t\t[HideInInspector] _Page{j}("Page {j} (pair in format 2, slot in format 3)", Float) = 0' for j in range(NPG3))
# Character/page storage is packed four scalars per float4 register (see the comment in the template): _CharN lives in
# c(N/4).(N%4), _PageM in c(NCH/4 + M/4).(M%4). The named properties stay the public animation targets.
LANES = "xyzw"


def alias(name, index, base_register):
    return f"\t\t\tfloat {name}{index} : packoffset(c{base_register + index // 4}.{LANES[index % 4]});"


PAGE_BASE_REGISTER = NCH // 4
decl = "\n".join(alias("_Char", i, 0) for i in range(NCH))
# The first NPG page lanes are shared by both formats; format 3 only appends registers, nothing shifts.
decl += "\n" + "\n".join(alias("_Page", j, PAGE_BASE_REGISTER) for j in range(NPG))
decl += "\n#ifdef KAT_PAGE_FORMAT_3\n" + "\n".join(alias("_Page", j, PAGE_BASE_REGISTER) for j in range(NPG, NPG3)) + "\n#endif"


def summ(prefix, n, per=8):
    rows = ["\t\t\t" + " + ".join(f"{prefix}{i}" for i in range(r, min(r + per, n))) for r in range(0, n, per)]
    return " +\n".join(rows)


tpl = (HERE / "shader_template.txt").read_text(encoding="utf-8")
dest = HERE.parent / "Shaders" / "KF_AvatarTextShaderPaged.shader"


def render() -> str:
    return (tpl.replace("@@PROPS@@", props).replace("@@DECL@@", decl)
            .replace("@@SUM_CHARS@@", summ("_Char", NCH)).replace("@@SUM_PAGES2@@", summ("_Page", NPG))
            .replace("@@SUM_PAGES3@@", summ("_Page", NPG3)))


def check() -> bool:
    current = dest.read_text(encoding="utf-8") if dest.exists() else ""
    if current.replace("\r\n", "\n") != render().replace("\r\n", "\n"):
        print(f"{dest} is out of date -- run: python tools/gen_shader.py")
        return False
    print("shader is up to date")
    return True


def regenerate() -> None:
    out = render()
    dest.parent.mkdir(exist_ok=True)
    dest.write_text(out, encoding="utf-8")
    print(f"wrote {dest} ({len(out.splitlines())} lines)")
    # Debug fields in material YAML: _DebugFillEnabled / _DebugFillPage / _DebugFillSlot / _DebugFillColumns / _DebugFillRows
    DEBUG_FLOAT = re.compile(r"(- _DebugFill(?:Enabled|Page|Slot|Columns|Rows): )[^\r\n]+")
    for mat in sorted(HERE.parent.rglob("*.mat")):
        text = mat.read_bytes().decode("utf-8")
        fixed = DEBUG_FLOAT.sub(r"\g<1>0", text)
        if fixed != text:
            mat.write_bytes(fixed.encode("utf-8"))
            print(f"reset debug fields in {mat.relative_to(HERE.parent)}")


if __name__ == "__main__":
    if "--check" in sys.argv:
        sys.exit(0 if check() else 1)
    regenerate()
