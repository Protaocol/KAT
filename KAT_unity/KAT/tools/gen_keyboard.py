#!/usr/bin/env python3
"""
Regenerates the in-game keyboard from one layout table:

  Textures/KAT_Keyboard.png            key caps + the labels of the special keys (default look)
  Textures/KAT_Keyboard_Shift.png      the same, with the Shift caps lit (shown while Shift is held)
  Textures/KAT_Keyboard_Highlight.png  region ids: R = G = key id, B = 255 on a key (what the press highlight looks up)
  Shaders/KAT_KeyboardKeyRects.cginc   every key's rectangle in uv space (the shader draws each key's glyph inside it)
  Resources/KAT_Misc/KillFrenzyAvatarKeyboard.prefab   the contact receivers

The glyph on each key is NOT in the texture. The shader reads it live out of the same glyph atlas the text uses, so the legends are right
on every page, not just page 0.

Key map (one map for every page; this is the "unify everything" rule):
  * the 26 letters sit where a QWERTY keyboard has them, and their key number K is the letter's position in the top half of page 0:
    A..Z = K 33..58 (page 0 slot 33..58 = ASCII 65..90)
  * every other key takes the remaining K values (0..32 and 59..63) in reading order: top row left to right, then down.
  * top half of a page    : the key with number K types glyph slot K                    (Shift held)
  * lower half of a page  : the key with number K types glyph slot 64 + (K ^ 32)        (default)
    (K ^ 32 swaps the two halves of ASCII's case bit, so on page 0 the key that types A in the top half types a in the lower half,
     @ <-> `, [ <-> {, and so on.)

Run it from KAT_unity/KAT:   python3 tools/gen_keyboard.py
Needs Pillow. Touches the files above only; commit the result.
"""
import re
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
W, H = 4098, 2048
PREFAB = ROOT / "Resources/KAT_Misc/KillFrenzyAvatarKeyboard.prefab"
FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"

# ---- id numbers the highlight map and the animator share (keep in sync with KatSettings) --------------------------------------
ID_CLEAR, ID_BKSP, ID_SHIFT_L, ID_SHIFT_R, ID_SPACE, ID_PGUP, ID_PGDN, ID_DISPLAY = 64, 65, 66, 67, 68, 69, 70, 71

KEY_W = 208           # key cap size in texture pixels (the grid pitch is about 272)
RADIUS = 26

# ---- layout in prefab/quad coordinates (the contacts live in the Layout quad's local space: x -0.5..0.5, y -0.25..0.25 stretched) --
# existing key positions, read from the old prefab so the new layout lines up with what people are used to
OLD = {  # name -> (x, y)
    "`": (-0.444, 0.276), "1": (-0.3785, 0.276), "2": (-0.3121, 0.276), "3": (-0.2474, 0.276), "4": (-0.1819, 0.276),
    "5": (-0.1155, 0.276), "6": (-0.05, 0.276), "7": (0.0164, 0.276), "8": (0.0819, 0.276), "9": (0.1474, 0.276),
    "0": (0.2138, 0.276), "-": (0.2793, 0.276), "=": (0.3439, 0.276),
    "q": (-0.3442, 0.142), "w": (-0.2795, 0.142), "e": (-0.2131, 0.142), "r": (-0.1476, 0.142), "t": (-0.0826, 0.142),
    "y": (-0.0164, 0.142), "u": (0.0492, 0.142), "i": (0.116, 0.142), "o": (0.1816, 0.142), "p": (0.2466, 0.142),
    "[": (0.3122, 0.142), "]": (0.3778, 0.142), "\\": (0.4434, 0.142),
    "a": (-0.3344, 0.011), "s": (-0.2664, 0.011), "d": (-0.2008, 0.011), "f": (-0.1346, 0.011), "g": (-0.0684, 0.011),
    "h": (-0.0016, 0.011), "j": (0.0646, 0.011), "k": (0.1314, 0.011), "l": (0.197, 0.011), ";": (0.2632, 0.011), "'": (0.3294, 0.011),
    "z": (-0.3058, -0.12), "x": (-0.2396, -0.12), "c": (-0.1728, -0.12), "v": (-0.1072, -0.12), "b": (-0.041, -0.12),
    "n": (0.0252, -0.12), "m": (0.0914, -0.12), ",": (0.157, -0.12), ".": (0.2238, -0.12), "/": (0.2912, -0.12),
}
PITCH = 0.06566
ROW_Y = {"r0": 0.409, "r6": -0.386}


