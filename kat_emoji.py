"""
Emoji and other full-colour glyphs.

A normal glyph is a single-colour SDF. Emoji can't be, because they keep their own colours, so they get RGBA "colour pages" of
their own. Unity packs those into a separate BC7 texture array. A colour glyph is drawn like any wide glyph: two slots, square,
[glyph][FOLLOW].

There are two ways to get emoji into a charset, and that's all:

  --emoji "😭😂❤️"     type them. They're drawn from your font if it has them, otherwise from the system's colour emoji font.
  --emoji-dir DIR      a folder of images. A file named after a word is a shortcode: DIR/sob.png lets people type :sob: and get that
                       picture. A file named after an emoji is a picture for that emoji: DIR/😭.png. Big images are scaled down
                       to fit a cell.

Shortcodes are lower case letters, digits, _ and -. No spaces, and no "+".

Identical pictures share one glyph. Every file in --emoji-dir is hashed (md5 of the file's bytes). Files with the same hash are
the same picture, so they get ONE slot on the colour page and the extra names become aliases of it:

  sob.png + cry.png (same bytes)      -> one glyph, :sob: and :cry: both type it
  sob.png + 😭.png  (same bytes)      -> one glyph; the emoji is the glyph, :sob: is a shortcode for it
  😭.png + 😂.png   (same bytes)      -> one glyph; 😂 is a "synonym" that typing swaps for 😭's glyph

Only byte-identical files merge. The same picture saved as a PNG and as a WEBP has different bytes, so it stays two glyphs.

When two sources want the same emoji, this is who wins (highest first):

  1. a picture in --emoji-dir named after the emoji (DIR/😭.png)
  2. your font, if it has the emoji
  3. the system's colour emoji font

An emoji you ask for also beats a plain text glyph: if it's in the charset's text set too (the ranked characters, --extra, the
symbol block), the text copy is dropped, so you never get an error about it.

charset.json gets three optional fields (an ordinary charset has none of them):

  "color"          the glyph keys, in slot order. A key is either an emoji (possibly several code points, like a skin-tone
                   or flag sequence) or a private-use character for an image.
  "emoji_aliases"  shortcode -> key
  "emoji_synonyms" emoji (or emoji sequence) -> key, for emoji that are just another name for a picture that already has a key
  "color_sources"  key -> image file name, for the keys that come from DIR
"""

from __future__ import annotations

import hashlib
import re
from dataclasses import dataclass, field
from pathlib import Path

IMAGE_EXTS = (".png", ".webp", ".jpg", ".jpeg", ".gif", ".bmp")

# Images get a private-use character as their key (plane 15). Emoji that are several code points long get a single stand-in
# character from SEQUENCE_BASE up, but only inside the client; those never end up in charset.json.
CUSTOM_BASE = 0xF0000
SEQUENCE_BASE = 0xF8000
CUSTOM_LIMIT = SEQUENCE_BASE - CUSTOM_BASE

ALIAS_RE = re.compile(r"[a-z0-9_\-]{1,32}")

VS16 = "\ufe0f"
ZWJ = "\u200d"
KEYCAP = "\u20e3"


def normalize_alias(name: str) -> str:
    """':Sob:' -> 'sob'. Raises ValueError if the name couldn't be typed as a shortcode."""
    n = name.strip().strip(":").strip().lower()
    if not ALIAS_RE.fullmatch(n):
        raise ValueError(f"{name!r} can't be a shortcode: use 1-32 of a-z 0-9 _ - (no spaces, no '+')")
    return n


def split_emoji(text: str) -> list[str]:
    """Cut a string of emoji into one key per emoji. Skin tones, variation selectors, ZWJ sequences, keycaps, flags and tag
    sequences stay in one piece. It isn't a full grapheme segmenter, but it's enough for a list of emoji."""
    out: list[str] = []
    i, n = 0, len(text)
    while i < n:
        j = i + 1
        cp = ord(text[i])
        if 0x1F1E6 <= cp <= 0x1F1FF and j < n and 0x1F1E6 <= ord(text[j]) <= 0x1F1FF:
            j += 1                                     # a flag is two regional indicators
        while j < n:
            c = text[j]
            o = ord(c)
            if c in (VS16, KEYCAP) or 0x1F3FB <= o <= 0x1F3FF or 0xE0020 <= o <= 0xE007F:
                j += 1
            elif c == ZWJ and j + 1 < n:
                j += 2                                 # the joiner and whatever it joins
            else:
                break
        out.append(text[i:j])
        i = j
    return [k for k in out if not k.isspace()]


