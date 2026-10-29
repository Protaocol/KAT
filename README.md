# KAT Paged Glyphs

A rework of [KillFrenzyAvatarText](https://github.com/killfrenzy96/KillFrenzyAvatarText) (KAT) that renders persistent,
atlas-based text (great for large charsets like Chinese) on your VRChat avatar. Text is baked offline into SDF page
atlases, then streamed over OSC into avatar parameters that a custom shader turns back into text.

Note: This is developed with chinese glyphs and fonts in mind. Even tho the idea is absolutely compatible with probably all languages and glyphs and even emojis, you can see quite alot of chinese as tests and some tailored features (like opencc t2s in KatOscApp.pyw and chinese punctuations list). Feel free to add, modify or remove those if it didn't suit your need or is causing troubles

---

## TLDR — install & quickstart

1. **Python 3.10+**, then:

   ```shell
   pip install -r requirements.txt
   ```

   (use of python venv is recommended so you don't accidentally nuke your global package environment, cuz who knows)

2. **Build the charset + atlas from a font** (this runs the whole pipeline):

   ```shell
   python font2atlas.py NotoSansCJKsc-Regular.otf
   ```

   This produces `charset.json` and `atlas/`. The charset plus atlas are all the client needs.

3. **In Unity:**

   Method 1:

   import [KillFrenzyAvatarText](https://github.com/killfrenzy96/KillFrenzyAvatarText/releases/download/1.3.2/KAT_UnityPackage_v1.3.2.unitypackage), copy this project's `KAT_unity/KAT/Scripts` and
   `KAT_unity/KAT/Shaders` over the originals (let them overwrite), then
   **Tools > KAT > KAT Paged > Build Glyph Array + Material (BC4)** (or R8 as fallback) and pick the `atlas/` folder.

   Method 2:

   copy the entire KAT_unity/KAT into your project's `/Assets`, then import your atlas with the step above

   Method 3 (tbd):

   import [KAT](https://github.com/pr0t4oco1/KAT/releases), then import your atlas with the step above

4. **Install** with the normal KAT installer (**Tools > KAT > Install KAT**), upload the avatar, and
   enable OSC in VRChat.

5. **Send text from Python** (or use the desktop app `KatOscApp.pyw`):

   ```python
   from katosc import KatOsc, KatOscConfig
   cfg = KatOscConfig()
   cfg.charset_path = "charset.json"
   kat = KatOsc(cfg)
   kat.set_text("你好，世界！\nHello world")
   ```

6. **Preview without Unity**:

   You can use `preview_layout.py` to preview how your text would look like in game without
   needing to compile your entire avatar by using the command below

   `python preview_layout.py charset.json atlas/ "你好 Hello" preview.png`

   This way if your avatar takes a long time to compile, you can use this to choose a font you like
   and ensure no tofu and no `?` on characters you will use before using the font/atlas

---

## 1. Requirements

- Python 3.10+, `pip install -r requirements.txt` (OSC client + toolchain: `python-osc`, Pillow, NumPy, SciPy,
  OpenCC, wordfreq, FontTools).
- Unity with the VRChat avatar SDK set up.
- A font that has all the characters you would type (LXGW WenKai, Noto Sans CJK, SIL OFL fonts, etc).
- PC/PCVR only. Sadly vrchat on mobile/standalone quest doesnt support avatars with custom shaders, so it would be
impossible for the chatbox to work there.

## 2. Quickstart

### 2.1 Build charset + atlas

The one-command pipeline:

```shell
python font2atlas.py yourfont.otf
```

The quickstart defaults to paged format 2 and fills as much of the selected format as the ranked character source allows. Use `--protocol 3` for the larger format. The quickstart uses face 0 and the same font for ASCII/narrow glyphs; if you need a different `.ttc` face, a separate narrow font, or non-default cell/SDF settings, use the advanced staged tools below. It writes `charset.json` and `atlas/` in the current directory, and asks before replacing an existing build.

For advanced control, run the stages yourself to inspect the intermediates, or start from the middle after editing something:

```shell
python font2charset.py yourfont.otf glyphs.txt   # get all glyphs that the font has
python filter_charset.py glyphs.txt filtered.txt # rank by zipf word/character frequency
python build_charset.py --freq filtered.txt --count 3500 -o charset.json
python build_atlas.py charset.json --font yourfont.otf --narrow-font monofont.ttf --cell 64 -o atlas/
```

Add `--font-index N` for `.ttc` collections (Noto CJK: 0=Japanese, 1=Korean, 2=SC, 3=TC, 4=HK). Add `--strict` to
`build_atlas.py` to fail instead of warn when the font lacks a glyph.

### 2.2 Unity

1. Install with either one of the following ways

   a. import [the original KillFrenzyAvatarText](https://github.com/killfrenzy96/KillFrenzyAvatarText/releases/download/1.3.2/KAT_UnityPackage_v1.3.2.unitypackage)

      copy this project's `KAT_unity/KAT/Scripts` and `KAT_unity/KAT/Shaders` over the originals (overwrite).

   b. copy the entire KAT_unity/KAT into your project's `/Assets`

   c. import [this version of KAT](https://github.com/pr0t4oco1/KAT/releases)
2. **Tools > KAT > KAT Paged > Build Glyph Array + Material (BC4)** (or R8 as fallback) -> pick the generated `atlas/` folder
3. Install with the KAT installer at **Tools > KAT > Install KAT**

### 2.3 Run the client / app

```shell
python KatOscApp.pyw --charset charset.json
```

or from code:

```python
import time
from katosc import KatOsc, KatOscConfig

cfg = KatOscConfig()
cfg.charset_path = "charset.json"
cfg.keep_seconds = None
kat = KatOsc(cfg)
kat.set_text("你好，世界！\nHello 2026")
time.sleep(30)
kat.set_text("")
kat.stop()
```

## 3. How it works

- **Glyphs & pages.** Every character is baked into a numbered glyph, `glyph_index = page * 255 + slot`. Page 0 holds
  up to 128 narrow glyphs (ASCII, monospace-shaped). Pages 1+ hold wide (CJK) glyphs, up to 255 per page.
- **Slots.** The visible text box is a fixed grid of 128 slots (32 × 4). Narrow glyphs take 1 slot; wide glyphs take 2
  (`[glyph][FOLLOW]`, FOLLOW = page 0 slot 254). Layout code pads so wide glyphs won't split across a row or an OSC
  transmission group.
- **OSC transport.** VRChat synced floats only carry 8 bits (255 usable values: `symbol` 0–254). A "pointer write" sends
  one group of M slots (M even, default 4): M char floats + page floats (M/2 in format 2, M in format 3).
- **The animator trick.** It would be absolute pain to write every possible permutation as a state in the animator, plus
  only M slots are sent per transmission. Therefore, we split the whole 128 characters board into chunks, then write each
  chunk one by one. Clearing the board takes the same time as writing a full board so `KAT_Pointer = 255` is the reserved
  "clear everything" signal specificly for this frequently used command. That is also why it cost so much more to work on
  WD-on animators, because WD-off keeps the state of previous chunks when another chunk is being written, only change when
  explicitly changed. WD-on just reset everything once you finish a chunk and so each slot now needs a parameter to keep
  the glyph from instantly disappearing.
- **The shader** reads the remembered `_CharN`/`_PageN` values, samples the SDF atlas in a Texture2DArray, and draws
  smooth edges at any size, plus optional box/border/speech-bubble tail around the text.
- **The atlas** is a one-time offline bake: each glyph rendered from the font into a signed-distance-field page
  (`atlas/page_XX.png` + `atlas_meta.json` with format, page count, cell size, and charset SHA-256).

### Page formats (2 vs 3)

| | Format 2 (default) | Format 3 (opt-in) |
| --- | --- | --- |
| Page numbers per synced float | 2 (base-15 pair, `pa * 15 + pb`) | 1 (the page itself) |
| Max page number | 14 | 253 |
| Wide-glyph capacity | 3,570 | 64,515 |
| Wire bits per slot | 12 | 16 |
| `KAT_PageSync` floats per group of M | M / 2 | M |
| Shader | `% 15u` / `/ 15u`, `_PagePack[16]` | `_PagePack[slot]`, `_PagePack[32]`, keyword `KAT_PAGE_FORMAT_3` |

Use format 3 only when your charset exceeds 14 wide pages; it costs more wire bits, animator layers and synced bits
(M ≤ 8 at 128 slots, since M=16 exceeds the 256-bit budget). To switch: bake the charset/atlas for format 3
(`--format 3`), Choose **3** under `Page format` in the installer, then the client auto-detects the avatar's
format from its `KAT_PageSync*` parameter count and sends accordingly. The client will refuse to send your text if
the format between the client and the avatar mismatch btw.

## 4. Customization

You can just see what you can customize by running the python scripts with a `-h` or `--help`

- **Font / cell size / spread**: `build_atlas.py --cell 64 --spread 6 --supersample 4 --strict`.
- **Charset contents**: `build_charset.py --pattern ... --count N --extra "..." --punct ... --narrow ...`. The charset
  is always chosen explicitly (`--charset`, Browse..., or `KatOscConfig.charset_path`).
- **Box / border / colors / scale / anchor**: live material properties on `KAT_Text_Paged` (`_BoxEnabled`,
  `_ShrinkToFit`, `_BackColor`, `_BorderColor`, `_BorderWidth`, `_Padding`, `_CornerRadius`, `_Scale`, `_AnchorX`,
  `_AnchorY`, `_MainColor`, `_ShadowColor`, `_OutlineWidth`, `_Weight`, `_GrowEnabled`). Preview with
  `preview_layout.py`.
- **Timing**: `KatOscConfig` — `osc_delay` (0.25 s default), `osc_delay_slow`/`osc_delay_fast` mapped exponentially
  from the Roll Speed dial, `resync_interval`/`resync_first_delay` for the idle heal pass.
- **Keep text time**: empty = never clear, `0` = KAT hidden, positive = auto-clear N seconds after delivery.
- **In-world keyboard**: not supported in this build (it can't send page numbers). Typing from `KatOscApp.pyw` or a
  script please. I am more than happy to accept pull requests that made the keyboard working.

## 5. Limits

| | Limit | Why |
| --- | --- | --- |
| Wide glyphs | 3,570 (fmt 2) / 64,515 (fmt 3) | wide pages × 255 |
| Narrow glyphs | 128 | fixed 16×8 page-0 grid |
| On-screen glyphs | 128 slots | 128 narrow, or 64 wide, or a mix |
| Synced bits per avatar | 256 | VRChat budget |

The shipped base set uses 97 narrow + 25 punctuation, leaving 3,545 CJK slots in format 2 (Level-1 3500 fits). The quickstart uses this remaining capacity as its default ranked-character cutoff.

## 6. Development

Key files: `kat_protocol.py` (protocol definition + helpers), `kat_codec.py` (wire codec + `Charset`), `katosc.py`
(OSC client), `kat_app.py` (app logic), `build_charset.py` / `build_atlas.py` (builders), `font2charset.py` /
`filter_charset.py` (font inventory -> ranked list), `font2atlas.py` (quickstart pipeline), `sanity_check.py`,
`preview_layout.py`, `osc_proxy.py` (OSC sniffer), `update_generated.py` / `validate_protocol.py` /
`KAT_unity/KAT/tools/gen_shader.py` (generated-artifact sync), `protocol.json` (source of truth to make sure client's and avatar's parameters match).

## 7. Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| `?` where a character should be | Not in `charset.json` (advanced path: use `build_charset.py --extra`), or NFKC folding doesn't map your input onto a present glyph |
| Tofu boxes | Font missing that glyph — check `build_atlas.py` warnings / `missing_glyphs` in `atlas_meta.json` |
| Nothing shows | Wrong material, missing glyph array, shader compile error, wrong render queue, or `KAT_Visible` off |
| Right shapes, wrong glyphs | Charset/atlas from different builds (`sanity_check.py` compares hashes), or avatar built without the patched scripts |
| Half a Chinese character | Client `text_length`/layout mismatch, or wide glyph crossing a row/group boundary; rebuild with current tools |
| Blurry strokes | Cell size too small or `--spread` too large — tune `--cell` / `--spread` with the advanced `build_atlas.py` stage |
| Faded glyph texture | Re-imported as sRGB; the array builder makes it linear on purpose |
| Text fills slowly / flickers | Chunk size vs refresh trade-off; enable Write Defaults-off check via the audit tool |
| Box shrinks away from head | `KAT_Style_HeadScale` layer missing; re-run the current install path |

## 8. Licenses

This project is licensed under the **MIT License** (see the root `LICENSE` file for details), with the following third-party components and derivative works:

- `katosc.py` and `KatOscApp.pyw` both derive from [KatOsc](https://github.com/killfrenzy96/KatOscApp/tree/main) with **MIT** license.
- Components inside `unity/KAT` (including the shader and Unity scripts) derive from [KAT](https://github.com/killfrenzy96/KillFrenzyAvatarText) which also uses **MIT** license.
- Baked atlases are derivative of the font you render with, check its license (SIL OFL fonts like Noto allow embedding). This project ships no fonts or atlases.

## Legacy shader compatibility

The original non-paged `KF_AvatarTextShader.shader` is retained for compatibility with older setups (but not used). It uses the original tiled `_MainTex` atlas and is not interchangeable with the generated SDF page atlas.