def build_layout():
    """Returns {K: (x, y)} for the 64 glyph keys, in quad coordinates."""
    letters = {}
    for i, ch in enumerate("abcdefghijklmnopqrstuvwxyz"):
        letters[33 + i] = OLD[ch]

    # the non-letter keys in reading order
    r0 = [(-0.444 + PITCH * n, ROW_Y["r0"]) for n in range(12)]
    r1 = [OLD[c] for c in "`1234567890-="]
    r2 = [OLD[c] for c in "[]\\"]
    r3 = [OLD[c] for c in ";'"]
    r4 = [OLD[c] for c in ",./"]
    r6 = [(PITCH * n, ROW_Y["r6"]) for n in (-2, -1, 0, 1, 2)]
    others = r0 + r1 + r2 + r3 + r4 + r6
    ks = list(range(0, 33)) + list(range(59, 64))
    assert len(others) == len(ks) == 38, (len(others), len(ks))
    layout = dict(letters)
    layout.update(dict(zip(ks, others)))
    assert sorted(layout) == list(range(64))
    return layout


# specials: (name, id, param, x0, x1, y_center) in quad x / y
SPECIALS = [
    # name,    id,         parameter,                    centre x, centre y, cap width px, cap height px, contact capsule height
    ("Clear",  ID_CLEAR,   "KAT_KeyboardPressedClear",   0.402,   0.4359,  530, 196, 0.13),
    ("Bksp",   ID_BKSP,    "KAT_KeyboardPressedBksp",    0.4272,  0.276,   336, 208, 0.08),
    ("ShiftL", ID_SHIFT_L, "KAT_KeyboardPressedShiftL", -0.4079, -0.124,   500, 208, 0.12),
    ("ShiftR", ID_SHIFT_R, "KAT_KeyboardPressedShiftR",  0.402,  -0.124,   530, 208, 0.13),
    ("Space",  ID_SPACE,   "KAT_KeyboardPressedSpace",  -0.0001, -0.255,  1756, 208, 0.43),
    ("PgUp",   ID_PGUP,    "KAT_KeyboardPressedPgUp",    0.31,   -0.255,   208, 208, 0.04),
    ("PgDn",   ID_PGDN,    "KAT_KeyboardPressedPgDn",    0.3757, -0.255,   208, 208, 0.04),
]
DISPLAY = (-0.355, -0.255, 940, 208)   # centre x, centre y, width px, height px

LABELS = {"Clear": "Clear", "Bksp": "Bksp", "ShiftL": "Shift", "ShiftR": "Shift", "PgUp": "\u25b2", "PgDn": "\u25bc"}


def px(x, y):
    return (x + 0.5) * W, (0.5 - y) * H


def rect_px(cx, cy, w, h):
    x, y = px(cx, cy)
    return (round(x - w / 2), round(y - h / 2), round(x + w / 2), round(y + h / 2))


def all_rects(layout):
    """id -> pixel rectangle (x0, y0, x1, y1), y down."""
    rects = {k: rect_px(x, y, KEY_W, KEY_W) for k, (x, y) in layout.items()}
    for name, kid, _p, x, y, w, h, _c in SPECIALS:
        rects[kid] = rect_px(x, y, w, h)
    rects[ID_DISPLAY] = rect_px(DISPLAY[0], DISPLAY[1], DISPLAY[2], DISPLAY[3])
    return rects


