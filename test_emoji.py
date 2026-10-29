#!/usr/bin/env python3
"""Tests for emoji, shortcodes and the format 3 symbol block.

Run it with:  python test_emoji.py
It only needs the standard library plus the project's own requirements: no OSC server, no Unity.
"""
import json
import os
import sys
import tempfile
import types
import unittest

import numpy as np
from PIL import Image

# katosc imports python-osc. Nothing here opens a socket, so if it isn't installed a stand-in is good enough.
try:
    import pythonosc  # noqa: F401
except ImportError:
    _p = types.ModuleType("pythonosc")
    _p.udp_client = types.SimpleNamespace(SimpleUDPClient=type("U", (), {"__init__": lambda s, *a: None, "send_message": lambda s, *a: None}))
    _p.osc_server = types.SimpleNamespace(ThreadingOSCUDPServer=object)
    _p.dispatcher = types.SimpleNamespace(Dispatcher=object)
    sys.modules["pythonosc"] = _p

import build_atlas
import build_charset
from kat_codec import FOLLOW, Charset
from kat_emoji import collect_emoji, normalize_alias, split_emoji
from katosc import KatOsc, KatOscConfig

SOB, THUMB = "😭", "👍🏽"
IMAGE_KEY = chr(0xF0000)


def make_charset(**kw):
    return Charset(" abc?:", "你好", color=[SOB, THUMB, IMAGE_KEY],
                   emoji_aliases={"sob": SOB, "thumbs": THUMB, "party": IMAGE_KEY}, **kw)


class CharsetEmoji(unittest.TestCase):
    def test_emoji_pages_come_right_after_the_wide_pages(self):
        cs = make_charset()
        self.assertEqual(cs.color_first_page, 2)          # page 0 is narrow, page 1 is wide text, page 2 is emoji
        self.assertEqual(cs.pages, 3)
        self.assertEqual(cs.sdf_pages, 2)
        self.assertEqual(cs.index_of(SOB), 2 * 255)
        self.assertTrue(cs.is_color(cs.index_of(SOB)))
        self.assertFalse(cs.is_color(cs.index_of("你")))
        self.assertEqual(cs.char_at(2 * 255 + 1), THUMB)

    def test_shortcodes_and_sequences_become_one_glyph(self):
        cs = make_charset()
        self.assertEqual(cs.index_of(cs.expand_emoji(":sob:")), cs.index_of(SOB))
        self.assertEqual(cs.expand_emoji(":SOB:"), SOB)                              # case doesn't matter
        self.assertEqual(cs.index_of(cs.expand_emoji(THUMB)), cs.index_of(THUMB))    # skin tone typed directly
        self.assertEqual(cs.expand_emoji(SOB + "\ufe0f"), SOB)                       # a stray variation selector goes away
        self.assertEqual(cs.expand_emoji(":nope: a:b"), ":nope: a:b")                # unknown shortcodes stay as typed
        self.assertEqual(len(cs.expand_emoji("x:sob:y")), 3)

    def test_a_charset_without_emoji_is_untouched(self):
        cs = Charset(" ab?", "你好")
        self.assertEqual(sorted(cs.to_json_dict()), ["format", "narrow", "page_format", "pages", "wide"])
        self.assertEqual(cs.expand_emoji(":sob:"), ":sob:")
        self.assertEqual(cs.pages, 2)

    def test_json_round_trip(self):
        cs = make_charset(page_format=2)
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "c.json")
            with open(path, "w", encoding="utf-8") as f:
                json.dump(cs.to_json_dict(), f, ensure_ascii=False)
            again = Charset.load(path)
        self.assertEqual(again.color, cs.color)
        self.assertEqual(again.emoji_aliases, cs.emoji_aliases)
        self.assertEqual(again.index_of(IMAGE_KEY), cs.index_of(IMAGE_KEY))

    def test_emoji_pages_use_up_page_numbers(self):
        full = "".join(chr(0x4E00 + i) for i in range(14 * 255))
        with self.assertRaises(ValueError):
            Charset(" a", full, color=[SOB])                   # format 2: 14 wide pages + 1 emoji page is one too many
        Charset(" a", full, page_format=3, color=[SOB])        # format 3 has room
        Charset(" a", full[:13 * 255], color=[SOB])            # 13 + 1 fits format 2

    def test_bad_charsets_are_refused(self):
        with self.assertRaises(ValueError):
            Charset(" a", "你", color=["你"])                              # already a text glyph
        with self.assertRaises(ValueError):
            Charset(" a", "你", color=[SOB, SOB])                          # twice
        with self.assertRaises(ValueError):
            Charset(" a", "你", color=[SOB], emoji_aliases={"x": THUMB})   # shortcode points at nothing
        with self.assertRaises(ValueError):
            Charset(" a", "你", color=[SOB], emoji_aliases={"a+b": SOB})   # no plus in shortcodes


