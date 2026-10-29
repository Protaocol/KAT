# KillFrenzy Avatar Text (KAT)

A text display system for VRChat avatars, driven by OSC. This paged build handles large
charsets (all of CJK) via SDF glyph-page atlases and a persistent single-quad text renderer.

Based on [killfrenzy96's KillFrenzyAvatarText](https://github.com/killfrenzy96/KillFrenzyAvatarText)

---

## TLDR (install & quickstart)

1. **Unity 2019.4.31f1/2022.3.6f1/2022.3.22f1 + VRCSDK3** (with avatar dynamics).
2. Import the Unity package (Releases).
3. In Unity: **Tools > KAT > KAT Paged > Build Glyph Array + Material**, pick the `atlas/` folder produced by `font2atlas.py`.
4. **Tools > KAT > Install KAT** on your avatar.
5. Upload the avatar, enable OSC in VRChat, then run a KAT OSC app (or `KatOscApp.pyw`) to set text.

---

## Prerequisites

- Unity 2019.4.31f1, VRCSDK3 with avatar dynamics, an existing avatar.
- Parameter budget: 18 bits for the slowest setting (1 sync parameter), up to 74 bits at 8 sync
  parameters. Budget is shown live in the installer options panel.
- The avatar's animator should use **Write Defaults off**, it could be WD on but with a cost of more parameters.

## Recommended OSC apps

You need an app to write text over OSC. On this repo, that is the in-project Python client
(`katosc.py` / `KatOscApp.pyw`) in the repository root, modified from [the original repo](https://github.com/killfrenzy96/KatOscApp),
not sure but probably still supports [TTS Voice Wizard](https://github.com/VRCWizard/TTS-Voice-Wizard) or anything that originally supports KillFrenzy's KAT

## Whats new (or changed)

Against the stock KillFrenzyAvatarText package, this build:

- Replaces the glyph atlas with a **Texture2DArray of SDF pages** (narrow ASCII page + wide CJK pages)
  built from your own charset (`font2atlas.py` -> `charset.json` + `atlas/`).
- Uses the **paged shader** `KF_AvatarTextShaderPaged.shader` with `_CharN`/`_PageN` material properties.
- Adds paging animator layers and `KAT_PageSync*` parameters, auto-detected by the client.
- Supports **two page formats** (see below). The format is selected by the installer and must match the
  atlas metadata; the OSC client reads it back from the avatar, so there is nothing to configure on it.
- You can now use it with **WD on** too yayayay, just tick a box at installation and get some extra parameters to compensate

## Page format

| | Format 2 (default) | Format 3 (opt-in) |
| --- | --- | --- |
| `KAT_PageSync` values per group | M / 2 (page pairs, base 15) | M (one page per slot) |
| Highest page number | 14 | 253 |
| Wide-glyph capacity | 3,570 | 64,515 |
| Cost per slot | 12 bits | 16 bits |

The installer's **Page format** setting defaults to **Auto**: it follows the built glyph atlas
(`atlas_meta.json`, else the page count), so you normally never touch it. The OSC client needs no
matching setting — it reads the format off the avatar's parameters and sends in that format.

## Menu and advanced menu

The **KAT Text** expression submenu (anchor position, text style, roll speed) is always installed. Tick **Install advanced menu**
in the installer to add an **Advanced** submenu inside it:

| Control | Parameter | Drives |
| --- | --- | --- |
| Billboard: Always / Never | `KAT_Adv_Billboard` (int, 0 = auto) | `_FaceViewerMode`: overrides the billboard the anchor radial sets (side speech-bubble positions never billboard) |
| Grow With Distance | `KAT_Adv_Grow` | `_GrowEnabled` |
| Show Box | `KAT_Adv_Box` | `_BoxEnabled` |
| Shrink Box To Text | `KAT_Adv_Shrink` | `_ShrinkToFit` |
| Bold Text | `KAT_Adv_Bold` | `_Weight` |
| Board Size (radial, 0.5 = normal) | `KAT_Adv_Size` | uniform scale of the whole text board (the `Constraint` parent of `Text`): 0.35x at 0, 1x at 0.5, 2.5x at 1 |

All are local-only (not synced, saved) expression parameters (1 int, 4 bool, 1 float), each with its own small animator layer holding explicit clips for
both states, so they behave the same with WD on or off. They only use material properties no other KAT layer animates.

## Board controls: hold, drop in world, drop and follow

Tick **Install board controls (hold / drop)** (needs VRChat SDK 3.5+ with VRC constraints, a humanoid avatar; the toggle is greyed
out otherwise). It adds a **Board** submenu inside **KAT Text**, driven by one synced int, `KAT_Board_Mode` (8 synced bits, not saved,
so the board starts docked):

| Menu item (radio) | Mode | What happens |
| --- | --- | --- |
| Reset: Back To Body | 0 | docked: the normal head / chest anchor and its radial, exactly as before. Ticked while docked; press it to send the board back |
| Hold: Left Hand / Right Hand | 1 / 2 | the board floats in front of that hand |
| Drop: In World | 3 | VRC constraint `Freeze To World`: stays exactly where it was when you switched |
| Drop: Follow Me | 4 | stays where you released it, but moves with the avatar root (offsets are rebaked when un-frozen) |

Typical use: tick *Hold*, move your hand, then tick *Drop: In World* or *Drop: Follow Me*; untick to dock again. Combine with the
Advanced menu's **Board Size** radial to resize it.

How it is built: `KAT_Board.cs` adds a `VRCParentConstraint` to the `Text` object (inactive while docked, so the anchor system is
untouched) plus its targets: `KAT_Board_HandL_Point` / `KAT_Board_HandR_Point` under the hand bones, and, inside one `KAT_Board_Targets` object under
`KillFrenzyAvatarText` (not on the avatar root), `KAT_Board_HandL` / `KAT_Board_HandR` (anchors that a `VRCPositionConstraint` pulls onto those points, so they
have the hand's position but the avatar's facing) and `KAT_Board_Follow` (in front of the chest). Sources are added the documented way,
`constraint.Sources.Add(new VRCConstraintSource(transform, weight))`, and `ApplyModifiedChanges()` is called after every scripted change (without it the
VRChat docs warn the changes may not be applied, which is why the first version of this had no sources). One animator layer, `KAT_Board_Mode`, has one state per mode and writes every constraint property in every state (same
result with WD on or off). Entering *Follow* goes through a quarter-second frozen "prep" state so the constraint can rebake its offsets
when it un-freezes. The VRC constraint types are found by name and the animation properties are discovered at install time with
`AnimationUtility.GetAnimatableBindings`; if your SDK differs, the installer logs what the constraint exposes instead of failing to compile.

If a mode does nothing, run **Tools > KAT > Check Board Controls (selected avatar)** with the avatar selected, and look at the console line it prints: it lists
the constraint's flags and every source with its transform and weight (the installer prints the same line when it installs). Three sources are expected.

Notes: the hold keeps the board upright and turns it about Y only (like the docked board), facing the way the avatar faces; the
frozen / followed board is visible to other players because the mode is synced; **this is experimental: check each mode in-game**.

## Emoji (BC7)

If the atlas folder has `color_page_*.png` files (the emoji, see the root README), **Build Glyph Array** also builds `KAT_ColorArray`
(BC7, sRGB, with an RGBA32 fallback) and turns on `KAT_COLOR_GLYPHS` on `KAT_Text_Paged`. Emoji pages count towards the page limit
(14 wide pages in format 2).

**The mipmaps use a Kaiser filter.** When a texture is built from script, Unity always makes box-filtered mips, and the importer's Kaiser
option only applies to textures it imports itself. So `KatMips` (in `KAT_Paged.cs`) makes the mips by hand, with a 2x Kaiser-windowed sinc
downsample, for both the SDF array (R8 / BC4) and the emoji array (BC7 / RGBA32). The emoji are filtered premultiplied, so their
transparent pixels can't darken the edges. Set `KatMips.UseKaiser = false` to go back to Unity's box mips.

## Installation (advanced)

Don't really have an advance way to install it to be honest, just use the installer and then tweak stuff

The `KAT_HeadAnchor` expression parameter is a synced 0–1 radial: 0 = chest, 0.2–0.7 = left/right
speech-bubble positions with a tail, 1 = top-of-head chatbox with no tail. Still have some features
undone/don't know how to do so feel free to add whatever feature you want and pull request

## How KAT works (brief)

- Up to 128 text slots, 32 columns × 4 rows. Pointer `KAT_Pointer` (1..32) selects which group of M
  slots (M = sync parameters, default 4) is being written; 255 clears everything.
- Each pointer write sends `KAT_CharSync0..M-1` glyph slots + `KAT_PageSync*` page values, quantized to
  8-bit floats. Persistence comes from animator states that hold their last written value (Write
  Defaults off), so only changed groups are ever rewritten.
- The shader reads the remembered `_CharN`/`_PageN` material values each frame, samples the SDF atlas,
  and draws text plus an optional box/border/tail.

## In-game keyboard: pages, halves and key legends

The keyboard has 64 glyph keys plus Space, Clear, Bksp, two Shift keys and two page arrows, and a small display (the atlas page number, and which
128-slot half of the page is on the keys). The glyph printed on each key is drawn live from the same glyph atlas the text uses, so it is right on every page.

* **Halves.** One page is split in two halves of 64 glyphs. The lower half (slots 64..127) is on the keys by default; hold **Shift** for the top half (slots 0..63).
* **Pages.** The arrows step through keyboard pages: **▲ = previous, ▼ = next**, wrapping around (▲ on page 0 goes to the very last page; ▼ on the last page goes back to page 0).
  The last pages are the emoji pages, in the same order as the atlas, and emoji are typed like any other wide glyph; their legends are drawn in colour on the keys. Page 0 (narrow, 128 slots) is one keyboard page. Every wide atlas page has
  up to 255 slots, so it is two keyboard pages (slots 0..127, then 128..254). Slots 253 and 254 of a wide page can't be typed from the keyboard.
* **Key map** (the same on every page, `tools/gen_keyboard.py` prints the full table): the 26 letters sit where QWERTY has them, and their key number K is the
  letter's place in the top half of page 0 (A..Z = 33..58); the other 38 keys take the remaining numbers in reading order. Key K types slot K in the top half and
  slot `64 + (K ^ 32)` in the lower half, so on page 0 Shift gives A..Z and no Shift gives a..z, and `@`/`` ` ``, `[`/`{`, `\`/`|`, `]`/`}` share keys.
  Digits and most punctuation therefore land in ASCII order after the letters are placed, not where a US keyboard has them.
* **Wide glyphs** take two slots (the glyph, then a FOLLOW marker) and have to start on an even slot. If the next slot is odd, the keyboard first types a blank to line up.
  Bksp removes two slots if the last thing typed was a wide glyph (it only remembers that for one glyph; after a Bksp it guesses from the page it is on).
* **Key presses** go through one small state per glyph key (layer `KAT_KeyboardKeys`) that sets `KAT_KeyboardKeyCode` / `KAT_KeyboardKeyDown` with parameter drivers. An earlier version animated those parameters from a Direct blend tree, which the typing states' Copy drivers can't read, so only Space typed.
* **Text on the keyboard.** The layer `KAT_TextOnKeyboard` finds the keyboard's source on the text constraint by name (it is the last source, not source 1) and weights it while the keyboard is on.
* **OSC always wins.** Text sent by OSC overwrites whatever the keyboard typed, and the keyboard resets its pointer when it sees one. The two are not made compatible.
* **Needs the paged atlas** (Build Glyph Array + Material) for the key legends. Without it the keys are blank; they still type.
* `Bksp` used to send glyph slot 97 as its "leave this slot alone" value, which also meant OSC could never write slot 97 while the keyboard was installed. It now uses slot 253
  and only in the keyboard's own pointer range.

Regenerate the keyboard art, the contact receivers and the shader's key table with `python3 tools/gen_keyboard.py` (needs Pillow). Layout changes go in that script.