def draw_textures(rects):
    font = ImageFont.truetype(FONT, 92)
    names = {kid: name for name, kid, *_ in SPECIALS}
    for shift_lit, out in ((False, "Textures/KAT_Keyboard.png"), (True, "Textures/KAT_Keyboard_Shift.png")):
        im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        d = ImageDraw.Draw(im)
        for kid, r in rects.items():
            if kid == ID_DISPLAY:
                d.rounded_rectangle(r, RADIUS, fill=(6, 6, 6, 255), outline=(60, 60, 60, 255), width=5)
                continue
            lit = shift_lit and kid in (ID_SHIFT_L, ID_SHIFT_R)
            d.rounded_rectangle(r, RADIUS, fill=(150, 70, 10, 255) if lit else (17, 17, 17, 255))
            label = LABELS.get(names.get(kid, ""))
            if label:
                cx, cy = (r[0] + r[2]) / 2, (r[1] + r[3]) / 2
                d.text((cx, cy), label, font=font, fill=(235, 235, 235, 255), anchor="mm")
        im.save(ROOT / out)

    hl = Image.new("RGB", (W, H), (0, 0, 0))
    d = ImageDraw.Draw(hl)
    for kid, r in rects.items():
        d.rounded_rectangle(r, RADIUS, fill=(kid, kid, 0 if kid == ID_DISPLAY else 255))
    hl.save(ROOT / "Textures/KAT_Keyboard_Highlight.png")


def write_rects(rects):
    lines = ["// Generated by tools/gen_keyboard.py. Do not edit.",
             "// Every keyboard region in uv space: (x0, y0, width, height), y up. Ids 0..63 are the glyph keys, 71 is the page display.",
             "static const float4 KAT_KEY_RECT[72] = {"]
    for i in range(72):
        if i in rects:
            x0, y0, x1, y1 = rects[i]
            lines.append("\tfloat4(%.6f, %.6f, %.6f, %.6f)," % (x0 / W, 1 - y1 / H, (x1 - x0) / W, (y1 - y0) / H))
        else:
            lines.append("\tfloat4(0, 0, 0, 0),")
    lines.append("};")
    (ROOT / "Shaders/KAT_KeyboardKeyRects.cginc").write_text("\n".join(lines) + "\n")


# ---- prefab ---------------------------------------------------------------------------------------------------------------------