class ClientLayout(unittest.TestCase):
    def setUp(self):
        cfg = KatOscConfig()
        cfg.osc_enable_server = False
        cfg.osc_enable_chatbox = False
        self.cs = make_charset()
        self.kat = KatOsc(cfg, self.cs, autostart=False)

    def test_a_shortcode_takes_two_slots_like_any_wide_glyph(self):
        slots = self.kat._layout(":sob:a")
        self.assertEqual(slots[0], self.cs.index_of(SOB))
        self.assertEqual(slots[1], FOLLOW)
        self.assertEqual(slots[2], self.cs.index_of("a"))
        self.assertEqual(self.kat.measure_text_slots(":sob:a", padded=False), 3)   # 6 characters typed, 3 slots used

    def test_skin_tone_and_image_emoji(self):
        slots = self.kat._layout(THUMB + ":party:")
        self.assertEqual(slots[0], self.cs.index_of(THUMB))
        self.assertEqual(slots[2], self.cs.index_of(IMAGE_KEY))
        self.assertEqual(self.kat.undrawable_chars(THUMB + ":sob: ab"), [])

    def test_unknown_shortcode_is_just_text(self):
        self.assertEqual(self.kat.undrawable_chars(":nope:"), ["n", "o", "p", "e"])


class EmojiInputs(unittest.TestCase):
    def test_split_emoji_keeps_each_emoji_in_one_piece(self):
        self.assertEqual(split_emoji("😭👍🏽❤️"), ["😭", "👍🏽", "❤️"])
        self.assertEqual(split_emoji("👨‍👩‍👧🇯🇵"), ["👨‍👩‍👧", "🇯🇵"])
        self.assertEqual(split_emoji("1️⃣"), ["1️⃣"])

    def test_shortcode_rules(self):
        self.assertEqual(normalize_alias(":Sob:"), "sob")
        for bad in ("has space", "a+b", "x!"):
            with self.assertRaises(ValueError):
                normalize_alias(bad)

    def test_typed_emoji_plus_a_folder(self):
        with tempfile.TemporaryDirectory() as d:
            for n, name in enumerate(("party.png", "Big_Blue.png")):
                Image.new("RGBA", (8, 8), (255, 0, n + 1, 255)).save(os.path.join(d, name))
            open(os.path.join(d, "notes.txt"), "w").close()
            es = collect_emoji(inline="🎉😭", emoji_dir=d)
        self.assertEqual(es.color[:2], ["🎉", "😭"])           # typed ones first, as typed
        self.assertEqual(sorted(es.aliases), ["big_blue", "party"])
        self.assertEqual(len(es.color), 4)                      # notes.txt is ignored
        self.assertEqual(set(es.sources.values()), {"party.png", "Big_Blue.png"})
        self.assertEqual(es.pages(), 1)

    def test_plus_in_a_file_name_is_an_error(self):
        with tempfile.TemporaryDirectory() as d:
            Image.new("RGBA", (8, 8)).save(os.path.join(d, "sob+cry.png"))
            with self.assertRaises(ValueError):
                collect_emoji(emoji_dir=d)

    def test_same_shortcode_twice_is_an_error(self):
        with tempfile.TemporaryDirectory() as d:
            Image.new("RGBA", (8, 8)).save(os.path.join(d, "sob.png"))
            Image.new("RGB", (8, 8)).save(os.path.join(d, "SOB.jpg"))
            with self.assertRaises(ValueError):
                collect_emoji(emoji_dir=d)


