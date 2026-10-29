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