def rewrite_prefab(layout):
    text = PREFAB.read_bytes().decode("utf8")
    crlf = "\r\n" in text
    text = text.replace("\r\n", "\n")
    parts = re.split(r"\n(?=--- )", text)
    head, blocks = [], []
    for p in parts:
        m = re.match(r"--- !u!(\d+) &(\d+)", p)
        if m:
            blocks.append([int(m.group(1)), m.group(2), p])
        else:
            head.append(p)
    by_id = {b[1]: b for b in blocks}

    def name_of(b):
        return re.search(r"m_Name: (.*)", b[2]).group(1)

    def comp_of(go_id, ty):
        for b in blocks:
            if b[0] == ty and re.search(r"m_GameObject: \{fileID: %s\}" % go_id, b[2]):
                return b
        return None

    def children(tr):
        part = tr[2].split("m_Children:")[1].split("m_Father")[0]
        return re.findall(r"- \{fileID: (\d+)\}", part)

    tr_of_go = {}
    for b in blocks:
        if b[0] == 4:
            tr_of_go[re.search(r"m_GameObject: \{fileID: (\d+)\}", b[2]).group(1)] = b
    go_by_name = {name_of(b): b for b in blocks if b[0] == 1}
    low_go, up_go, sp_go = go_by_name["LowerCase"], go_by_name["UpperCase"], go_by_name["Special"]

    # template: an existing key receiver (GameObject + Transform + MonoBehaviour)
    first_child_tr = by_id[children(tr_of_go[low_go[1]])[0]]
    tmpl_go = by_id[re.search(r"m_GameObject: \{fileID: (\d+)\}", first_child_tr[2]).group(1)]
    tmpl_mono = comp_of(tmpl_go[1], 114)
    # a capsule receiver (Space) is the template for the special keys
    space_tr = next(by_id[c] for c in children(tr_of_go[sp_go[1]])
                    if name_of(by_id[re.search(r"m_GameObject: \{fileID: (\d+)\}", by_id[c][2]).group(1)]) == "Space")
    space_go = by_id[re.search(r"m_GameObject: \{fileID: (\d+)\}", space_tr[2]).group(1)]
    space_mono = comp_of(space_go[1], 114)

    # throw away the old keys; keep the specials that stay (Bksp, ShiftL, ShiftR, Space, Clear), they get rewritten below
    dead = set()
    for parent in (low_go, up_go, sp_go):
        for c in children(tr_of_go[parent[1]]):
            tr = by_id[c]
            go_id = re.search(r"m_GameObject: \{fileID: (\d+)\}", tr[2]).group(1)
            dead.update({c, go_id, comp_of(go_id, 114)[1]})
    blocks = [b for b in blocks if b[1] not in dead]

    counter = [7100000000000000000]

    def nid():
        counter[0] += 1
        return str(counter[0])

    def make_key(name, parent_tr, pos, param, capsule_height=None):
        go_id, tr_id, mono_id = nid(), nid(), nid()
        src_go, src_mono = (tmpl_go, tmpl_mono) if capsule_height is None else (space_go, space_mono)
        go = src_go[2]
        go = re.sub(r"--- !u!1 &\d+", "--- !u!1 &" + go_id, go)
        go = re.sub(r"(m_Component:\n)(?:  - component: \{fileID: \d+\}\n)+",
                    r"\1  - component: {fileID: %s}\n  - component: {fileID: %s}\n" % (tr_id, mono_id), go)
        go = re.sub(r"m_Name: .*", "m_Name: " + name, go, count=1)
        tr = ("--- !u!4 &%s\nTransform:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n"
              "  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: %s}\n  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}\n"
              "  m_LocalPosition: {x: %.4f, y: %.4f, z: 0}\n  m_LocalScale: {x: 1, y: 1, z: 1}\n  m_Children: []\n"
              "  m_Father: {fileID: %s}\n  m_RootOrder: 0\n  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}" % (tr_id, go_id, pos[0], pos[1], parent_tr[1]))
        mono = src_mono[2]
        mono = re.sub(r"--- !u!114 &\d+", "--- !u!114 &" + mono_id, mono)
        mono = re.sub(r"m_GameObject: \{fileID: \d+\}", "m_GameObject: {fileID: %s}" % go_id, mono)
        mono = re.sub(r"parameter: .*", "parameter: " + param, mono)
        if capsule_height is not None:
            mono = re.sub(r"height: .*", "height: %s" % capsule_height, mono)
        return [1, go_id, go], [4, tr_id, tr], [114, mono_id, mono]

    new_children = {low_go[1]: [], up_go[1]: [], sp_go[1]: []}
    for k, pos in sorted(layout.items()):
        for set_go, param_id in ((low_go, 64 + (k ^ 32)), (up_go, k)):
            parent_tr = tr_of_go[set_go[1]]
            go, tr, mono = make_key("K%02d" % k, parent_tr, pos, "KAT_KeyboardPressed%d" % param_id)
            blocks += [go, tr, mono]
            new_children[set_go[1]].append(tr[1])
    for name, kid, param, x, y, _w, _h, cap_h in SPECIALS:
        go, tr, mono = make_key(name, tr_of_go[sp_go[1]], (x, y), param, capsule_height=cap_h)
        blocks += [go, tr, mono]
        new_children[sp_go[1]].append(tr[1])

    for go_id, kids in new_children.items():
        tr = tr_of_go[go_id]
        tr[2] = re.sub(r"m_Children:\n(?:  - \{fileID: \d+\}\n?)*|m_Children: \[\]\n?",
                       "m_Children:\n" + "".join("  - {fileID: %s}\n" % k for k in kids), tr[2], count=1)

    out = "\n".join(head + [b[2] for b in blocks]) if False else None
    header = parts[0]
    body = "\n".join([header] + [b[2] for b in blocks])
    if not body.endswith("\n"):
        body += "\n"
    if crlf:
        body = body.replace("\n", "\r\n")
    PREFAB.write_bytes(body.encode("utf8"))


def main():
    layout = build_layout()
    rects = all_rects(layout)
    draw_textures(rects)
    write_rects(rects)
    rewrite_prefab(layout)
    print("keys:", len(layout), "+", len(SPECIALS), "special; wrote textures, rect table and prefab")
    for k in sorted(layout):
        print("K%02d  top slot %3d  lower slot %3d  at (%.3f, %.3f)" % (k, k, 64 + (k ^ 32), *layout[k]))


if __name__ == "__main__":
    sys.exit(main())