class IdenticalPictures(unittest.TestCase):
    def folder(self, d, *names, same=True):
        for i, n in enumerate(names):
            Image.new("RGBA", (16, 16), (255, 0, 0 if same else i, 255)).save(os.path.join(d, n))

    def test_two_shortcodes_for_one_picture_share_a_glyph(self):
        with tempfile.TemporaryDirectory() as d:
            self.folder(d, "sob.png", "cry.png")
            es = collect_emoji(emoji_dir=d)
        self.assertEqual(len(es.color), 1)
        self.assertEqual(es.aliases["sob"], es.aliases["cry"])
        self.assertEqual(es.merged, [("sob.png", "cry.png")])          # cry.png sorts first and owns the glyph

    def test_a_word_and_an_emoji_with_the_same_picture(self):
        with tempfile.TemporaryDirectory() as d:
            self.folder(d, "sob.png", "😭.png")
            es = collect_emoji(inline="😭", emoji_dir=d)
        self.assertEqual(es.color, ["😭"])                              # the emoji owns the glyph, :sob: points at it
        self.assertEqual(es.aliases, {"sob": "😭"})
        self.assertEqual(es.sources, {"😭": "😭.png"})

    def test_two_emoji_with_the_same_picture_become_a_synonym(self):
        with tempfile.TemporaryDirectory() as d:
            self.folder(d, "😂.png", "🤣.png")
            es = collect_emoji(inline="🤣", emoji_dir=d)
        self.assertEqual(es.color, ["😂"])                              # the typed 🤣 gave up its own slot
        self.assertEqual(es.synonyms, {"🤣": "😂"})
        cs = Charset(" abc", "你", color=es.color, emoji_aliases=es.aliases, color_sources=es.sources, emoji_synonyms=es.synonyms)
        self.assertEqual(cs.index_of(cs.expand_emoji("a🤣")[1]), cs.index_of("😂"))
        again = Charset(" abc", "你", **{k: v for k, v in zip(("color", "emoji_aliases", "color_sources", "emoji_synonyms"),
                                                             (cs.color, cs.emoji_aliases, cs.color_sources, cs.emoji_synonyms))})
        self.assertEqual(again.emoji_synonyms, {"🤣": "😂"})
        self.assertEqual(cs.to_json_dict()["emoji_synonyms"], {"🤣": "😂"})

    def test_different_pictures_stay_separate(self):
        with tempfile.TemporaryDirectory() as d:
            self.folder(d, "a1.png", "b1.png", same=False)
            es = collect_emoji(emoji_dir=d)
        self.assertEqual(len(es.color), 2)
        self.assertEqual(es.merged, [])

    def test_synonym_must_point_at_an_emoji(self):
        with self.assertRaises(ValueError):
            Charset(" a", "你", color=[SOB], emoji_synonyms={THUMB: "x"})


class _FakeKat:
    """Just enough of KatOsc for AppController.fit: one slot per character, 10 slots."""
    text_length = 10
    expired_count = 0

    def measure_text_slots(self, text, padded=True):
        return len(text)

    def set_text(self, text):
        self.text = text

    def undrawable_chars(self, text):
        return []


class OverflowForShortcodes(unittest.TestCase):
    def setUp(self):
        from kat_app import AppController
        self.ctl = AppController(_FakeKat())

    def type(self, text):
        return self.ctl.on_text_changed(text)

    def test_a_plain_overflow_is_still_refused(self):
        self.assertTrue(self.type("abcdefghij").text == "abcdefghij")
        self.assertEqual(self.type("abcdefghijk").text, "abcdefghij")

    def test_an_open_shortcode_may_run_over(self):
        text = "abcdefgh:a_long_emoji_name"
        update = self.type(text)
        self.assertEqual(update.text, text)
        self.assertFalse(update.rewrite)

    def test_the_colon_must_be_within_the_last_34_characters(self):
        tail = ":" + "x" * 33                      # the colon is the 34th-last character
        self.assertEqual(self.type("abcdefgh" + tail).text, "abcdefgh" + tail)
        self.ctl._last_text = ""
        far = "abcdefgh" + ":" + "x" * 34         # the 35th-last: too far
        self.assertNotEqual(self.type(far).text, far)

    def test_a_colon_as_the_last_character_does_not_open_one(self):
        self.ctl._last_text = ""
        self.assertNotEqual(self.type("abcdefghijklmnopq:").text, "abcdefghijklmnopq:")

    def test_never_more_than_32_over(self):
        self.ctl._last_text = ""
        text = "abcdefgh:" + "x" * 40
        self.assertLessEqual(len(self.type(text).text), 10 + 32)