@dataclass
class EmojiSet:
    color: list[str] = field(default_factory=list)            # glyph keys, in slot order
    aliases: dict[str, str] = field(default_factory=dict)     # shortcode -> key
    sources: dict[str, str] = field(default_factory=dict)     # key -> image file name
    overridden: list[str] = field(default_factory=list)       # typed emoji whose picture came from --emoji-dir instead of a font
    synonyms: dict[str, str] = field(default_factory=dict)    # emoji -> key it is another name for (identical picture)
    merged: list[tuple[str, str]] = field(default_factory=list)  # (file, file it was merged into), for reporting

    def __bool__(self) -> bool:
        return bool(self.color)

    def pages(self) -> int:
        return -(-len(self.color) // 255)


def _plain(key: str) -> str:
    """The emoji without its variation selector, so ❤ and ❤️ count as the same one when we compare them."""
    return key.replace(VS16, "")


def file_digest(path: Path) -> str:
    """md5 of the file's bytes. It's only used to spot identical files, so speed matters and security doesn't."""
    h = hashlib.md5(usedforsecurity=False)
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def collect_emoji(*, inline: str = "", emoji_dir: str | None = None) -> EmojiSet:
    """Gathers the emoji from --emoji and --emoji-dir. Problems (a bad file name, two files with the same shortcode) are raised
    here, while the charset is being built, instead of turning into a mystery in game. Files with identical bytes share one glyph."""
    es = EmojiSet()

    for key in split_emoji(inline or ""):
        if key not in es.color:
            es.color.append(key)

    if not emoji_dir:
        return es
    d = Path(emoji_dir)
    if not d.is_dir():
        raise FileNotFoundError(f"emoji folder not found: {d}")

    # pass 1: look at every file name, so a bad one is reported before anything is built
    seen_alias: dict[str, str] = {}
    seen_emoji: dict[str, str] = {}
    entries: list[tuple[Path, str, str]] = []           # (file, "word" | "emoji", the shortcode or the emoji)
    for p in sorted(d.iterdir(), key=lambda q: q.name.lower()):
        if not p.is_file() or p.suffix.lower() not in IMAGE_EXTS:
            continue
        stem = p.stem
        if ALIAS_RE.fullmatch(stem.lower()):            # a word: it's a shortcode
            alias = stem.lower()
            if alias in seen_alias:
                raise ValueError(f"{p.name} and {seen_alias[alias]} would both be :{alias}:")
            seen_alias[alias] = p.name
            entries.append((p, "word", alias))
            continue
        keys = split_emoji(stem)                        # not a word: is it an emoji?
        if len(keys) == 1 and ord(keys[0][0]) > 0x7F:
            emoji = keys[0]
            if _plain(emoji) in seen_emoji:
                raise ValueError(f"{p.name} and {seen_emoji[_plain(emoji)]} are both pictures for {emoji}")
            seen_emoji[_plain(emoji)] = p.name
            entries.append((p, "emoji", emoji))
            continue
        normalize_alias(stem)                           # neither: this raises the "can't be a shortcode" error

    # pass 2: group identical pictures. The first file of a group that is named after an emoji is the one that owns the glyph
    # (so the emoji is a real key and the shortcodes point at it); with no such file, the first file by name owns it.
    groups: dict[str, list[tuple[Path, str, str]]] = {}
    for entry in entries:
        groups.setdefault(file_digest(entry[0]), []).append(entry)

    custom = 0                                          # how many private-use keys have been handed out
    for group in groups.values():
        owner = next((e for e in group if e[1] == "emoji"), group[0])
        okind, ovalue = owner[1], owner[2]
        if okind == "word":
            if custom >= CUSTOM_LIMIT:
                raise ValueError(f"too many emoji images (the limit is {CUSTOM_LIMIT})")
            key = chr(CUSTOM_BASE + custom)
            custom += 1
            es.color.append(key)
        else:
            typed = next((k for k in es.color if _plain(k) == _plain(ovalue)), None)
            if typed is not None:                       # you typed it too: the picture wins over the font
                key = typed
                es.overridden.append(typed)
            else:
                key = ovalue
                es.color.append(key)
        es.sources[key] = owner[0].name

        for p, kind, value in group:
            if p is owner[0]:
                continue
            es.merged.append((p.name, owner[0].name))
            if kind == "word":
                es.aliases[value] = key
                continue
            if _plain(value) == _plain(key):
                continue
            typed = next((k for k in es.color if _plain(k) == _plain(value)), None)
            if typed is not None and typed != key:      # typed too, but its picture is the same as another glyph: use that one
                es.color.remove(typed)
                es.overridden.append(typed)
            es.synonyms[value] = key

        if okind == "word":
            es.aliases[ovalue] = key
    return es