class Priority(unittest.TestCase):
    def folder(self, d, *names, same=False):
        for i, n in enumerate(names):          # a different colour per file, unless same=True (then they're byte-identical)
            Image.new("RGBA", (16, 16), (255, 0, 0 if same else i, 255)).save(os.path.join(d, n))

    def test_a_picture_named_after_an_emoji_beats_the_font(self):
        with tempfile.TemporaryDirectory() as d:
            self.folder(d, "😭.png", "sob.png")
            es = collect_emoji(inline="😭😂", emoji_dir=d)
        self.assertEqual(es.color[:2], ["😭", "😂"])             # the typed emoji keep their place
        self.assertEqual(es.overridden, ["😭"])
        self.assertEqual(es.sources["😭"], "😭.png")
        self.assertNotIn("😂", es.sources)                       # that one still comes from a font
        self.assertEqual(len(es.color), 3)                       # plus the :sob: picture
        self.assertEqual(es.aliases["sob"], es.color[2])

    def test_an_emoji_named_picture_also_adds_the_emoji_on_its_own(self):
        with tempfile.TemporaryDirectory() as d:
            self.folder(d, "🎉.png")
            es = collect_emoji(emoji_dir=d)
        self.assertEqual(es.color, ["🎉"])
        self.assertEqual(es.sources, {"🎉": "🎉.png"})
        self.assertEqual(es.overridden, [])

    def test_variation_selectors_dont_stop_a_match(self):
        with tempfile.TemporaryDirectory() as d:
            self.folder(d, "❤.png")
            es = collect_emoji(inline="❤️", emoji_dir=d)
        self.assertEqual(es.color, ["❤️"])
        self.assertEqual(es.overridden, ["❤️"])

    def test_two_pictures_for_one_emoji_is_an_error(self):
        with tempfile.TemporaryDirectory() as d:
            self.folder(d, "❤.png", "❤️.png")
            with self.assertRaises(ValueError):
                collect_emoji(emoji_dir=d)

    def test_a_name_that_is_neither_a_shortcode_nor_an_emoji_is_an_error(self):
        with tempfile.TemporaryDirectory() as d:
            self.folder(d, "two words.png")
            with self.assertRaises(ValueError):
                collect_emoji(emoji_dir=d)

    def test_emoji_beat_plain_text_glyphs(self):
        ranked = list("你好世界") + ["★", "☆"]
        narrow, wide = build_charset.build_sets(ranked, page_format=3, exclude={"★"}, extra="★", punct="★，")
        self.assertNotIn("★", narrow + wide)                     # dropped from ranked, extra, punct and the symbol block
        self.assertIn("☆", narrow + wide)                        # its neighbour is untouched
        self.assertIn("，", wide)

    def test_the_picture_really_is_what_gets_baked(self):
        font = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
        if not os.path.exists(font):
            self.skipTest("no test font on this machine")
        with tempfile.TemporaryDirectory() as d:
            self.folder(d, "😭.png")
            es = collect_emoji(inline="😭", emoji_dir=d)
            cs = Charset(build_charset.NARROW_BASE, "你好", color=es.color, emoji_aliases=es.aliases, color_sources=es.sources)
            cs_path = os.path.join(d, "charset.json")
            with open(cs_path, "w", encoding="utf-8") as f:
                json.dump(cs.to_json_dict(), f, ensure_ascii=False)
            out = os.path.join(d, "atlas")
            self.assertEqual(build_atlas.render_atlas(cs_path, font, cell_size=32, spread=4, supersample=2, out=out, emoji_dir=d), 0)
            page = np.asarray(Image.open(os.path.join(out, "color_page_00.png")))
            self.assertEqual(tuple(page[16, 16, :3]), (255, 0, 0))   # the red picture, not whatever a font would draw


class Symbols(unittest.TestCase):
    RANKED = [chr(0x4E00 + i) for i in range(300)]

    def test_format_2_never_gets_symbols_on_auto(self):
        _, wide = build_charset.build_sets(self.RANKED, page_format=2)
        self.assertFalse(set(wide) & build_charset.SYMBOL_SET)

    def test_format_3_with_room_to_spare_gets_them(self):
        _, wide = build_charset.build_sets(self.RANKED, page_format=3)
        for ch in "ⅠⅡⅢⅣⅤⅥⅦⅧⅨⅩ①→★":
            self.assertIn(ch, wide)
        self.assertEqual(wide[:len(build_charset.WIDE_PUNCT)], build_charset.WIDE_PUNCT)   # symbols go on the end, nothing shifts

    def test_off_and_on(self):
        _, off = build_charset.build_sets(self.RANKED, page_format=3, symbols="off")
        self.assertFalse(set(off) & build_charset.SYMBOL_SET)
        _, on = build_charset.build_sets(self.RANKED, page_format=2, symbols="on")
        self.assertIn("Ⅳ", on)

    def test_not_added_when_the_format_is_nearly_full(self):
        many = [chr(0x20000 + i) for i in range(64515 - 25 - 100)]    # plane 2 is all wide; leaves far less than SYMBOLS_MIN_FREE
        _, wide = build_charset.build_sets(many, page_format=3)
        self.assertFalse(set(wide) & build_charset.SYMBOL_SET)

    def test_the_font_can_veto_symbols_and_emoji_are_never_doubled(self):
        _, wide = build_charset.build_sets(self.RANKED, page_format=3, symbol_ok=lambda ch: ch != "Ⅳ", exclude="★")
        self.assertNotIn("Ⅳ", wide)
        self.assertNotIn("★", wide)
        self.assertIn("Ⅲ", wide)

    def test_symbols_are_really_unique(self):
        self.assertEqual(len(build_charset.SYMBOLS), len(set(build_charset.SYMBOLS)))


class AtlasPages(unittest.TestCase):
    def test_image_emoji_end_to_end(self):
        font = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
        if not os.path.exists(font):
            self.skipTest("no test font on this machine")
        with tempfile.TemporaryDirectory() as d:
            emoji_dir = os.path.join(d, "emoji")
            os.mkdir(emoji_dir)
            Image.new("RGBA", (300, 120), (10, 200, 30, 255)).save(os.path.join(emoji_dir, "bar.png"))   # big and not square on purpose
            cs_path = os.path.join(d, "charset.json")
            es = collect_emoji(emoji_dir=emoji_dir)
            cs = Charset(build_charset.NARROW_BASE, "你好", color=es.color, emoji_aliases=es.aliases, color_sources=es.sources)
            with open(cs_path, "w", encoding="utf-8") as f:
                json.dump(cs.to_json_dict(), f, ensure_ascii=False)
            out = os.path.join(d, "atlas")
            self.assertEqual(build_atlas.render_atlas(cs_path, font, cell_size=32, spread=4, supersample=2, out=out, emoji_dir=emoji_dir), 0)
            meta = json.load(open(os.path.join(out, "atlas_meta.json")))
            self.assertEqual(meta["color_pages"], 1)
            self.assertEqual(meta["color_page_start"], cs.color_first_page)
            self.assertEqual(meta["pages"], cs.pages)
            page = np.asarray(Image.open(os.path.join(out, "color_page_00.png")))
            self.assertEqual(page.shape, (16 * 32, 16 * 32, 4))
            cell = page[:32, :32]                                     # slot 0 is the top-left cell
            self.assertGreater(cell[16, 16, 3], 250)                  # solid in the middle
            self.assertEqual(tuple(cell[16, 16, :3]), (10, 200, 30))
            self.assertEqual(cell[0, 0, 3], 0)                        # empty at the corner...
            self.assertEqual(tuple(cell[0, 0, :3]), (10, 200, 30))    # ...but carrying the emoji's colour, so no dark edges
            self.assertEqual(page[:32, 32:64, 3].max(), 0)            # the next cell is unused
            # build again without emoji: the old emoji page mustn't be left lying around
            plain = Charset(build_charset.NARROW_BASE, "你好")
            with open(cs_path, "w", encoding="utf-8") as f:
                json.dump(plain.to_json_dict(), f, ensure_ascii=False)
            build_atlas.render_atlas(cs_path, font, cell_size=32, spread=4, supersample=2, out=out)
            self.assertFalse(os.path.exists(os.path.join(out, "color_page_00.png")))


if __name__ == "__main__":
    unittest.main(verbosity=2)
